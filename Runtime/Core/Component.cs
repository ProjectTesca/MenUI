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
        protected Signal<T> Signal<T>(T initial) { return context.Signal(initial); }
        protected Computed<T> Computed<T>(Func<T> compute) { return context.Computed(compute); }
        protected Effect Effect(Action body) { return context.Effect(body); }
        protected void Batch(Action action) { context.Batch(action); }
        protected T Untrack<T>(Func<T> read) { return context.Untrack(read); }
        protected void OnCleanup(Action cleanup) { context.OnCleanup(cleanup); }
        protected Region Show(Func<bool> condition, Func<Element> then, Func<Element> otherwise = null) { return context.Show(condition, then, otherwise); }
        protected Region Dynamic(Func<Element> build) { return context.Dynamic(build); }
        protected Region Overlay(Func<Element> content) { return context.Overlay(content); }
        protected Element Mount(Component component) { return context.Mount(component); }
        protected void AddStyleSheet(StyleSheet sheet) { context.AddStyleSheet(sheet); }
        protected ScrollState ScrollState() { return context.ScrollState(); }
        protected ScrollView ScrollView(ScrollState state, ScrollAxis axis = ScrollAxis.Vertical) { return context.ScrollView(state, axis); }
        protected Box Scrollbar(ScrollState state, ScrollAxis axis = ScrollAxis.Vertical) { return context.Scrollbar(state, axis); }
        protected Box ScrollArea(ScrollState state, params Element[] children) { return context.ScrollArea(state, children); }
        protected Region For<T, K>(Func<List<T>> items, Func<T, K> key, Func<Signal<T>, Signal<int>, Element> render) { return context.For(items, key, render); }
        protected ScrollView VirtualList<T>(ScrollState state, Func<List<T>> items, float itemHeight, Func<Signal<T>, Signal<int>, Element> render, float gap = 0f, int overscan = 3, int rowsPerFrame = 4) { return context.VirtualList(state, items, itemHeight, render, gap, overscan, rowsPerFrame); }
    }
}
