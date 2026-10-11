using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace MenUI
{
    /// Everything one panel owns: the reactive runtime, the element trees and
    /// the pipeline (style → layout → paint → render). Also the factory for
    /// elements and reactive values, which `MenUIApp` and `Component` forward.
    public sealed class MenUIContext
    {
        internal readonly ReactiveRuntime runtime = new ReactiveRuntime();
        // Fonts: one shaper per family registered in MenUI Settings (index 0
        // is the default), and the atlas textures they draw from.
        internal readonly TextShaper[] shapers;
        internal readonly string[] fontFamilies;
        internal const int AtlasSlotCount = 8;
        private readonly Texture[] atlasSlots = new Texture[AtlasSlotCount];
        private bool atlasesChanged;
        private bool warnedAtlasSlots;
        /// Set whenever TMP shaped something this frame, so the panel renders
        /// once more next frame in case new glyphs reach the GPU late.
        internal bool shapedThisFrame;
        /// Time spent inside TextMeshPro and how many strings it shaped.
        internal long shapeTicks;
        internal int shapeCount;
        internal readonly PanelRenderer renderer;
        /// Where images are copied to be drawn (see ImageAtlas).
        internal readonly ImageAtlas images;
        // Downloaded images (MenUIApp's WebImages): start a download again,
        // let a texture go. Null without one (tests).
        internal Action<ImageSource> downloadImage;
        internal Action<ImageSource> unloadImage;
        // Downloads asked for while painting, started before the next layout.
        private readonly List<ImageSource> imageRequests = new List<ImageSource>();
        internal readonly StyleResolver resolver = new StyleResolver();
        internal readonly LayoutEngine layout = new LayoutEngine();
        internal readonly Painter painter;

        // Physical pixels per logical pixel, and the panel in physical pixels
        // (changed only by Resize).
        internal float scale;
        internal int bufferWidth;
        internal int bufferHeight;
        private Signal<float> scaleSignal;

        /// The scale, read so that a binding reruns when Resize changes it
        /// (bindings that turn logical pixels into physical ones).
        internal float TrackedScale { get { return scaleSignal.Value; } }

        internal readonly Box root;
        internal readonly Box overlayRoot;

        internal bool needsStyle = true;
        internal bool needsLayout = true;
        internal bool needsPaint = true;
        private bool renderAgain;

        // Work queues filled by Element.MarkLayoutDirty / MarkPaintDirty.
        internal readonly List<Element> layoutQueue = new List<Element>();
        internal readonly List<Element> paintQueue = new List<Element>();
        internal readonly List<Element> styleQueue = new List<Element>();
        internal readonly List<StyleSheet> styleSheets = new List<StyleSheet>();
        /// MenUI's own rules (a browser's user-agent style sheet): the first
        /// sheet, so every rule of yours wins over it. Components add their
        /// rules when first used.
        internal readonly StyleSheet defaultSheet = new StyleSheet();
        // Which components' rules it holds (ComponentStyles bits), and their colors.
        private int componentStyles;
        private Theme theme = MenUI.Theme.Light;
        private Signal<Theme> themeSignal;
        internal int reconcileStamp;
        /// Per-panel state of add-ons (MaterialDesign.Of keeps its instance here).
        internal readonly Dictionary<string, object> extensions = new Dictionary<string, object>();
        /// The set or order of records changes: repaint everything.
        internal bool paintStructureDirty = true;
        /// Record ranges to paint again (see Painter.RepaintRange), and every
        /// element that has one.
        internal readonly List<Element> rangeRequests = new List<Element>();
        internal readonly List<Element> rangeOwners = new List<Element>();

        internal void RequestRangePaint(Element owner)
        {
            if (!owner.rangeRequested)
            {
                owner.rangeRequested = true;
                rangeRequests.Add(owner);
            }
            needsPaint = true;
        }

        // Published for input: elements in paint order (bottom first), and
        // versions that tell the router when to rebuild or re-test.
        internal readonly List<Element> paintOrder = new List<Element>();
        internal int hitVersion;
        internal int layoutVersion;

        // Input: a device-specific source feeding the device-independent router.
        internal readonly InputRouter router;
        private IPointerSource pointerSource;
        private readonly PanelSurface surface;
        private readonly PointerSample[] samples = new PointerSample[InputRouter.MaxPointers];
        internal readonly TextInputCoordinator textInput;

        // Transform groups (scrolling, translate) and time for animations.
        internal readonly TransformGroups groups;
        internal float time;
        internal int frame;
        /// Virtual list rows given new content this frame (diagnostics).
        internal int rowsFilled;
        private readonly List<IFrameTask> animating = new List<IFrameTask>();
        private readonly Action tickTasks;
        private float tickSeconds;
        internal readonly StyleAnimations styleAnimations;
        internal readonly BackgroundWork background;
        private readonly Signal<float> viewerDistance;
        /// Bumped after every layout, for bindings that read laid-out sizes
        /// (a scrollbar thumb's travel).
        internal readonly Signal<int> layoutTick;

        // Timings of the last Update, in stopwatch ticks (see MenUIApp.logPerformance).
        internal long styleTicks, layoutTicks, paintTicks, renderTicks, tickTicks, inputTicks;
        /// For the periodic summary: panel renders so far, and the frame
        /// tasks of the last frame that ran and that waited (suspended).
        internal int renders;
        internal int tasksRun, tasksWaiting;
        internal bool paintedThisFrame, groupsMovedThisFrame;
        /// Log what scroll views receive and compute (MenUIApp.logPerformance).
        internal bool diagnostics;

        internal MenUIContext(float scale, int bufferWidth, int bufferHeight, TextShaper[] shapers, string[] fontFamilies,
            PanelRenderer renderer,
            PanelSurface surface = null)
        {
            this.surface = surface;
            if (surface != null)
            {
                surface.LogicalWidth = bufferWidth / scale;
                surface.LogicalHeight = bufferHeight / scale;
            }
            router = new InputRouter(this);
            textInput = new TextInputCoordinator(this);
            styleAnimations = new StyleAnimations(this);
            background = new BackgroundWork(this);
            viewerDistance = new Signal<float>(runtime, 100000f);
            tickTasks = () => TickTasks();
            groups = new TransformGroups(bufferWidth, bufferHeight);
            layoutTick = new Signal<int>(runtime, 0);
            themeSignal = new Signal<Theme>(runtime, theme);
            scaleSignal = new Signal<float>(runtime, scale);
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = new PointerSample();
            }
            this.scale = scale;
            this.bufferWidth = bufferWidth;
            this.bufferHeight = bufferHeight;
            this.shapers = shapers;
            this.fontFamilies = fontFamilies;
            if (shapers != null)
            {
                for (int i = 0; i < shapers.Length; i++)
                {
                    shapers[i].context = this;
                }
            }
            this.renderer = renderer;
            images = new ImageAtlas(this, renderer);
            // Without a renderer (tests), the records are painted all the same.
            painter = new Painter(renderer != null ? renderer.records : new RecordBuffer(4096));

            // Both roots fill the panel; the overlay root is painted last and
            // ignores every clip and stacking context of the main tree.
            root = new Box(this);
            overlayRoot = new Box(this);
            overlayRoot.inlineStyle.SetEnum(StyleProperty.PointerEvents, (int)PointerEventsMode.None);
            // CSS's initial font size, 16 logical pixels (the computed default
            // is physical): what everything inherits unless styled.
            root.FontSize(16f);
            overlayRoot.FontSize(16f);
            root.MarkStyleDirty();
            overlayRoot.MarkStyleDirty();
            AddStyleSheet(defaultSheet);
        }

        /// Logical size of the panel (what 100% width and height mean).
        public float LogicalWidth { get { return bufferWidth / scale; } }
        public float LogicalHeight { get { return bufferHeight / scale; } }

        /// Installs the device layer (null: no pointer input).
        public void SetPointerSource(IPointerSource source)
        {
            pointerSource = source;
            if (source != null && surface != null)
            {
                source.Attach(surface);
            }
        }

        /// Changes the panel's size (physical pixels) and scale (physical
        /// pixels per logical pixel) while it runs: everything is styled, laid
        /// out and drawn again at the new size — cheap enough to animate. A
        /// larger scale draws the same logical layout with more pixels (zoom);
        /// a larger size gives the layout more logical room.
        public void Resize(int width, int height, float newScale)
        {
            width = Mathf.Clamp(width, 16, 8192);
            height = Mathf.Clamp(height, 16, 8192);
            newScale = Mathf.Clamp(newScale, 0.25f, 8f);
            if (width == bufferWidth && height == bufferHeight && newScale == scale)
            {
                return;
            }
            bufferWidth = width;
            bufferHeight = height;
            scale = newScale;
            scaleSignal.Value = newScale;
            if (renderer != null)
            {
                renderer.Resize(width, height);
            }
            groups.SetPanelSize(width, height);
            if (surface != null)
            {
                surface.LogicalWidth = LogicalWidth;
                surface.LogicalHeight = LogicalHeight;
                if (textInput.host != null)
                {
                    textInput.host.Attach(surface);   // fits its canvas to the new logical size
                }
            }
            // Pixel snapping depends on the scale: resolve every style again,
            // and lay everything out afresh (no measurement is valid any more).
            root.subtreeStyleDirty = true;
            root.MarkStyleDirty();
            overlayRoot.subtreeStyleDirty = true;
            overlayRoot.MarkStyleDirty();
            int pass = layout.NextPass;
            InvalidateLayout(root, pass);
            InvalidateLayout(overlayRoot, pass);
            root.MarkLayoutDirty(false);
            overlayRoot.MarkLayoutDirty(false);
            paintStructureDirty = true;
            needsPaint = true;
            hitVersion++;
        }

        private static void InvalidateLayout(Element e, int pass)
        {
            e.layoutDirty = true;
            e.savedAnswerCount = -1;
            e.stackCount = 0;
            e.validSincePass = pass;
            e.measureOverflowed = false;
            for (int i = 0; i < e.children.Count; i++)
            {
                InvalidateLayout(e.children[i], pass);
            }
        }

        /// Installs the text entry layer (null: text boxes cannot be typed in).
        public void SetTextInputHost(ITextInputHost host)
        {
            textInput.host = host;
            if (host != null && surface != null)
            {
                host.Attach(surface);
            }
        }

        /// Polls the pointer source and routes its samples, then lets the text
        /// input host follow; call once a frame before Update, so what the
        /// handlers change is drawn this frame.
        internal void ProcessInput()
        {
            if (pointerSource != null)
            {
                int count = pointerSource.Poll(samples);
                router.Process(samples, count);
            }
            textInput.Update();
        }

        /// Runs the pipeline stages that have work to do; call once a frame.
        // ---- fonts ---------------------------------------------------------------------

        /// The shaper of a font index (the default font for an unknown one).
        internal TextShaper ShaperFor(int fontIndex)
        {
            return fontIndex > 0 && fontIndex < shapers.Length ? shapers[fontIndex] : shapers[0];
        }

        /// CSS font-family: the first family of the comma-separated list that
        /// is registered (quotes allowed); otherwise the default font, with a
        /// warning once per unknown list.
        internal int ResolveFontFamily(string families)
        {
            if (fontFamilies == null || string.IsNullOrEmpty(families))
            {
                return 0;
            }
            string[] candidates = families.Split(',');
            for (int c = 0; c < candidates.Length; c++)
            {
                string name = candidates[c].Trim().Trim('"', '\'');
                for (int i = 0; i < fontFamilies.Length; i++)
                {
                    if (string.Equals(fontFamilies[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }
            if (!unknownFamilies.Contains(families))
            {
                unknownFamilies.Add(families);
                Debug.LogWarning("[MenUI] font-family \"" + families + "\" is not registered in MenUI Settings; using "
                    + fontFamilies[0] + ".");
            }
            return 0;
        }

        private readonly List<string> unknownFamilies = new List<string>();

        /// The atlas slot of a glyph texture, assigning a free slot to a new
        /// one (the shader samples up to AtlasSlotCount atlases).
        internal int AtlasSlot(Texture atlas)
        {
            int free = -1;
            for (int i = 0; i < AtlasSlotCount; i++)
            {
                if (atlasSlots[i] == atlas)
                {
                    return i;
                }
                if (free < 0 && atlasSlots[i] == null)
                {
                    free = i;
                }
            }
            if (free < 0)
            {
                if (!warnedAtlasSlots)
                {
                    warnedAtlasSlots = true;
                    Debug.LogWarning("[MenUI] more than " + AtlasSlotCount + " glyph atlases in use; some text will look wrong.");
                }
                return 0;
            }
            atlasSlots[free] = atlas;
            atlasesChanged = true;
            return free;
        }

        /// Adds a style sheet; its rules apply to every element of the panel.
        /// Later sheets win ties with earlier ones.
        public void AddStyleSheet(StyleSheet sheet)
        {
            if (styleSheets.Contains(sheet))
            {
                return;
            }
            sheet.order = styleSheets.Count;
            styleSheets.Add(sheet);
            // Everything matches again.
            root.subtreeStyleDirty = true;
            root.MarkStyleDirty();
            overlayRoot.subtreeStyleDirty = true;
            overlayRoot.MarkStyleDirty();
        }

        /// Adds a style sheet right after MenUI's built-in one, below every
        /// sheet added so far (a design system's defaults, which your own
        /// sheets override).
        internal void AddDefaultStyleSheet(StyleSheet sheet)
        {
            if (styleSheets.Contains(sheet))
            {
                return;
            }
            styleSheets.Insert(1, sheet);
            for (int i = 0; i < styleSheets.Count; i++)
            {
                styleSheets[i].order = i;
            }
            root.subtreeStyleDirty = true;
            root.MarkStyleDirty();
            overlayRoot.subtreeStyleDirty = true;
            overlayRoot.MarkStyleDirty();
        }

        /// Everything matches its rules again (a sheet's rules were replaced).
        internal void RematchStyles()
        {
            root.subtreeStyleDirty = true;
            root.MarkStyleDirty();
            overlayRoot.subtreeStyleDirty = true;
            overlayRoot.MarkStyleDirty();
        }

        internal bool IsAncestorClass(string name)
        {
            for (int i = 0; i < styleSheets.Count; i++)
            {
                if (styleSheets[i].ancestorClasses.Contains(name)) return true;
            }
            return false;
        }

        internal bool AnySheetUsesAncestorTags()
        {
            for (int i = 0; i < styleSheets.Count; i++)
            {
                if (styleSheets[i].hasAncestorTags) return true;
            }
            return false;
        }

        /// Resolves the styles of the elements queued by MarkStyleDirty (and
        /// what they pass inherited values to) — not the whole tree.
        internal void ResolveStyles()
        {
            needsStyle = false;
            resolver.ResolveQueue(this);
        }

        /// Advances time-based work (animations, scroll momentum); call once
        /// a frame before input. What the tasks change takes effect together
        /// (one batch): each binding reruns at most once per frame.
        internal void Tick(float seconds)
        {
            time += seconds;
            frame++;
            rowsFilled = 0;
            tickSeconds = seconds;
            runtime.Batch(tickTasks);
        }

        private void TickTasks()
        {
            tasksRun = 0;
            tasksWaiting = 0;
            // Backwards: tasks started meanwhile are appended and wait a frame.
            for (int i = animating.Count - 1; i >= 0; i--)
            {
                if (i >= animating.Count)
                {
                    continue;
                }
                IFrameTask task = animating[i];
                Owner scope = task.Scope;
                if (scope != null && scope.IsSuspended())
                {
                    tasksWaiting++;
                    continue;   // out of sight: waits, costs nothing
                }
                tasksRun++;
                if (!task.Tick(tickSeconds))
                {
                    animating.RemoveAt(i);
                }
            }
        }

        // ---- layers (GPU transform groups for transforms and fades) ----------------

        /// Gives `e` a transform group while its style needs one — translated,
        /// scaled, or with a fade or transform animating — and releases it
        /// after. With a group, translate, scale and opacity are applied by
        /// the GPU: changing them neither lays out nor repaints.
        internal void UpdateLayer(Element e)
        {
            bool needs = e.computed.IsTransformed || (e.animator != null && e.animator.AnimatesLayer());
            if (e.translateGroup == TransformGroups.Root)
            {
                if (!needs)
                {
                    return;
                }
                int group = groups.Allocate();
                if (group == TransformGroups.Root)
                {
                    return;   // none left: drawn untransformed, opacity by the painter
                }
                e.translateGroup = group;
                e.styleLayer = true;
                // Its subtree now draws into the group (and its opacity moves
                // there): arranged and drawn again once.
                e.MarkLayoutDirty(false);
                if (!e.layerCleanupRegistered && e.owner != null)
                {
                    e.layerCleanupRegistered = true;
                    e.owner.OnCleanup(() => ReleaseLayer(e));
                }
            }
            else if (!needs && e.styleLayer)
            {
                ReleaseLayer(e);
                return;
            }
            SyncLayer(e);
        }

        internal void ReleaseLayer(Element e)
        {
            if (!e.styleLayer || e.translateGroup == TransformGroups.Root)
            {
                return;
            }
            groups.SetStyleTransform(e.translateGroup, 0f, 0f, 1f, 1f);
            groups.Free(e.translateGroup);
            e.translateGroup = TransformGroups.Root;
            e.styleLayer = false;
            e.MarkLayoutDirty(false);
        }

        /// Hands the element's translate, scale and opacity to its group.
        internal void SyncLayer(Element e)
        {
            int group = e.translateGroup;
            if (group == TransformGroups.Root)
            {
                return;
            }
            ComputedStyle s = e.computed;
            // A prepared box is drawn at zero opacity until it is shown.
            groups.SetStyleTransform(group, s.numbers[StyleProperty.TranslateX], s.numbers[StyleProperty.TranslateY],
                s.Scale, e.inert ? 0f : s.Opacity);
        }

        /// Whether `e` is in the panel's tree (or the overlay's).
        internal bool IsAttached(Element e)
        {
            Element top = e;
            while (top.parent != null)
            {
                top = top.parent;
            }
            return top == root || top == overlayRoot;
        }

        /// Runs `task` every frame (before input) until it reports done.
        internal void StartAnimating(IFrameTask task)
        {
            if (!animating.Contains(task))
            {
                animating.Add(task);
            }
        }

        internal void Update()
        {
            styleTicks = 0;
            layout.arrangedCount = 0;
            paintedThisFrame = false;
            groupsMovedThisFrame = false;
            layoutTicks = 0;
            paintTicks = 0;
            renderTicks = 0;
            shapeTicks = 0;
            shapeCount = 0;
            long mark = Stopwatch.GetTimestamp();

            // An image copy did not fit last frame: make room (its holders
            // repaint).
            if (images.compactionPending)
            {
                images.Compact();
            }
            if (imageRequests.Count > 0)
            {
                for (int i = 0; i < imageRequests.Count; i++)
                {
                    if (downloadImage != null && imageRequests[i].state == ImageSource.StateUnloaded)
                    {
                        downloadImage(imageRequests[i]);
                    }
                }
                imageRequests.Clear();
            }

            // Layout can write reactive values (scroll extents) whose bindings
            // restyle something; one more round settles that in this frame.
            for (int round = 0; round < 2; round++)
            {
                if (needsStyle)
                {
                    ResolveStyles();
                    long now = Stopwatch.GetTimestamp();
                    styleTicks += now - mark;
                    mark = now;
                }
                if (needsLayout)
                {
                    needsLayout = false;
                    layout.Run(this);
                    // Only a layout that moved something is news (hit testing
                    // tests again, bindings on sizes rerun).
                    if (layout.arrangedCount > 0)
                    {
                        layoutVersion++;
                        layoutTick.Value = layoutTick.Peek() + 1;
                        needsPaint = true;
                    }
                    long now = Stopwatch.GetTimestamp();
                    layoutTicks += now - mark;
                    mark = now;
                }
                if (!needsStyle && !needsLayout)
                {
                    break;
                }
            }

            bool render = renderAgain;
            if (needsPaint)
            {
                needsPaint = false;
                if (atlasesChanged && renderer != null)
                {
                    atlasesChanged = false;
                    for (int i = 0; i < AtlasSlotCount; i++)
                    {
                        renderer.SetAtlas(i, atlasSlots[i]);
                    }
                }
                painter.Paint(this);
                paintedThisFrame = true;
                long now = Stopwatch.GetTimestamp();
                paintTicks = now - mark;
                mark = now;
                render = true;
            }
            // Scrolling and translation: only the group arrays change.
            if (groups.dirty)
            {
                groups.Resolve();
            }
            if (groups.uploadPending && renderer != null)
            {
                groups.uploadPending = false;
                renderer.SetGroups(groups.offsets, groups.clips);
                groupsMovedThisFrame = true;
                render = true;
            }
            if (render && renderer != null)
            {
                renders++;
                renderer.Render();
                renderTicks = Stopwatch.GetTimestamp() - mark;
            }
            // New glyphs may reach the GPU a frame late; draw once more then.
            renderAgain = shapedThisFrame;
            shapedThisFrame = false;
        }

        // ---- factory ---------------------------------------------------------

        public Box Div()
        {
            return new Box(this);
        }

        public TextElement Text(string content)
        {
            return new TextElement(this, content);
        }

        public TextElement Text(Func<string> content)
        {
            TextElement element = new TextElement(this, string.Empty);
            Bind(() => element.SetContent(content()));
            return element;
        }

        /// A downloaded image that was let go is drawn again: download it
        /// before the next frame's layout (not in the middle of a paint).
        internal void RequestImage(ImageSource source)
        {
            if (!imageRequests.Contains(source))
            {
                imageRequests.Add(source);
            }
        }

        /// An image of `texture` (CSS `img`, see ImageElement).
        public ImageElement Image(Texture texture)
        {
            return new ImageElement(this, images.SourceFor(texture, new Rect()));
        }

        /// The part `pixels` of `texture` (x and y from its top-left): one
        /// sprite of a sheet.
        public ImageElement Image(Texture texture, Rect pixels)
        {
            return new ImageElement(this, images.SourceFor(texture, pixels));
        }

        /// The image `texture()` names, following the signals it reads (null:
        /// nothing drawn).
        public ImageElement Image(Func<Texture> texture)
        {
            ImageElement element = new ImageElement(this, null);
            Bind(() => element.SetSource(images.SourceFor(texture(), new Rect())));
            return element;
        }

        public ImageElement Image(ImageSource source)
        {
            return new ImageElement(this, source);
        }

        /// Draws shapes, arcs, lines and ripples (see Graphic); `draw` reruns
        /// when a signal it read changes.
        public Graphic Graphic(Action<GraphicCanvas> draw)
        {
            return new Graphic(this, draw);
        }

        /// A one-line text box editing `value` (CSS `input`).
        public TextBox TextBox(Signal<string> value)
        {
            return new TextBox(this, value, false);
        }

        /// A text box of several lines editing `value` (CSS `textarea`).
        public TextBox TextArea(Signal<string> value)
        {
            return new TextBox(this, value, true);
        }

        // ---- theme ----------------------------------------------------------------------

        /// The colors of the built-in components (see Theme). Reading it in a
        /// binding follows SetTheme: `.Background(() => UI.Theme.Background)`.
        public Theme Theme { get { return themeSignal.Value; } }

        /// Changes the built-in components' look, including those already built.
        public void SetTheme(Theme value)
        {
            if (value == null)
            {
                return;
            }
            theme = value;
            themeSignal.Value = value;
            defaultSheet.Clear();
            for (int kind = 1; kind <= ComponentStyles.Last; kind <<= 1)
            {
                if ((componentStyles & kind) != 0)
                {
                    ComponentStyles.Add(defaultSheet, theme, kind);
                }
            }
            // Every element matches again (the rules are new objects).
            root.subtreeStyleDirty = true;
            root.MarkStyleDirty();
            overlayRoot.subtreeStyleDirty = true;
            overlayRoot.MarkStyleDirty();
        }

        /// Adds a component's rules to the built-in sheet the first time it is built.
        internal void EnsureStyles(int kind)
        {
            if ((componentStyles & kind) != 0)
            {
                return;
            }
            componentStyles |= kind;
            ComponentStyles.Add(defaultSheet, theme, kind);
        }

        public Signal<T> Signal<T>(T initial)
        {
            return new Signal<T>(runtime, initial);
        }

        public Computed<T> Computed<T>(Func<T> compute)
        {
            return new Computed<T>(runtime, runtime.currentOwner, compute);
        }

        public Effect Effect(Action body)
        {
            Effect effect = new Effect(runtime, runtime.currentOwner, body);
            effect.Run();
            return effect;
        }

        public void Batch(Action action)
        {
            runtime.Batch(action);
        }

        // ---- animation (see Animation/) ----------------------------------------------

        /// A number that animates: `.AnimateTo(target, animation)`, read `.Value`
        /// in bindings.
        public AnimatedFloat Animated(float initial)
        {
            return new AnimatedFloat(this, initial);
        }

        /// A color that animates.
        public AnimatedColor Animated(Color initial)
        {
            return new AnimatedColor(this, initial);
        }

        /// Calls `frame` every frame for `seconds` with the progress (0 → 1,
        /// eased), ending with 1; then `onComplete`.
        public Playback Animate(float seconds, Action<float> frame, Easing easing = null, Action onComplete = null)
        {
            Playback playback = new Playback(this, Mathf.Max(0f, seconds), easing, frame, onComplete);
            StartAnimating(playback);
            return playback;
        }

        /// Calls `tick` every frame (with the seconds since the last one)
        /// until stopped, or until the scope that called this goes away.
        public Playback EveryFrame(Action<float> tick)
        {
            Playback playback = new Playback(this, -1f, null, tick, null);
            StartAnimating(playback);
            return playback;
        }

        public T Untrack<T>(Func<T> read)
        {
            return runtime.Untrack(read);
        }

        public void OnCleanup(Action cleanup)
        {
            runtime.currentOwner.OnCleanup(cleanup);
        }

        internal void Bind(Action body)
        {
            Effect effect = new Effect(runtime, runtime.currentOwner, body);
            effect.Run();
        }

        /// Shows `then` while `condition` is true, `otherwise` (if any) while
        /// it is false. A branch is built once per switch; its bindings stop
        /// when it is removed. A removed branch with `:exit-to` styles plays
        /// its exit first (the new branch is already there meanwhile).
        public Region Show(Func<bool> condition, Func<Element> then, Func<Element> otherwise = null)
        {
            Region region = new Region(this);
            Owner scope = runtime.currentOwner;
            bool initialized = false;
            bool last = false;
            Owner branch = null;
            Element shown = null;
            Effect watcher = new Effect(runtime, scope, () =>
            {
                bool value = condition();
                if (initialized && value == last)
                {
                    return;
                }
                initialized = true;
                last = value;
                if (branch != null)
                {
                    Element leaving = shown;
                    Leave(branch, () => region.RemoveChild(leaving));
                }
                branch = null;
                shown = null;
                Func<Element> build = value ? then : otherwise;
                if (build != null)
                {
                    Owner owner = new Owner(runtime, scope);
                    Element built = null;
                    runtime.RunWithOwner(owner, () => { built = build(); });
                    branch = owner;
                    shown = built;
                    if (built != null)
                    {
                        region.AppendChild(built);
                    }
                }
            });
            watcher.Run();
            return region;
        }

        /// Rebuilds its content whenever something `build` read changes (the
        /// old content plays its `:exit-to` transitions, if any, first).
        public Region Dynamic(Func<Element> build)
        {
            Region region = new Region(this);
            Owner scope = runtime.currentOwner;
            Owner branch = null;
            Element shown = null;
            Effect watcher = new Effect(runtime, scope, () =>
            {
                if (branch != null)
                {
                    Element leaving = shown;
                    Leave(branch, () => region.RemoveChild(leaving));
                }
                // What `build` reads is tracked by this effect; what it
                // creates belongs to a branch of its own, so the old content
                // can outlive a rerun while it exits.
                Owner owner = new Owner(runtime, scope);
                Owner saved = runtime.currentOwner;
                runtime.currentOwner = owner;
                Element built = null;
                try
                {
                    built = build();
                }
                finally
                {
                    runtime.currentOwner = saved;
                }
                branch = owner;
                shown = built;
                if (built != null)
                {
                    region.AppendChild(built);
                }
            });
            watcher.Run();
            return region;
        }

        /// Shows page `index()` of `pages` (tabs, steps). Each page is built
        /// the first time it is shown and then kept: switching back only
        /// hides and shows it — no rebuild, no restyle, its layout cached —
        /// far cheaper than Dynamic, for the memory (and running bindings) of
        /// the hidden pages. With `preload`, the other pages are built ahead
        /// in the background (see Deferred): styled and laid out out of
        /// sight, a little every frame, so even a first visit is quick.
        public Region Pages(Func<int> index, params Func<Element>[] pages)
        {
            return Pages(index, false, pages);
        }

        public Region Pages(Func<int> index, bool preload, params Func<Element>[] pages)
        {
            Region region = new Region(this);
            Owner scope = runtime.currentOwner;
            // Each page sits in a holder: `display: contents` when shown (the
            // page lays out as the region's child), `none` when hidden, and
            // an out-of-sight box while it is being preloaded.
            Box[] holders = new Box[pages.Length];
            Owner[] owners = new Owner[pages.Length];
            DeferredLoad[] loads = new DeferredLoad[pages.Length];
            int shown = -1;
            // A hidden page is suspended: its animations wait, it costs nothing.
            Action<int> show = i =>
            {
                Box holder = holders[i];
                if (holder.inert)
                {
                    RevealContents(holder);
                }
                holder.Display(DisplayMode.Contents);
                if (owners[i] != null)
                {
                    owners[i].suspended = false;
                }
            };
            Action<int> hide = i =>
            {
                holders[i].Display(DisplayMode.None);
                if (owners[i] != null)
                {
                    owners[i].suspended = true;
                }
            };
            Action<int> build = i =>
            {
                if (i < 0 || i >= pages.Length)
                {
                    return;
                }
                if (loads[i] != null && !loads[i].finished)
                {
                    // Wanted now: stop preloading it, build it at once instead.
                    loads[i].owner.Dispose();
                    region.RemoveChild(holders[i]);
                    loads[i] = null;
                    holders[i] = null;
                }
                if (holders[i] != null)
                {
                    return;
                }
                Box holder = Div().Display(DisplayMode.None);
                Owner owner = new Owner(runtime, scope);
                owner.suspended = true;
                Element built = null;
                runtime.RunWithOwner(owner, () => { built = pages[i](); });
                holder.Child(built);
                holders[i] = holder;
                owners[i] = owner;
                region.AppendChild(holder);
            };
            Effect watcher = new Effect(runtime, scope, () =>
            {
                int current = index();
                if (current == shown)
                {
                    return;
                }
                build(current);
                if (shown >= 0 && shown < holders.Length && holders[shown] != null)
                {
                    hide(shown);
                }
                if (current >= 0 && current < holders.Length && holders[current] != null)
                {
                    show(current);
                }
                shown = current;
            });
            watcher.Run();
            if (preload)
            {
                for (int i = 0; i < pages.Length; i++)
                {
                    if (holders[i] != null)
                    {
                        continue;
                    }
                    int page = i;
                    Box holder = PrepareHidden(Div());
                    holders[i] = holder;
                    region.AppendChild(holder);
                    Owner owner = new Owner(runtime, scope);
                    owners[i] = owner;
                    loads[i] = background.Start(owner, holder, load =>
                    {
                        load.Content.Child(pages[page]());
                        return Task.CompletedTask;
                    }, () =>
                    {
                        // Ready: hidden (and suspended) until chosen, then shown at once.
                        RevealContents(holder);
                        if (shown != page)
                        {
                            hide(page);
                        }
                        else
                        {
                            show(page);
                        }
                    });
                }
            }
            return region;
        }

        // ---- background loading ------------------------------------------------------------

        /// Milliseconds a frame may spend loading in the background (Deferred,
        /// Pages preloading, Near), MenUI's own work that frame included;
        /// at least one step runs every frame however busy it is.
        public float BackgroundBudget
        {
            get { return background.budget; }
            set { background.budget = Mathf.Max(0f, value); }
        }

        /// Shows `placeholder` (if any) while `build` fills `load.Content`
        /// in the background — a little every frame, out of sight — then
        /// shows the content in its place. The content box sits where the
        /// placeholder is while it is prepared (laid out and drawn at zero
        /// opacity now and then), so showing it moves and redraws nothing.
        /// The placeholder plays its `:exit-to`. `load.Content` is a block box
        /// in the placeholder's place: style it (FlexGrow, ...) as the
        /// content's place needs.
        public Region Deferred(Func<DeferredLoad, Task> build, Func<Element> placeholder = null)
        {
            Region region = new Region(this);
            Owner scope = runtime.currentOwner;
            Owner placeholderOwner = new Owner(runtime, scope);
            Element shownPlaceholder = null;
            runtime.RunWithOwner(placeholderOwner, () =>
            {
                shownPlaceholder = placeholder != null ? placeholder() : null;
            });
            if (shownPlaceholder == null)
            {
                shownPlaceholder = Div();   // an empty mark of the content's place
            }
            Box holder = Prepare(Div(), shownPlaceholder, false, scope);
            region.AppendChild(holder);
            region.AppendChild(shownPlaceholder);
            Owner owner = new Owner(runtime, scope);
            background.Start(owner, holder, build, () =>
            {
                Reveal(holder);
                owner.suspended = false;
                Element leaving = shownPlaceholder;
                Leave(placeholderOwner, () => region.RemoveChild(leaving));
            });
            return region;
        }

        /// Deferred with a plain builder: it runs in one background step.
        public Region Deferred(Func<Element> build, Func<Element> placeholder = null)
        {
            return Deferred(load =>
            {
                Element built = build();
                if (built != null)
                {
                    load.Content.Child(built);
                }
                return Task.CompletedTask;
            }, placeholder);
        }

        /// A box prepared out of sight on `anchor` (see Element.anchor): laid
        /// out there, drawn at zero opacity through a GPU group of its own
        /// (kept for the box's life), never hit. Without a free group it is
        /// not drawn until shown.
        private Box Prepare(Box holder, Element anchor, bool matchHeight, Owner scope)
        {
            holder.inert = true;
            holder.anchor = anchor;
            holder.matchAnchorHeight = matchHeight;
            // A stacking context of its own keeps its records together: what
            // is added inside repaints only its range.
            holder.Absolute().Display(DisplayMode.Block).ZIndex(0);
            if (!holder.ownsRecordRange)
            {
                holder.ownsRecordRange = true;
                rangeOwners.Add(holder);
            }
            if (holder.translateGroup == TransformGroups.Root)
            {
                int group = groups.Allocate();
                if (group != TransformGroups.Root)
                {
                    holder.translateGroup = group;
                    holder.styleLayer = false;
                    scope.OnCleanup(() =>
                    {
                        if (holder.translateGroup == group)
                        {
                            groups.Free(group);
                            holder.translateGroup = TransformGroups.Root;
                        }
                    });
                }
            }
            holder.MarkLayoutDirty(true);
            SyncLayer(holder);
            return holder;
        }

        /// Shows a prepared box in its anchor's place (in the flow, first):
        /// its layout and records are ready, only its opacity changes.
        private void Reveal(Box holder)
        {
            holder.inert = false;
            holder.anchor = null;
            holder.Position(PositionMode.Relative).Display(DisplayMode.Block);
            SyncLayer(holder);
            hitVersion++;   // can be hit now (range repaints of it out of sight did not say)
            if (holder.translateGroup == TransformGroups.Root)
            {
                // Not drawn while prepared (no GPU group was free): drawn now.
                paintStructureDirty = true;
            }
            needsPaint = true;
        }

        /// A page being preloaded (Pages): laid out at the region's width,
        /// not drawn; shown later as `display: contents`.
        private static Box PrepareHidden(Box holder)
        {
            holder.inert = true;
            holder.Absolute().Left(0).Top(0).Right(0);
            return holder;
        }

        private void RevealContents(Box holder)
        {
            holder.inert = false;
            holder.Position(PositionMode.Relative).Left(Length.Auto).Top(Length.Auto).Right(Length.Auto)
                .Display(DisplayMode.Contents);
            paintStructureDirty = true;
            needsPaint = true;
        }

        /// Runs the background loads for what is left of this frame's budget
        /// (call at the end of the frame, after Update).
        internal void RunBackground(double spentMilliseconds)
        {
            background.RunSlice(spentMilliseconds);
        }

        // ---- distance ------------------------------------------------------------------------

        /// How far the viewer (the local player's head) is from the panel, in
        /// meters (a dependency when read in a binding; updated a few times a
        /// second by MenUIApp; very far until known).
        public float ViewerDistance { get { return viewerDistance.Value; } }

        internal void SetViewerDistance(float meters)
        {
            // Small moves are not changes: bindings rerun only on real ones.
            if (Mathf.Abs(meters - viewerDistance.Peek()) >= 0.05f)
            {
                viewerDistance.Value = meters;
            }
        }

        /// Shows `near` within `meters` of the viewer and `far` (if any)
        /// beyond (with a little slack, so standing on the line does not
        /// flicker). The near content is built in the background the first
        /// time (the far one shows meanwhile); going away hides it — or,
        /// with `unloadWhenFar`, removes it (its memory is freed; it is
        /// built again next time).
        public Region Near(float meters, Func<Element> near, Func<Element> far = null, bool unloadWhenFar = false)
        {
            return Near(meters, load =>
            {
                Element built = near();
                if (built != null)
                {
                    load.Content.Child(built);
                }
                return Task.CompletedTask;
            }, far, unloadWhenFar);
        }

        public Region Near(float meters, Func<DeferredLoad, Task> near, Func<Element> far = null, bool unloadWhenFar = false)
        {
            Region region = new Region(this);
            Owner scope = runtime.currentOwner;
            float leave = meters * 1.1f + 0.25f;
            bool isNear = false;
            bool initialized = false;
            // Both contents live in boxes of their own that stay: the one in
            // view is in the flow; the other sits on it, prepared (laid out,
            // drawn at zero opacity). Switching swaps the two — no element
            // added or removed, nothing redrawn but what moved.
            Box farHolder = Div();
            Owner farOwner = new Owner(runtime, scope);
            runtime.RunWithOwner(farOwner, () =>
            {
                Element built = far != null ? far() : null;
                if (built != null)
                {
                    farHolder.Child(built);
                }
            });
            region.AppendChild(farHolder);
            Owner nearOwner = null;
            Box nearHolder = null;
            bool nearReady = false;
            // The side out of view is suspended: its animations wait.
            Action showNear = () =>
            {
                Reveal(nearHolder);
                nearOwner.suspended = false;
                Prepare(farHolder, nearHolder, false, scope);
                farOwner.suspended = true;
            };
            Action showFar = () =>
            {
                if (farHolder.inert)
                {
                    Reveal(farHolder);
                }
                farOwner.suspended = false;
            };
            Effect watcher = new Effect(runtime, scope, () =>
            {
                float distance = viewerDistance.Value;
                bool next = isNear ? distance <= leave : distance <= meters;
                if (initialized && next == isNear)
                {
                    return;
                }
                initialized = true;
                isNear = next;
                if (isNear)
                {
                    if (nearHolder != null && nearReady)
                    {
                        showNear();
                        return;
                    }
                    if (nearHolder == null)
                    {
                        // Built in the background on the far content, which stays until it is ready.
                        nearOwner = new Owner(runtime, scope);
                        Box holder = Prepare(Div(), farHolder, false, nearOwner);
                        nearHolder = holder;
                        region.InsertChild(0, holder);
                        background.Start(nearOwner, holder, near, () =>
                        {
                            nearReady = true;
                            if (isNear)
                            {
                                showNear();
                            }
                        });
                    }
                    return;
                }
                if (nearHolder == null)
                {
                    return;
                }
                if (unloadWhenFar || !nearReady)
                {
                    showFar();
                    Box leaving = nearHolder;
                    Owner owner = nearOwner;
                    nearHolder = null;
                    nearOwner = null;
                    nearReady = false;
                    Leave(owner, () => region.RemoveChild(leaving));
                }
                else
                {
                    // Kept: prepared again on the far content, so coming back is only a switch.
                    showFar();
                    Prepare(nearHolder, farHolder, false, nearOwner);
                    nearOwner.suspended = true;
                }
            });
            watcher.Run();
            return region;
        }

        /// Ends a scope that showed something: its elements with `:exit-to`
        /// styles (in the tree, displayed) get that state, and once their
        /// transitions have played, `owner` is disposed and `remove` runs.
        /// Without any, both happen now. Returns whether it waits.
        internal bool Leave(Owner owner, Action remove)
        {
            List<Element> leaving = CollectExits(owner, null);
            if (leaving == null)
            {
                owner.Dispose();
                remove();
                return false;
            }
            for (int i = 0; i < leaving.Count; i++)
            {
                leaving[i].SetPseudoState(PseudoState.ExitTo, true);
            }
            StartAnimating(new ExitTask(leaving, owner, remove));
            return true;
        }

        private List<Element> CollectExits(Owner owner, List<Element> into)
        {
            List<Element> candidates = owner.exitElements;
            if (candidates != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    Element e = candidates[i];
                    if (e.computed.Display != DisplayMode.None && IsAttached(e))
                    {
                        if (into == null)
                        {
                            into = new List<Element>();
                        }
                        into.Add(e);
                    }
                }
            }
            if (owner.children != null)
            {
                for (int i = 0; i < owner.children.Count; i++)
                {
                    into = CollectExits(owner.children[i], into);
                }
            }
            return into;
        }

        /// `e` has an `:exit-to` style: its scope's Leave waits for it.
        internal void RegisterExit(Element e)
        {
            if (e.registeredExit || e.owner == null)
            {
                return;
            }
            e.registeredExit = true;
            if (e.owner.exitElements == null)
            {
                e.owner.exitElements = new List<Element>();
            }
            e.owner.exitElements.Add(e);
        }

        /// Puts `content` on the overlay layer (above everything, unclipped)
        /// for as long as the calling scope lives. Position it with
        /// `.Position(PositionMode.Absolute)` and insets.
        public Region Overlay(Func<Element> content)
        {
            Region placeholder = new Region(this);
            Element built = null;
            Owner owner = new Owner(runtime, runtime.currentOwner);
            runtime.RunWithOwner(owner, () => { built = content(); });
            if (built != null)
            {
                overlayRoot.AppendChild(built);
                owner.OnCleanup(() => overlayRoot.RemoveChild(built));
            }
            return placeholder;
        }

        // ---- components (see Components/) ----------------------------------------------

        /// A button (tag `button`) with a text label. Add `.OnClick(...)`.
        public Box Button(string label, ButtonVariant variant = ButtonVariant.Secondary)
        {
            return Controls.Button(this, Text(label), variant);
        }

        /// A button with any content (an icon and a label, ...).
        public Box Button(Element content, ButtonVariant variant = ButtonVariant.Secondary)
        {
            return Controls.Button(this, content, variant);
        }

        /// A check box with an optional label; clicking either toggles `isChecked`.
        public Box Checkbox(Signal<bool> isChecked, string label = null)
        {
            return Controls.Checkbox(this, isChecked, label);
        }

        /// An on/off switch with an optional label.
        public Box Switch(Signal<bool> isOn, string label = null)
        {
            return Controls.Switch(this, isOn, label);
        }

        /// One radio button: selected while `selected` equals `value`, and
        /// clicking it sets that. Several with the same signal form a group.
        public Box Radio<T>(Signal<T> selected, T value, string label = null)
        {
            return Controls.Radio(this, selected, value, label);
        }

        /// A slider from `minimum` to `maximum` (`step` 0: continuous).
        public Box Slider(Signal<float> value, float minimum = 0f, float maximum = 1f, float step = 0f)
        {
            return Controls.Slider(this, value, minimum, maximum, step);
        }

        /// A bar filled to `progress` (0 to 1).
        public Box ProgressBar(Func<float> progress)
        {
            return Controls.ProgressBar(this, progress);
        }

        /// Three pulsing dots, for "working on it" (animates while shown).
        public Box Spinner()
        {
            return Controls.Spinner(this);
        }

        /// A row of tabs; `selected` is the index of the current one. Show
        /// the matching content yourself (`Dynamic(() => ...)`).
        public Box Tabs(Signal<int> selected, params string[] labels)
        {
            return Controls.Tabs(this, selected, labels);
        }

        /// A thin horizontal line.
        public Box Divider()
        {
            return Controls.Divider(this);
        }

        /// A drop-down list: shows `label(selected)`, and a menu of `options`
        /// on click.
        public Box Select<T>(Signal<T> selected, List<T> options, Func<T, string> label)
        {
            return Popups.Select(this, selected, options, label);
        }

        /// A drop-down list of strings.
        public Box Select(Signal<string> selected, params string[] options)
        {
            return Popups.Select(this, selected, new List<string>(options), text => text);
        }

        /// A modal dialog with `content`, shown while `open` is true; clicking
        /// the dimmed backdrop closes it (unless `closeOnBackdrop` is false).
        /// The returned region can go anywhere in the tree, or nowhere.
        public Region Dialog(Signal<bool> open, Func<Element> content, bool closeOnBackdrop = true)
        {
            return Popups.Dialog(this, open, content, closeOnBackdrop);
        }

        /// Shows `message` at the bottom of the panel for `seconds`.
        public void Toast(string message, float seconds = 3f)
        {
            if (toasts == null)
            {
                EnsureStyles(ComponentStyles.Toast);
                toasts = new Signal<List<ToastMessage>>(runtime, new List<ToastMessage>());
                runtime.RunWithOwner(runtime.root, () =>
                {
                    Overlay(() => Div().Tag("toast-stack").Child(
                        For(() => toasts.Value, item => item.id,
                            (item, index) => Div().Tag("toast").Child(Text(item.Peek().text)))));
                });
            }
            ToastMessage toast = new ToastMessage(++lastToastId, message);
            List<ToastMessage> shown = new List<ToastMessage>(toasts.Peek());
            shown.Add(toast);
            toasts.Value = shown;
            After(seconds, () =>
            {
                List<ToastMessage> remaining = new List<ToastMessage>(toasts.Peek());
                remaining.Remove(toast);
                toasts.Value = remaining;
            });
        }

        private Signal<List<ToastMessage>> toasts;
        private int lastToastId;

        /// Runs `action` once after `seconds` (cancel with the result).
        internal Delay After(float seconds, Action action)
        {
            Delay delay = new Delay(this, seconds, action);
            StartAnimating(delay);
            return delay;
        }

        /// Where an element is on the panel now, in logical pixels (its
        /// layout box mapped by scrolling, translation and scale).
        internal void LogicalRect(Element e, out float left, out float top, out float width, out float height)
        {
            float zoom = groups.ScaleOf(e.group);
            left = groups.ScreenX(e.group, e.x) / scale;
            top = groups.ScreenY(e.group, e.y) / scale;
            width = e.width * zoom / scale;
            height = e.height * zoom / scale;
        }

        // ---- scrolling ---------------------------------------------------------------

        /// State shared by a scroll view, its scrollbars and your code.
        public ScrollState ScrollState()
        {
            return new ScrollState(this);
        }

        /// A box whose content scrolls (content only; add scrollbars separately).
        public ScrollView ScrollView(ScrollState state, ScrollAxis axis = ScrollAxis.Vertical)
        {
            return new ScrollView(this, state, axis);
        }

        /// A scrollbar for `state`: a track with a draggable thumb (press the
        /// track to page). It hides itself while there is nothing to scroll.
        /// In VRChat it is the way to scroll with a mouse — the wheel turns
        /// the view there. Its look comes from the theme: tags `scrollbar`
        /// (with class `vertical` or `horizontal`) and `scrollbar-thumb`.
        public Box Scrollbar(ScrollState state, ScrollAxis axis = ScrollAxis.Vertical)
        {
            EnsureStyles(ComponentStyles.Scrollbar);
            bool vertical = axis != ScrollAxis.Horizontal;
            Box track = Div().Tag("scrollbar").Class(vertical ? "vertical" : "horizontal");
            Box thumb = Div().Tag("scrollbar-thumb").Absolute();
            if (vertical)
            {
                thumb.Left(0).Right(0).Top(0).MinHeight(32)
                    .Height(() => Length.Percent(Proportion(state.ViewportHeight, state.ContentHeight)));
            }
            else
            {
                thumb.Top(0).Bottom(0).Left(0).MinWidth(32)
                    .Width(() => Length.Percent(Proportion(state.ViewportWidth, state.ContentWidth)));
            }
            // Shown when there is something to scroll — or will be: content
            // being loaded in the background counts (see ScrollState).
            track.Display(() => (vertical ? state.NeedsBarY : state.NeedsBarX) ? DisplayMode.Block : DisplayMode.None);

            // Thumb position: moved on the GPU like the content.
            thumb.Translate(
                () => vertical ? 0f : ThumbPosition(state.OffsetX, state.MaxOffsetX, track.width - thumb.width),
                () => vertical ? ThumbPosition(state.OffsetY, state.MaxOffsetY, track.height - thumb.height) : 0f);

            int grabPointer = -1;
            float grabStart = 0f;
            float grabOffset = 0f;
            thumb.OnPointerDown(e =>
            {
                grabPointer = e.PointerId;
                grabStart = vertical ? e.Y : e.X;
                grabOffset = vertical ? state.OffsetY : state.OffsetX;
                state.StopMomentum();
                e.StopPropagation();
            });
            thumb.OnPointerMove(e =>
            {
                if (e.PointerId != grabPointer)
                {
                    return;
                }
                float free = ((vertical ? track.height - thumb.height : track.width - thumb.width)) / scale;
                float maximum = vertical ? state.MaxOffsetY : state.MaxOffsetX;
                if (free > 0f)
                {
                    float target = grabOffset + ((vertical ? e.Y : e.X) - grabStart) * maximum / free;
                    if (vertical) state.ScrollTo(state.OffsetX, target);
                    else state.ScrollTo(target, state.OffsetY);
                }
                e.StopPropagation();
            });
            thumb.OnPointerUp(e =>
            {
                if (e.PointerId == grabPointer)
                {
                    grabPointer = -1;
                }
                e.StopPropagation();
            });
            // Pressing the track pages toward the pointer.
            track.OnPointerDown(e =>
            {
                float thumbStart = ThumbPosition(vertical ? state.OffsetY : state.OffsetX,
                    vertical ? state.MaxOffsetY : state.MaxOffsetX,
                    vertical ? track.height - thumb.height : track.width - thumb.width);
                float page = vertical ? state.ViewportHeight : state.ViewportWidth;
                float direction = (vertical ? e.LocalY : e.LocalX) < thumbStart ? -1f : 1f;
                if (vertical) state.ScrollBy(0f, direction * page);
                else state.ScrollBy(direction * page, 0f);
            });
            return track.Child(thumb);
        }

        /// The usual combination: a scroll view and, beside it, a scrollbar
        /// (shown while there is something to scroll; the view takes its
        /// room otherwise). Give the returned box a size.
        public Box ScrollArea(ScrollState state, params Element[] children)
        {
            // flex-basis 0: it fills the room anyway, so its content's natural
            // width is never needed (measuring a long list for it is costly).
            ScrollView view = ScrollView(state).FlexGrow(1).FlexBasis(0).MinWidth(0).Children(children);
            Box bar = Scrollbar(state).Margin(2).FlexShrink(0);
            // min-width/height 0: as a flex item it must be able to shrink below
            // its content (the scroll view's full content), the usual CSS
            // `min-height: 0` for scrolling regions.
            return Div().Flex().Row().MinWidth(0).MinHeight(0).Children(view, bar);
        }

        private static float Proportion(float viewport, float content)
        {
            return content > 0f ? Mathf.Clamp(viewport / content * 100f, 0f, 100f) : 100f;
        }

        /// Logical px of travel for a thumb, from physical free space.
        private float ThumbPosition(float offset, float maximum, int freePixels)
        {
            // Reading layoutTick makes the binding follow layout (the track may resize).
            if (layoutTick.Value < 0)
            {
                return 0f;
            }
            return maximum > 0f && freePixels > 0 ? offset / maximum * freePixels / scale : 0f;
        }

        // ---- lists -------------------------------------------------------------------

        /// A keyed list: elements follow their key (moved, not rebuilt) as the
        /// list changes. The builder gets the item and its index as signals.
        public Region For<T, K>(Func<List<T>> items, Func<T, K> key, Func<Signal<T>, Signal<int>, Element> render)
        {
            return ListFactory.For(this, items, key, render);
        }

        /// A scrolling list of fixed-height rows that only builds the rows in
        /// view and recycles them. Pair it with `Scrollbar(state)` if wanted.
        /// At most `rowsPerFrame` rows get new content per frame (rows in view
        /// first); a row waiting for its content stays hidden meanwhile.
        public ScrollView VirtualList<T>(ScrollState state, Func<List<T>> items, float itemHeight,
            Func<Signal<T>, Signal<int>, Element> render, float gap = 0f, int overscan = 3, int rowsPerFrame = 4)
        {
            return ListFactory.VirtualList(this, state, items, itemHeight, render, gap, overscan, rowsPerFrame);
        }

        public Element Mount(Component component)
        {
            component.context = this;
            Element built = null;
            Owner owner = new Owner(runtime, runtime.currentOwner);
            runtime.RunWithOwner(owner, () => { built = component.Render(); });
            return built;
        }
    }
}
