using System;
using System.Collections.Generic;

namespace MenUI
{
    /// A reusable piece of UI. `Render` runs once when mounted with
    /// `Mount(new MyComponent(...))`; reactive parts are lambdas, as in an app.
    public abstract class Component
    {
        internal MenUIContext context;

        public abstract Element Render();

        protected MenUIContext UI { get { return context; } }

        protected Box Div() { return context.Div(); }
        protected TextElement Text(string content) { return context.Text(content); }
        protected TextElement Text(Func<string> content) { return context.Text(content); }
        protected TextBox TextBox(Signal<string> value) { return context.TextBox(value); }
        protected TextBox TextArea(Signal<string> value) { return context.TextArea(value); }
        protected Signal<T> Signal<T>(T initial) { return context.Signal(initial); }
        protected Computed<T> Computed<T>(Func<T> compute) { return context.Computed(compute); }
        protected Effect Effect(Action body) { return context.Effect(body); }
        protected void Batch(Action action) { context.Batch(action); }
        protected AnimatedFloat Animated(float initial) { return context.Animated(initial); }
        protected AnimatedColor Animated(UnityEngine.Color initial) { return context.Animated(initial); }
        protected Playback Animate(float seconds, Action<float> frame, Easing easing = null, Action onComplete = null) { return context.Animate(seconds, frame, easing, onComplete); }
        protected Playback EveryFrame(Action<float> tick) { return context.EveryFrame(tick); }
        protected T Untrack<T>(Func<T> read) { return context.Untrack(read); }
        protected void OnCleanup(Action cleanup) { context.OnCleanup(cleanup); }
        protected Region Show(Func<bool> condition, Func<Element> then, Func<Element> otherwise = null) { return context.Show(condition, then, otherwise); }
        protected Region Dynamic(Func<Element> build) { return context.Dynamic(build); }
        protected Region Overlay(Func<Element> content) { return context.Overlay(content); }
        protected Element Mount(Component component) { return context.Mount(component); }
        protected void AddStyleSheet(StyleSheet sheet) { context.AddStyleSheet(sheet); }
        protected Box Button(string label, ButtonVariant variant = ButtonVariant.Secondary) { return context.Button(label, variant); }
        protected Box Button(Element content, ButtonVariant variant = ButtonVariant.Secondary) { return context.Button(content, variant); }
        protected Box Checkbox(Signal<bool> isChecked, string label = null) { return context.Checkbox(isChecked, label); }
        protected Box Switch(Signal<bool> isOn, string label = null) { return context.Switch(isOn, label); }
        protected Box Radio<T>(Signal<T> selected, T value, string label = null) { return context.Radio(selected, value, label); }
        protected Box Slider(Signal<float> value, float minimum = 0f, float maximum = 1f, float step = 0f) { return context.Slider(value, minimum, maximum, step); }
        protected Box ProgressBar(Func<float> progress) { return context.ProgressBar(progress); }
        protected Box Spinner() { return context.Spinner(); }
        protected Box Tabs(Signal<int> selected, params string[] labels) { return context.Tabs(selected, labels); }
        protected Box Divider() { return context.Divider(); }
        protected Box Select<T>(Signal<T> selected, List<T> options, Func<T, string> label) { return context.Select(selected, options, label); }
        protected Box Select(Signal<string> selected, params string[] options) { return context.Select(selected, options); }
        protected Region Dialog(Signal<bool> open, Func<Element> content, bool closeOnBackdrop = true) { return context.Dialog(open, content, closeOnBackdrop); }
        protected void Toast(string message, float seconds = 3f) { context.Toast(message, seconds); }
        protected void SetTheme(Theme theme) { context.SetTheme(theme); }
        protected ScrollState ScrollState() { return context.ScrollState(); }
        protected ScrollView ScrollView(ScrollState state, ScrollAxis axis = ScrollAxis.Vertical) { return context.ScrollView(state, axis); }
        protected Box Scrollbar(ScrollState state, ScrollAxis axis = ScrollAxis.Vertical) { return context.Scrollbar(state, axis); }
        protected Box ScrollArea(ScrollState state, params Element[] children) { return context.ScrollArea(state, children); }
        protected Region For<T, K>(Func<List<T>> items, Func<T, K> key, Func<Signal<T>, Signal<int>, Element> render) { return context.For(items, key, render); }
        protected ScrollView VirtualList<T>(ScrollState state, Func<List<T>> items, float itemHeight, Func<Signal<T>, Signal<int>, Element> render, float gap = 0f, int overscan = 3, int rowsPerFrame = 4) { return context.VirtualList(state, items, itemHeight, render, gap, overscan, rowsPerFrame); }
    }
}
