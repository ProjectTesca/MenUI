using System;
using System.Collections.Generic;
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
        internal readonly StyleResolver resolver = new StyleResolver();
        internal readonly LayoutEngine layout = new LayoutEngine();
        internal readonly Painter painter;

        internal readonly float scale;
        internal readonly int bufferWidth;
        internal readonly int bufferHeight;

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
        /// The set or order of records changes: repaint everything.
        internal bool paintStructureDirty = true;

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
        /// Bumped after every layout, for bindings that read laid-out sizes
        /// (a scrollbar thumb's travel).
        internal readonly Signal<int> layoutTick;

        // Timings of the last Update, in stopwatch ticks (see MenUIApp.logPerformance).
        internal long styleTicks, layoutTicks, paintTicks, renderTicks, tickTicks, inputTicks;
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
            groups = new TransformGroups(bufferWidth, bufferHeight);
            layoutTick = new Signal<int>(runtime, 0);
            themeSignal = new Signal<Theme>(runtime, theme);
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
            painter = new Painter(renderer);

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

        /// Advances time-based work (scroll momentum); call once a frame
        /// before input.
        internal void Tick(float seconds)
        {
            time += seconds;
            frame++;
            rowsFilled = 0;
            for (int i = animating.Count - 1; i >= 0; i--)
            {
                if (!animating[i].Tick(seconds))
                {
                    animating.RemoveAt(i);
                }
            }
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
                    layoutVersion++;
                    layoutTick.Value = layoutTick.Peek() + 1;
                    needsPaint = true;
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
                if (atlasesChanged)
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
            if (groups.uploadPending)
            {
                groups.uploadPending = false;
                renderer.SetGroups(groups.offsets, groups.clips);
                groupsMovedThisFrame = true;
                render = true;
            }
            if (render)
            {
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
        /// when it is removed.
        public Region Show(Func<bool> condition, Func<Element> then, Func<Element> otherwise = null)
        {
            Region region = new Region(this);
            Owner scope = runtime.currentOwner;
            bool initialized = false;
            bool last = false;
            Owner branch = null;
            Effect watcher = new Effect(runtime, scope, () =>
            {
                bool value = condition();
                if (initialized && value == last)
                {
                    return;
                }
                initialized = true;
                last = value;
                branch = SwapBranch(region, scope, branch, value ? then : otherwise);
            });
            watcher.Run();
            return region;
        }

        /// Rebuilds its content whenever something `build` read changes.
        public Region Dynamic(Func<Element> build)
        {
            Region region = new Region(this);
            Effect watcher = new Effect(runtime, runtime.currentOwner, () =>
            {
                // Built inside the effect: what it reads is tracked, and what
                // it creates is owned by the effect, so a rerun disposes it.
                Element built = build();
                region.RemoveAllChildren();
                if (built != null)
                {
                    region.AppendChild(built);
                }
            });
            watcher.Run();
            return region;
        }

        private Owner SwapBranch(Region region, Owner scope, Owner previous, Func<Element> build)
        {
            if (previous != null)
            {
                previous.Dispose();
            }
            region.RemoveAllChildren();
            if (build == null)
            {
                return null;
            }
            Owner owner = new Owner(runtime, scope);
            Element built = null;
            runtime.RunWithOwner(owner, () => { built = build(); });
            if (built != null)
            {
                region.AppendChild(built);
            }
            return owner;
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
        /// layout box moved by scrolling and translation).
        internal void LogicalRect(Element e, out float left, out float top, out float width, out float height)
        {
            Vector4 offset = groups.offsets[e.group];
            left = (e.x + offset.x) / scale;
            top = (e.y + offset.y) / scale;
            width = e.width / scale;
            height = e.height / scale;
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

        /// A scrollbar for `state`: a track with a draggable thumb. It hides
        /// itself while there is nothing to scroll. Style the track as usual
        /// (its width, or height for a horizontal bar, defaults to 8).
        public Box Scrollbar(ScrollState state, ScrollAxis axis = ScrollAxis.Vertical)
        {
            bool vertical = axis != ScrollAxis.Horizontal;
            Box track = Div();
            Box thumb = Div().Absolute().BorderRadius(Length.Percent(50))
                .Background(new Color(1f, 1f, 1f, 0.35f))
                .Hover(s => s.Background(new Color(1f, 1f, 1f, 0.6f)))
                .Active(s => s.Background(new Color(1f, 1f, 1f, 0.75f)));
            if (vertical)
            {
                track.Width(8);
                thumb.Left(0).Right(0).Top(0).MinHeight(24)
                    .Height(() => Length.Percent(Proportion(state.ViewportHeight, state.ContentHeight)));
            }
            else
            {
                track.Height(8);
                thumb.Top(0).Bottom(0).Left(0).MinWidth(24)
                    .Width(() => Length.Percent(Proportion(state.ViewportWidth, state.ContentWidth)));
            }
            track.Display(() => (vertical ? state.MaxOffsetY : state.MaxOffsetX) > 0.5f ? DisplayMode.Block : DisplayMode.None);

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

        /// The usual combination: a scroll view filling a box, with a thin
        /// scrollbar laid over its right edge. Give the returned box a size.
        public Box ScrollArea(ScrollState state, params Element[] children)
        {
            ScrollView view = ScrollView(state).FullSize().Children(children);
            Box bar = Scrollbar(state).Absolute().Top(3).Bottom(3).Right(3).Width(6);
            // min-width/height 0: as a flex item it must be able to shrink below
            // its content (the scroll view's full content), the usual CSS
            // `min-height: 0` for scrolling regions.
            return Div().Position(PositionMode.Relative).MinWidth(0).MinHeight(0).Children(view, bar);
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
