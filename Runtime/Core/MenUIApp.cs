// The behaviour a MenUI panel is written in:
//
//   public class MyMenu : MenUIApp
//   {
//       protected override Element Build()
//       {
//           var count = Signal(0);
//           return Div().Flex().Column().Gap(8).Padding(16).Children(
//               Text(() => $"Count: {count.Value}"));
//       }
//   }
//
// Add it to a GameObject and the editor sets up everything else (display
// quad, offscreen camera, quad pool, shaping text). The library code is
// compiled into this one Udon program, which is what lets bindings be plain
// lambdas.

using System;
using System.Collections.Generic;
using MenSharp;
using TMPro;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace MenUI
{
    public class MenUIApp : MenSharpBehaviour
    {
        [Header("Panel")]
        [Tooltip("Width of the panel texture in physical pixels.")]
        public int bufferWidth = 1024;
        [Tooltip("Height of the panel texture in physical pixels.")]
        public int bufferHeight = 768;
        [Tooltip("Physical pixels per logical pixel.")]
        public float scale = 1.5f;
        [Tooltip("While playing, changing the size or scale above resizes the panel live. This point of the panel "
            + "stays in place (0,0 bottom-left, 0.5,0.5 center, 0,1 top-left).")]
        public Vector2 resizePivot = new Vector2(0.5f, 0.5f);

        [Header("Diagnostics")]
        [Tooltip("Log start-up timings, slow frames and a summary of the first seconds.")]
        public bool logPerformance = true;
        [Tooltip("A frame whose MenUI work takes longer than this (ms) is logged.")]
        public float slowFrameMilliseconds = 4f;

        [Header("Set up automatically")]
        public Camera renderCamera;
        public MeshRenderer quadPoolRenderer;
        public MeshRenderer displayRenderer;
        public int recordCapacity = 4096;
        // One entry per font registered in MenUI Settings (index 0: the
        // default). A TMP font asset cannot be held by Udon, so each font has
        // a hidden TextMeshProUGUI that shapes with it; the metrics are read
        // from the font assets by the editor, per unit font size.
        public string[] fontFamilies;
        public TextMeshProUGUI[] shapingTexts;
        public RectTransform[] shapingTextTransforms;
        public float[] fontAscenders;
        public float[] fontDescenders;
        public float[] fontLineHeights;
        public float[] fontPaddings;
        // Transparent input fields over the panel that bring up VRChat's
        // input UI for text boxes (see InputFieldTextInputHost).
        public UnityEngine.UI.InputField[] textInputFields;
        public RectTransform[] textInputTransforms;
        public RectTransform textInputCanvas;

        private MenUIContext ui;
        // What the panel was last sized to, and its world size per physical
        // pixel (kept when resizing: a bigger buffer is a bigger panel).
        private int appliedWidth, appliedHeight;
        private float appliedScale;
        private float metersPerPixel;

        // Start-up diagnostics: the whole frame time (not only MenUI's) over
        // the first seconds, to tell MenUI's cost from the editor warming up.
        private const float WatchSeconds = 5f;
        private float watchStart;
        private bool watching;
        private int watchedFrames;
        private float worstFrame;
        private int worstFrameIndex;
        private float frameTimeSum;
        private double worstMenUI;

        /// The panel's context: factory, reactive runtime and pipeline.
        protected MenUIContext UI { get { return ui; } }

        private void Start()
        {
            if (renderCamera == null || quadPoolRenderer == null || displayRenderer == null
                || shapingTexts == null || shapingTexts.Length == 0 || shapingTexts[0] == null)
            {
                Debug.LogError("[MenUI] " + gameObject.name + " is not set up: select it and run MenUI > Set Up Selected Panel.");
                return;
            }
            long start = Stopwatch.GetTimestamp();
            FitColliderToDisplay();
            appliedWidth = bufferWidth;
            appliedHeight = bufferHeight;
            appliedScale = scale;
            metersPerPixel = displayRenderer.transform.localScale.x / bufferWidth;
            TextShaper[] shapers = new TextShaper[shapingTexts.Length];
            for (int i = 0; i < shapers.Length; i++)
            {
                shapers[i] = new TextShaper(shapingTexts[i], shapingTextTransforms[i],
                    fontAscenders[i], fontDescenders[i], fontLineHeights[i], fontPaddings[i]);
            }
            PanelRenderer panelRenderer = new PanelRenderer(renderCamera, quadPoolRenderer, displayRenderer,
                recordCapacity, bufferWidth, bufferHeight);
            ui = new MenUIContext(scale, bufferWidth, bufferHeight, shapers, fontFamilies, panelRenderer,
                new PanelSurface(displayRenderer.transform));
            ui.SetPointerSource(CreatePointerSource());
            ui.SetTextInputHost(CreateTextInputHost());
            ui.diagnostics = logPerformance;
            long created = Stopwatch.GetTimestamp();

            Element content = null;
            ui.runtime.RunWithOwner(ui.runtime.root, () => { content = Build(); });
            if (content != null)
            {
                ui.root.AppendChild(content);
            }
            OnMounted();
            long built = Stopwatch.GetTimestamp();

            if (logPerformance)
            {
                Debug.Log("[MenUI] " + gameObject.name + ": set-up " + Milliseconds(created - start).ToString("0.0")
                    + " ms, Build() " + Milliseconds(built - created).ToString("0.0") + " ms");
                watching = true;
                watchStart = Time.realtimeSinceStartup;
            }
        }

        public override void PostLateUpdate()
        {
            if (ui == null)
            {
                return;
            }
            if (bufferWidth != appliedWidth || bufferHeight != appliedHeight || scale != appliedScale)
            {
                ResizePanel(bufferWidth, bufferHeight, scale);
            }
            long start = Stopwatch.GetTimestamp();
            ui.Tick(Time.deltaTime);
            long ticked = Stopwatch.GetTimestamp();
            ui.ProcessInput();
            long inputDone = Stopwatch.GetTimestamp();
            ui.tickTicks = ticked - start;
            ui.inputTicks = inputDone - ticked;
            ui.Update();
            double total = Milliseconds(Stopwatch.GetTimestamp() - start);
            if (!logPerformance)
            {
                return;
            }

            if (total >= slowFrameMilliseconds)
            {
                Debug.Log("[MenUI] frame " + Time.frameCount + ": " + total.ToString("0.0") + " ms (tick "
                    + Milliseconds(ui.tickTicks).ToString("0.0") + ", input " + Milliseconds(ui.inputTicks).ToString("0.0")
                    + ", style "
                    + Milliseconds(ui.styleTicks).ToString("0.0") + ", layout " + Milliseconds(ui.layoutTicks).ToString("0.0")
                    + " of which text shaping " + Milliseconds(ui.shapeTicks).ToString("0.0") + " ms for "
                    + ui.shapeCount + " strings, paint " + Milliseconds(ui.paintTicks).ToString("0.0")
                    + ", render " + Milliseconds(ui.renderTicks).ToString("0.0") + "; "
                    + ui.layout.arrangedCount + " elements arranged, "
                    + (!ui.paintedThisFrame ? "no paint" : ui.painter.lastPaintWasFull ? "full paint" : ui.painter.lastRepaintedCount + " elements repainted")
                    + (ui.groupsMovedThisFrame ? ", groups moved" : "")
                    + (ui.rowsFilled > 0 ? ", " + ui.rowsFilled + " list rows filled" : "")
                    + ", " + ui.renderer.RecordCount + " records)");
            }

            if (watching)
            {
                float frame = Time.unscaledDeltaTime * 1000f;
                watchedFrames++;
                frameTimeSum += frame;
                if (frame > worstFrame)
                {
                    worstFrame = frame;
                    worstFrameIndex = Time.frameCount;
                }
                if (total > worstMenUI)
                {
                    worstMenUI = total;
                }
                if (Time.realtimeSinceStartup - watchStart >= WatchSeconds)
                {
                    watching = false;
                    Debug.Log("[MenUI] first " + WatchSeconds + " s: " + watchedFrames + " frames, average frame "
                        + (frameTimeSum / watchedFrames).ToString("0.0") + " ms, worst frame " + worstFrame.ToString("0.0")
                        + " ms (frame " + worstFrameIndex + "); worst MenUI work " + worstMenUI.ToString("0.0") + " ms");
                }
            }
        }

        /// Resizes the panel while it runs: `width` x `height` physical pixels
        /// at `newScale` physical pixels per logical pixel. The panel grows
        /// or shrinks in the world with its pixel size (around resizePivot);
        /// the scale zooms the content within it. Editing bufferWidth,
        /// bufferHeight or scale in the inspector during play does the same.
        public void ResizePanel(int width, int height, float newScale)
        {
            if (ui == null)
            {
                return;
            }
            ui.Resize(width, height, newScale);
            // The context clamps; follow what it settled on.
            width = ui.bufferWidth;
            height = ui.bufferHeight;
            newScale = ui.scale;
            Transform display = displayRenderer.transform;
            Vector3 oldSize = display.localScale;
            float newWidth = width * metersPerPixel;
            float newHeight = height * metersPerPixel;
            display.localPosition += new Vector3((resizePivot.x - 0.5f) * (oldSize.x - newWidth),
                (resizePivot.y - 0.5f) * (oldSize.y - newHeight), 0f);
            display.localScale = new Vector3(newWidth, newHeight, oldSize.z);
            FitColliderToDisplay();
            bufferWidth = appliedWidth = width;
            bufferHeight = appliedHeight = height;
            scale = appliedScale = newScale;
        }

        /// The panel's collider covers the display quad wherever it was moved
        /// or resized to (it stays thin: the text input fields in front of the
        /// quad must be what pointers reach first).
        private void FitColliderToDisplay()
        {
            BoxCollider panelCollider = GetComponent<BoxCollider>();
            Transform display = displayRenderer.transform;
            if (panelCollider == null || display.parent != transform)
            {
                return;
            }
            Vector3 size = display.localScale;
            panelCollider.center = display.localPosition;
            panelCollider.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), panelCollider.size.z);
        }

        private static double Milliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        /// Builds the panel's content, once. Reactive parts are lambdas.
        protected virtual Element Build()
        {
            return null;
        }

        /// The device layer for this panel. The default handles desktop mode
        /// (look at the panel, click); override to plug in other devices.
        protected virtual IPointerSource CreatePointerSource()
        {
            return new DesktopPointerSource();
        }

        /// The text entry layer for this panel. The default lays transparent
        /// InputFields over text boxes (VRChat's own input UI, desktop and
        /// VR); override to plug in another (null: no typing).
        protected virtual ITextInputHost CreateTextInputHost()
        {
            if (textInputFields == null || textInputFields.Length == 0)
            {
                return null;
            }
            return new InputFieldTextInputHost(textInputFields, textInputTransforms, textInputCanvas);
        }

        /// Called after the first Build.
        protected virtual void OnMounted()
        {
        }

        // ---- factory (forwarded to the context) --------------------------------

        protected Box Div() { return ui.Div(); }
        protected TextElement Text(string content) { return ui.Text(content); }
        protected TextElement Text(Func<string> content) { return ui.Text(content); }
        protected TextBox TextBox(Signal<string> value) { return ui.TextBox(value); }
        protected TextBox TextArea(Signal<string> value) { return ui.TextArea(value); }
        protected Signal<T> Signal<T>(T initial) { return ui.Signal(initial); }
        protected Computed<T> Computed<T>(Func<T> compute) { return ui.Computed(compute); }
        protected Effect Effect(Action body) { return ui.Effect(body); }
        protected void Batch(Action action) { ui.Batch(action); }
        protected AnimatedFloat Animated(float initial) { return ui.Animated(initial); }
        protected AnimatedColor Animated(Color initial) { return ui.Animated(initial); }
        protected Playback Animate(float seconds, Action<float> frame, Easing easing = null, Action onComplete = null) { return ui.Animate(seconds, frame, easing, onComplete); }
        protected Playback EveryFrame(Action<float> tick) { return ui.EveryFrame(tick); }
        protected T Untrack<T>(Func<T> read) { return ui.Untrack(read); }
        protected void OnCleanup(Action cleanup) { ui.OnCleanup(cleanup); }
        protected Region Show(Func<bool> condition, Func<Element> then, Func<Element> otherwise = null) { return ui.Show(condition, then, otherwise); }
        protected Region Dynamic(Func<Element> build) { return ui.Dynamic(build); }
        protected Region Overlay(Func<Element> content) { return ui.Overlay(content); }
        protected Element Mount(Component component) { return ui.Mount(component); }
        protected void AddStyleSheet(StyleSheet sheet) { ui.AddStyleSheet(sheet); }
        protected Box Button(string label, ButtonVariant variant = ButtonVariant.Secondary) { return ui.Button(label, variant); }
        protected Box Button(Element content, ButtonVariant variant = ButtonVariant.Secondary) { return ui.Button(content, variant); }
        protected Box Checkbox(Signal<bool> isChecked, string label = null) { return ui.Checkbox(isChecked, label); }
        protected Box Switch(Signal<bool> isOn, string label = null) { return ui.Switch(isOn, label); }
        protected Box Radio<T>(Signal<T> selected, T value, string label = null) { return ui.Radio(selected, value, label); }
        protected Box Slider(Signal<float> value, float minimum = 0f, float maximum = 1f, float step = 0f) { return ui.Slider(value, minimum, maximum, step); }
        protected Box ProgressBar(Func<float> progress) { return ui.ProgressBar(progress); }
        protected Box Spinner() { return ui.Spinner(); }
        protected Box Tabs(Signal<int> selected, params string[] labels) { return ui.Tabs(selected, labels); }
        protected Box Divider() { return ui.Divider(); }
        protected Box Select<T>(Signal<T> selected, List<T> options, Func<T, string> label) { return ui.Select(selected, options, label); }
        protected Box Select(Signal<string> selected, params string[] options) { return ui.Select(selected, options); }
        protected Region Dialog(Signal<bool> open, Func<Element> content, bool closeOnBackdrop = true) { return ui.Dialog(open, content, closeOnBackdrop); }
        protected void Toast(string message, float seconds = 3f) { ui.Toast(message, seconds); }
        protected void SetTheme(Theme theme) { ui.SetTheme(theme); }
        /// The panel's size in logical pixels (what 100% means at the top).
        protected float LogicalWidth { get { return ui.LogicalWidth; } }
        protected float LogicalHeight { get { return ui.LogicalHeight; } }
        protected ScrollState ScrollState() { return ui.ScrollState(); }
        protected ScrollView ScrollView(ScrollState state, ScrollAxis axis = ScrollAxis.Vertical) { return ui.ScrollView(state, axis); }
        protected Box Scrollbar(ScrollState state, ScrollAxis axis = ScrollAxis.Vertical) { return ui.Scrollbar(state, axis); }
        protected Box ScrollArea(ScrollState state, params Element[] children) { return ui.ScrollArea(state, children); }
        protected Region For<T, K>(Func<List<T>> items, Func<T, K> key, Func<Signal<T>, Signal<int>, Element> render) { return ui.For(items, key, render); }
        protected ScrollView VirtualList<T>(ScrollState state, Func<List<T>> items, float itemHeight, Func<Signal<T>, Signal<int>, Element> render, float gap = 0f, int overscan = 3, int rowsPerFrame = 4) { return ui.VirtualList(state, items, itemHeight, render, gap, overscan, rowsPerFrame); }
    }
}
