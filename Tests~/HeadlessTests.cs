using System;
using MenUI;
using MenUI.Material3;
using UnityEngine;

namespace MenUI
{
    /// A leaf standing in for text in headless tests: 7px per character.
    internal sealed class FakeText : Element
    {
        internal string content;
        internal int arranges;
        internal FakeText(MenUIContext context, string content) : base(context) { this.content = content; tag = "text"; }
        internal override bool IsLeaf { get { return true; } }
        internal void Set(string value) { content = value; ContentChanged(); }
        internal override void MeasureContent(int availableWidth, out int contentWidth, out int contentHeight)
        {
            contentWidth = content.Length * 7;
            if (availableWidth >= 0 && contentWidth > availableWidth) contentWidth = availableWidth;
            contentHeight = 10;
        }
        internal override void ArrangeContent(int contentWidth, int contentHeight) { arranges++; }
        internal override int MinContentWidthOfContent()
        {
            int longest = 0;
            foreach (string word in content.Split(' ')) longest = System.Math.Max(longest, word.Length);
            return longest * 7;
        }
    }

    /// A text input host that records what MenUI offers it.
    internal sealed class RecordingTextInputHost : ITextInputHost
    {
        internal readonly System.Collections.Generic.List<TextInputTarget> offered = new System.Collections.Generic.List<TextInputTarget>();
        public void Attach(PanelSurface surface) { }
        public void Update(System.Collections.Generic.List<TextInputTarget> targets) { offered.Clear(); offered.AddRange(targets); }
    }

    /// Unity's native logger does not exist here; collect messages instead.
    internal sealed class CollectingLogHandler : ILogHandler
    {
        internal readonly System.Collections.Generic.List<string> messages = new System.Collections.Generic.List<string>();
        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            messages.Add(logType + ": " + string.Format(format, args));
        }
        public void LogException(Exception exception, UnityEngine.Object context) { messages.Add("Exception: " + exception.Message); }
    }

    public static class Harness
    {
        static readonly CollectingLogHandler log = new CollectingLogHandler();
        static int failures;
        static void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? "  " + detail : ""));
            if (!ok) failures++;
        }
        static string R(Element e) => $"({e.X},{e.Y} {e.Width}x{e.Height})";

        static MenUIContext NewContext(float scale, int w, int h) => new MenUIContext(scale, w, h, null, new[] { "Noto Sans JP", "Inter" }, null);

        static void Layout(MenUIContext ui, Element content)
        {
            ui.root.AppendChild(content);
            Relayout(ui);
        }
        // The style and layout stages of MenUIContext.Update (without paint).
        static void Relayout(MenUIContext ui)
        {
            ui.ResolveStyles();
            ui.needsLayout = false;
            ui.layout.Run(ui);
        }


        // Stands in for the painter: publishes the paint order (tree order is
        // paint order when nothing uses z-index) so the router can hit test.
        static void PublishPaintOrder(MenUIContext ui)
        {
            ui.paintOrder.Clear();
            void Walk(Element e) { ui.paintOrder.Add(e); e.paintGeneration = ui.painter.Generation; foreach (var c in e.children) Walk(c); }
            Walk(ui.root);
            Walk(ui.overlayRoot);
            ui.hitVersion++;
        }
        static PointerSample[] samples = { new PointerSample() };
        // Advances frames (rows of a VirtualList fill within a per-frame budget).
        static void Frames(MenUIContext ui, int count)
        {
            for (int i = 0; i < count; i++) { ui.Tick(1f / 60f); Relayout(ui); }
            ui.groups.Resolve();
        }
        static void Pointer(MenUIContext ui, float x, float y, bool pressed, bool over = true, float scroll = 0f)
        {
            samples[0].PointerId = 0;
            samples[0].IsOverPanel = over;
            samples[0].U = x / ui.bufferWidth;
            samples[0].V = 1f - y / ui.bufferHeight;
            samples[0].PrimaryPressed = pressed;
            samples[0].ScrollDelta = scroll;
            ui.router.Process(samples, 1);
        }

        // A click, then the pipeline stages (without paint) and a fresh paint order.
        static void Click(MenUIContext ui, float x, float y)
        {
            Pointer(ui, x, y, true);
            Pointer(ui, x, y, false);
            Refresh(ui);
        }
        static void Refresh(MenUIContext ui)
        {
            Relayout(ui);
            ui.groups.Resolve();
            PublishPaintOrder(ui);
        }
        // Lets transitions (and exits) finish, then publishes the paint order.
        static void Settle(MenUIContext ui)
        {
            Frames(ui, 40);
            PublishPaintOrder(ui);
        }
        // Center of an element on the panel (physical px).
        static float CenterX(MenUIContext ui, Element e) => e.X + ui.groups.offsets[e.group].x + e.Width / 2f;
        static float CenterY(MenUIContext ui, Element e) => e.Y + ui.groups.offsets[e.group].y + e.Height / 2f;

        // ---- GPU layers: translate, scale, opacity on transform groups ----
        static void LayerTests()
        {
            {
                var ui = NewContext(1f, 200, 200);
                var zoomed = ui.Signal(false);
                Box inner = ui.Div().Size(20, 20);
                Box box = ui.Div().Size(100, 100).Margin(50).Children(inner)
                    .Bind(e => e.Scale(zoomed.Value ? 0.5f : 1f));
                Layout(ui, box);
                Check("layer: none while untransformed", box.translateGroup == TransformGroups.Root);
                zoomed.Value = true;
                Relayout(ui);
                ui.groups.Resolve();
                int g = box.translateGroup;
                Vector4 t = ui.groups.offsets[g];
                // Scaled around its center (100, 100): layout 50 maps to 75.
                Check("layer: a group while scaled", g != TransformGroups.Root && t.z == 0.5f
                    && Near(ui.groups.ScreenX(g, 50f), 75f), t.ToString());
                Check("layer: children draw in it", inner.group == g);
                PublishPaintOrder(ui);
                inner.OnClick(() => { });
                ui.hitVersion++;
                // The child at layout (50,50)-(70,70) is on screen at (75,75)-(85,85).
                Pointer(ui, 80, 80, false);
                Check("layer: hit testing maps through the scale", ui.router.HoverTarget(0) == inner);
                zoomed.Value = false;
                Relayout(ui);
                Check("layer: released when back to normal", box.translateGroup == TransformGroups.Root && inner.group == TransformGroups.Root);
            }

            {
                // A fade on a group: opacity goes to the GPU, and the group is
                // given back when the fade ends.
                var ui = NewContext(1f, 200, 200);
                var visible = ui.Signal(true);
                Box panel = ui.Div().Size(50, 50).Opacity(() => visible.Value ? 1f : 0f)
                    .Transition(StyleProperty.Opacity, AnimationSpec.Tween(0.2f, Easing.Linear));
                Layout(ui, panel);
                visible.Value = false;
                Relayout(ui);
                Frames(ui, 6);
                int g = panel.translateGroup;
                float alpha = ui.groups.offsets[g].w;
                Check("fade layer: opacity on the group", g != TransformGroups.Root && alpha > 0.3f && alpha < 0.7f, alpha.ToString());
                Frames(ui, 20);
                Check("fade layer: kept while transparent is fine, released once settled", panel.computed.Opacity == 0f
                    && panel.translateGroup == TransformGroups.Root);
            }

            {
                // Style translate animates on the GPU with a transition.
                var ui = NewContext(1f, 200, 200);
                var moved = ui.Signal(false);
                Box box = ui.Div().Size(20, 20).Bind(e => e.TranslateX(moved.Value ? 100f : 0f))
                    .Transition(StyleProperty.TranslateX, AnimationSpec.Tween(0.2f, Easing.Linear));
                Layout(ui, box);
                moved.Value = true;
                Relayout(ui);
                Frames(ui, 6);
                float x = ui.groups.offsets[box.translateGroup].x;
                Check("translate transition: moves on the GPU", x > 30f && x < 70f && ui.layout.arrangedCount == 0 && box.X == 0, x.ToString());
                Frames(ui, 20);
                Check("translate transition: arrives", ui.groups.offsets[box.translateGroup].x == 100f);
            }

            {
                // Graphic: records commands reactively, re-records on resize.
                var ui = NewContext(1f, 200, 200);
                var p = ui.Signal(0.5f);
                float seenWidth = -1f;
                Graphic g = ui.Graphic(c =>
                {
                    seenWidth = c.Width;
                    c.Line(0, c.Width * p.Value, 5, 4, Color.red);
                    c.Circle(10, 10, 5, Color.blue);
                }).Size(120, 10);
                Layout(ui, g);
                Check("graphic: recorded at its size", seenWidth == 120f && g.canvas.count == 2);
                p.Value = 1f;
                Check("graphic: reruns on a signal", g.canvas.numbers[2] == 120f);
            }
        }

        // ---- Material 3 ----
        static void MaterialTests()
        {
            {
                MaterialColorScheme seeded = MaterialColorScheme.FromSeed(Colors.Hex(0x3B82F6));
                float l, a, b;
                MaterialColorScheme.Lab(seeded.Primary, out l, out a, out b);
                float lc, ac, bc;
                MaterialColorScheme.Lab(seeded.PrimaryContainer, out lc, out ac, out bc);
                Check("scheme from seed: primary tone 40, container 90", Near(l, 40f, 1.5f) && Near(lc, 90f, 1.5f), l + " " + lc);
                Check("scheme from seed: bluish primary", b < 0f && seeded.Primary.b > seeded.Primary.r, seeded.Primary.ToString());
                MaterialColorScheme darkSeeded = MaterialColorScheme.FromSeed(Colors.Hex(0x3B82F6), true);
                MaterialColorScheme.Lab(darkSeeded.Surface, out l, out a, out b);
                Check("scheme from seed: dark surface", l < 10f && darkSeeded.IsDark, l.ToString());
            }

            {
                var ui = NewContext(1f, 800, 600);
                MaterialDesign m3 = MaterialDesign.Of(ui);
                Check("material: one per panel", MaterialDesign.Of(ui) == m3 && ui.styleSheets[1] == m3.sheet);
                var selected = ui.Signal(false);
                Box filled = m3.Button("Save", MaterialSymbol.Check);
                Box toggle = m3.ToggleButton(selected, "Like", MaterialSymbol.Favorite, ButtonStyle.Filled, ComponentSize.Medium);
                Box icon = m3.IconButton(MaterialSymbol.Search, IconButtonStyle.Filled, ComponentSize.Large, IconButtonWidth.Wide);
                Box row = ui.Div().Row().Gap(10).Padding(10).AlignItems(Align.Start).Children(filled, toggle, icon);
                Layout(ui, row);
                Settle(ui);
                MaterialColorScheme c = MaterialColorScheme.BaselineLight;
                Check("m3 button: filled colors and size", filled.computed.ColorOf(StyleProperty.Background) == c.Primary
                    && filled.computed.ColorOf(StyleProperty.TextColor) == c.OnPrimary && filled.Height == 40, R(filled));
                Check("m3 button: fully round", filled.computed.numbers[StyleProperty.CornerTopLeft] == 20f);
                Check("m3 icon button: large wide", icon.Width == 128 && icon.Height == 96, R(icon));

                // Hover: the state layer over the container.
                Pointer(ui, CenterX(ui, filled), CenterY(ui, filled), false);
                Settle(ui);
                Color hover = filled.computed.ColorOf(StyleProperty.Background);
                Check("m3 button: hover state layer", hover != c.Primary && Near(hover.r, c.Primary.r * 0.92f + c.OnPrimary.r * 0.08f, 0.01f),
                    hover.ToString());
                // Pressing morphs the corners (a spring toward 8).
                Pointer(ui, CenterX(ui, filled), CenterY(ui, filled), true);
                Settle(ui);
                Check("m3 button: pressed corners", filled.computed.numbers[StyleProperty.CornerTopLeft] == 8f,
                    filled.computed.numbers[StyleProperty.CornerTopLeft].ToString());
                Pointer(ui, CenterX(ui, filled), CenterY(ui, filled), false);
                Pointer(ui, 790, 590, false);
                Settle(ui);

                // Toggle: selected turns square and primary.
                Click(ui, CenterX(ui, toggle), CenterY(ui, toggle));
                Pointer(ui, 790, 590, false);   // away: no hover layer
                Settle(ui);
                Check("m3 toggle: selected, square", selected.Peek() && toggle.HasClass("selected")
                    && toggle.computed.numbers[StyleProperty.CornerTopLeft] == 16f
                    && toggle.computed.ColorOf(StyleProperty.Background) == c.Primary,
                    toggle.computed.numbers[StyleProperty.CornerTopLeft].ToString());

                // A new scheme restyles everything (with a transition).
                m3.SetColorScheme(MaterialColorScheme.BaselineDark);
                Relayout(ui);
                Settle(ui);
                Check("m3: scheme change", filled.computed.ColorOf(StyleProperty.Background) == MaterialColorScheme.BaselineDark.Primary);
            }

            {
                var ui = NewContext(1f, 800, 600);
                MaterialDesign m3 = MaterialDesign.Of(ui);
                var on = ui.Signal(false);
                var name = ui.Signal("");
                Box sw = m3.Switch(on, "Wi-Fi");
                Box field = m3.TextField(name, "Name");
                Layout(ui, ui.Div().Column().Gap(10).Padding(10).Children(sw, field));
                Settle(ui);
                Element thumb = sw.children[0].children[0];
                int thumbBefore = thumb.X;
                Click(ui, CenterX(ui, sw), CenterY(ui, sw));
                Settle(ui);
                Check("m3 switch: the thumb moves and grows", on.Peek() && thumb.X > thumbBefore && thumb.Width == 24, R(thumb));
                Element label = null;
                foreach (Element child in field.children[0].children) if (child.tag == "m3-field-label") label = child;
                int restingTop = label.Y;
                name.Value = "Ann";
                Relayout(ui);
                Settle(ui);
                Check("m3 text field: the label floats", label.Y < restingTop && label.computed.FontSize == 12f, R(label));
            }

            {
                var ui = NewContext(1f, 800, 600);
                MaterialDesign m3 = MaterialDesign.Of(ui);
                var open = ui.Signal(false);
                Box menu = m3.FabMenu(open, MaterialSymbol.Add,
                    new FabMenuItem("One", MaterialSymbol.Star, null), new FabMenuItem("Two", MaterialSymbol.Star, null));
                Layout(ui, ui.Div().Padding(20).Children(menu));
                Settle(ui);
                Element items = menu.children[0];
                Element fab = menu.children[1];
                int fabX = fab.X, fabY = fab.Y;
                Check("fab menu: closed items are invisible", items.children[0].computed.Opacity == 0f);
                open.Value = true;
                Relayout(ui);
                Settle(ui);
                Check("fab menu: items appear", items.children[0].computed.Opacity == 1f && items.children[1].computed.Opacity == 1f);
                Check("fab menu: the button stays put", fab.X == fabX && fab.Y == fabY, R(fab));
                open.Value = false;
                Relayout(ui);
                Settle(ui);
                Check("fab menu: items fade away, the button still in place", items.children[0].computed.Opacity == 0f
                    && fab.X == fabX && fab.Y == fabY);

                var expanded = ui.Signal(false);
                var selected = ui.Signal(0);
                Box rail = m3.NavigationRail(selected, expanded, new[] { "A", "B" }, new[] { MaterialSymbol.Home, MaterialSymbol.Map });
                ui.root.children[0].AppendChild(rail);
                Settle(ui);
                Check("rail: narrow", rail.Width == 96, R(rail));
                expanded.Value = true;
                Relayout(ui);
                Frames(ui, 4);
                Check("rail: widening", rail.Width > 96 && rail.Width < 220, R(rail));
                Settle(ui);
                Check("rail: wide", rail.Width == 220, R(rail));

                var progress = ui.Signal(0.5f);
                Graphic linear = m3.LinearProgress(() => progress.Value, true);
                Element loading = m3.LoadingIndicator(true);
                ui.root.children[0].AppendChild(ui.Div().Width(300).Children(linear, loading));
                Settle(ui);
                int commands = linear.canvas.count;
                progress.Value = 1f;
                Check("linear progress: steady command count", commands == 3 && linear.canvas.count == 3);
                Check("loading indicator: container and shape", ((Graphic)loading).canvas.count == 2);
            }
        }


        // Frames with background loading at their end (MenUI's own work: `spent` ms).
        static void LoadFrames(MenUIContext ui, int count, double spent = 0)
        {
            for (int i = 0; i < count; i++)
            {
                ui.Tick(1f / 60f);
                Relayout(ui);
                ui.RunBackground(spent);
            }
        }

        // ---- background loading ----
        static void LoadingTests()
        {
            {
                var ui = NewContext(1f, 300, 300);
                ui.BackgroundBudget = 0f;   // only the one step every frame gets
                int rows = 0;
                Box list = null;
                Region deferred = ui.Deferred(async load =>
                {
                    list = ui.Div().Column();
                    load.Content.Child(list);
                    for (int i = 0; i < 30; i++)
                    {
                        list.Child(ui.Div().Height(10));
                        rows++;
                        load.Progress = (i + 1) / 30f;
                        await load.Yield();
                    }
                }, () => ui.Div().Height(40).Tag("placeholder"));
                Box after = ui.Div().Height(10);
                Layout(ui, ui.Div().Children(deferred, after));
                Check("deferred: placeholder first, nothing built yet", rows == 0 && after.Y == 40 && deferred.children.Count == 2);
                ui.paintStructureDirty = false;
                LoadFrames(ui, 2);
                Box holder = (Box)deferred.children[0];
                Check("deferred: one step a frame even with no budget", rows == 2, rows.ToString());
                Check("deferred: not laid out every frame", list.Height == 0, R(list));
                LoadFrames(ui, 15);
                Relayout(ui);   // laid out now and then (every 0.25 s)
                Element placeholderBox = deferred.children[1];
                Check("deferred: laid out out of sight now and then, on the placeholder", holder.inert && list.Height > 100
                    && list.Height < 300 && after.Y == 40 && holder.X == placeholderBox.X && holder.Y == placeholderBox.Y
                    && holder.Width == placeholderBox.Width, R(list) + R(holder) + R(placeholderBox));
                LoadFrames(ui, 20);
                Relayout(ui);
                Check("deferred: shown when done", rows == 30 && !holder.inert && after.Y == 300, R(after));
                Settle(ui);
                Check("deferred: the placeholder is gone", deferred.children.Count == 1);
            }

            {
                // A generous budget: all in one frame.
                var ui = NewContext(1f, 300, 300);
                ui.BackgroundBudget = 1000f;
                int steps = 0;
                Region deferred = ui.Deferred(async load =>
                {
                    for (int i = 0; i < 20; i++) { steps++; load.Content.Child(ui.Div().Height(1)); await load.Yield(); }
                });
                Layout(ui, ui.Div().Children(deferred));
                LoadFrames(ui, 1);
                Check("deferred: a big budget finishes in one frame", steps == 20);
            }

            {
                // Removed while loading: the load stops.
                var ui = NewContext(1f, 300, 300);
                ui.BackgroundBudget = 0f;
                var show = ui.Signal(true);
                int steps = 0, cleanups = 0;
                Layout(ui, ui.Div().Children(ui.Show(() => show.Value, () => ui.Deferred(async load =>
                {
                    ui.OnCleanup(() => cleanups++);
                    for (int i = 0; i < 10; i++) { steps++; await load.Yield(); }
                }))));
                LoadFrames(ui, 3);
                show.Value = false;
                LoadFrames(ui, 5);
                Check("deferred: removed while loading stops it", steps == 3 && cleanups == 1, steps + " " + cleanups);
            }

            {
                // Pages preloaded in the background.
                var ui = NewContext(1f, 300, 300);
                ui.BackgroundBudget = 0f;
                var page = ui.Signal(0);
                int builds = 0;
                Region pages = ui.Pages(() => page.Value, true,
                    () => { builds++; return ui.Div().Height(30); },
                    () => { builds++; return ui.Div().Height(50); },
                    () => { builds++; return ui.Div().Height(70); });
                Box tail = ui.Div().Height(10);
                Layout(ui, ui.Div().Children(pages, tail));
                Check("preload: the shown page at once", builds == 1 && tail.Y == 30);
                LoadFrames(ui, 60);
                Check("preload: the others in the background", builds == 3 && tail.Y == 30, builds + " " + R(tail));
                page.Value = 2;
                Relayout(ui);
                Check("preload: switching builds nothing", builds == 3 && tail.Y == 70, R(tail));
            }

            {
                // Near: far content until the near one is ready; kept or unloaded when away.
                var ui = NewContext(1f, 300, 300);
                ui.BackgroundBudget = 0f;
                int nearBuilds = 0, nearCleanups = 0;
                Region near = ui.Near(3f, () =>
                {
                    nearBuilds++;
                    ui.OnCleanup(() => nearCleanups++);
                    return ui.Div().Height(80).Tag("near");
                }, () => ui.Div().Height(20).Tag("far"));
                Region unloading = ui.Near(3f, () => { ui.OnCleanup(() => nearCleanups += 100); return ui.Div().Height(5); },
                    null, true);
                Box tail = ui.Div().Height(10);
                Layout(ui, ui.Div().Children(near, unloading, tail));
                Check("near: far first", tail.Y == 20 && nearBuilds == 0);
                ui.SetViewerDistance(2f);
                Relayout(ui);
                Check("near: far while the near one loads", tail.Y == 20 && nearBuilds == 0);
                LoadFrames(ui, 3);
                Settle(ui);
                Check("near: near when ready", nearBuilds == 1 && tail.Y == 85, R(tail));
                ui.SetViewerDistance(3.2f);   // inside the slack: stays near
                Relayout(ui);
                Check("near: no flicker on the line", tail.Y == 85);
                ui.SetViewerDistance(10f);
                Relayout(ui);
                Settle(ui);
                Check("near: far again, kept or unloaded as asked", tail.Y == 20 && nearCleanups == 100, nearCleanups + " " + R(tail));
                ui.SetViewerDistance(1f);
                Relayout(ui);
                Settle(ui);
                Check("near: back again: the kept one at once", nearBuilds == 1 && tail.Y >= 80, R(tail));
            }

            {
                // Out of sight costs nothing: a kept Near side and a hidden page
                // stop their animations (they wait, then go on where they were).
                var ui = NewContext(1f, 300, 300);
                ui.BackgroundBudget = 1000f;
                AnimatedFloat nearValue = null, pageValue = null;
                Region near = ui.Near(3f, () =>
                {
                    nearValue = ui.Animated(0f);
                    nearValue.AnimateTo(100f, AnimationSpec.Tween(10f, Easing.Linear));
                    return ui.Div().Height(80);
                }, () => ui.Div().Height(20));
                var page = ui.Signal(0);
                Region pages = ui.Pages(() => page.Value,
                    () => ui.Div().Height(30),
                    () =>
                    {
                        pageValue = ui.Animated(0f);
                        pageValue.AnimateTo(100f, AnimationSpec.Tween(10f, Easing.Linear));
                        return ui.Div().Height(50);
                    });
                Layout(ui, ui.Div().Children(near, pages));
                ui.SetViewerDistance(1f);
                LoadFrames(ui, 3);
                Settle(ui);
                page.Value = 1;
                LoadFrames(ui, 6);
                float nearMoving = nearValue.Peek(), pageMoving = pageValue.Peek();
                Check("hidden: shown parts animate", nearMoving > 0f && pageMoving > 0f, nearMoving + " " + pageMoving);

                ui.SetViewerDistance(10f);
                page.Value = 0;
                LoadFrames(ui, 2);
                float nearStopped = nearValue.Peek(), pageStopped = pageValue.Peek();
                ui.Update();
                int paints = 0;
                for (int f = 0; f < 60; f++)
                {
                    ui.Tick(1f / 60f);
                    ui.Update();
                    if (ui.paintedThisFrame) paints++;
                    ui.RunBackground(0);
                }
                Check("hidden: out of sight, animations wait", nearValue.Peek() == nearStopped && pageValue.Peek() == pageStopped
                    && ui.tasksRun == 0 && ui.tasksWaiting == 2, nearValue.Peek() + " " + pageValue.Peek() + " " + ui.tasksRun + "/" + ui.tasksWaiting);
                Check("hidden: nothing to lay out or paint while they wait", paints == 0 && !ui.needsLayout, paints.ToString());

                ui.SetViewerDistance(1f);
                page.Value = 1;
                LoadFrames(ui, 3);
                Check("hidden: shown again, they go on from where they were", nearValue.Peek() > nearStopped
                    && nearValue.Peek() < nearStopped + 10f && pageValue.Peek() > pageStopped && pageValue.Peek() < pageStopped + 10f,
                    nearValue.Peek() + " " + pageValue.Peek());
            }
        }



        // ---- painting: incremental paints equal a fresh full paint ----
        // ---- images ----
        static ImageSource FakeImage(int width, int height) => new ImageSource(null, 0f, 0f, 1f, 1f, width, height);

        static Vector4 RecordTexel(MenUIContext ui, int record, int texel)
        {
            Color c = ui.painter.records.pixels[record * RecordBuffer.TexelsPerRecord + texel];
            return new Vector4(c.r, c.g, c.b, c.a);
        }

        static void ImageTests()
        {
            {
                // Sizing: natural size, one side keeps the aspect ratio, both as set.
                var ui = NewContext(2f, 1000, 1000);
                ImageSource wide = FakeImage(200, 100);
                ImageElement natural = ui.Image(wide);
                ImageElement byWidth = ui.Image(wide).Width(100);
                ImageElement byHeight = ui.Image(wide).Height(80);
                ImageElement both = ui.Image(wide).Size(30, 40);
                Box row = ui.Div().Column().AlignItems(Align.Start)
                    .Children(natural, byWidth, byHeight, both);
                Layout(ui, row);
                Check("image: natural size, a texture pixel per logical pixel", natural.Width == 400 && natural.Height == 200, R(natural));
                Check("image: a set width keeps the aspect ratio", byWidth.Width == 200 && byWidth.Height == 100, R(byWidth));
                Check("image: a set height keeps the aspect ratio", byHeight.Width == 320 && byHeight.Height == 160, R(byHeight));
                Check("image: both sides set", both.Width == 60 && both.Height == 80, R(both));
            }

            {
                // Fit: cover crops (the uv follows), contain letterboxes.
                var ui = NewContext(1f, 400, 400);
                ImageSource wide = FakeImage(200, 100);
                ImageElement cover = ui.Image(wide).Size(100, 100).Fit(ImageFit.Cover);
                ImageElement contain = ui.Image(wide).Size(100, 100).Fit(ImageFit.Contain).BorderRadius(10);
                ImageElement coverToo = ui.Image(wide).Size(100, 100).Fit(ImageFit.Cover);
                Layout(ui, ui.Div().Children(cover, contain, coverToo));
                ui.Update();
                ImageCopy copy = cover.held;
                Vector4 region = ui.images.RegionOf(copy);
                Vector4 coverQuad = RecordTexel(ui, cover.contentRecordStart, 0);
                Vector4 coverUv = RecordTexel(ui, cover.contentRecordStart, 3);
                float quarter = (region.z - region.x) * 0.25f;
                Check("image cover: drawn over the box, cropped", cover.contentRecordCount == 1
                    && coverQuad == new Vector4(0f, 0f, 100f, 100f), coverQuad.ToString());
                Check("image cover: the middle half of the image", Near(coverUv.x, region.x + quarter, 1e-5f)
                    && Near(coverUv.z, region.z - quarter, 1e-5f) && coverUv.y == region.y && coverUv.w == region.w, coverUv + " " + region);
                Vector4 containQuad = RecordTexel(ui, contain.contentRecordStart, 0);
                Check("image contain: whole, centered, from a copy half the size", containQuad == new Vector4(0f, 125f, 100f, 50f)
                    && RecordTexel(ui, contain.contentRecordStart, 3) == ui.images.RegionOf(contain.held)
                    && contain.held.level == 1 && copy.level == 0, containQuad.ToString());
                Check("image: rounded by the element's radii", RecordTexel(ui, contain.contentRecordStart, 5) == new Vector4(10f, 10f, 10f, 10f)
                    && RecordTexel(ui, contain.contentRecordStart, 7) == new Vector4(0f, 100f, 100f, 100f));
                Check("image: the same image at the same size shares its copy", coverToo.held == copy
                    && ui.images.copiesMade == 2 && copy.holders.Count == 2);
                Check("image: records verify", ui.painter.Verify(ui) == null, ui.painter.Verify(ui) ?? "");

                cover.Tint(new Color(1f, 0f, 0f, 0.5f));
                ui.Update();
                Check("image: a tint repaints in place", !ui.painter.lastPaintWasFull
                    && RecordTexel(ui, cover.contentRecordStart, 2).w == 0.5f && ui.painter.Verify(ui) == null);
            }

            {
                // Level of detail: a large image shown small is copied small.
                var ui = NewContext(1f, 400, 400);
                ImageSource photo = FakeImage(1000, 800);
                ImageElement thumbnail = ui.Image(photo).Width(100);
                ImageElement large = ui.Image(photo).Width(300);
                Layout(ui, ui.Div().Children(thumbnail, large));
                ui.Update();
                Check("image: copied at the level that covers its size", thumbnail.held.level == 3
                    && thumbnail.held.width == 125 && thumbnail.held.height == 100, thumbnail.held.level + " " + thumbnail.held.width);
                Check("image: a larger use, a larger copy", large.held.level == 1 && large.held.width == 500);

                // A new source releases the old copy (cached, not held).
                ImageCopy old = thumbnail.held;
                thumbnail.SetSource(FakeImage(50, 50));
                ui.Update();
                Check("image: a new source lets the old copy go", old.holders.Count == 0 && thumbnail.held != old
                    && thumbnail.Width == 100 && thumbnail.Height == 100 && ui.painter.Verify(ui) == null, R(thumbnail));
            }

            {
                // Released with its owner; room made by compaction, then pages.
                var ui = NewContext(1f, 400, 400);
                var showFirst = ui.Signal(true);
                ImageElement[] big = new ImageElement[4];
                for (int i = 2; i < 4; i++) big[i] = ui.Image(FakeImage(2000, 2000)).Size(40, 40).Fit(ImageFit.None);
                // Built in the branch: owned by it, released when it goes.
                Region first = ui.Show(() => showFirst.Value, () =>
                {
                    for (int i = 0; i < 2; i++) big[i] = ui.Image(FakeImage(2000, 2000)).Size(40, 40).Fit(ImageFit.None);
                    return ui.Div().Children(big[0], big[1]);
                });
                Layout(ui, ui.Div().Flex().Children(first, big[2], big[3]));
                ui.Update();
                Check("image atlas: one large page per full-size copy", ui.images.pageCount == 4
                    && ui.images.pages[0].size == 2048, ui.images.pageCount + " pages");
                ImageCopy dropped = big[0].held;
                showFirst.Value = false;
                ui.Update();
                Check("image: released with its owner", dropped.holders.Count == 0 && big[0].held == null);

                ImageElement late = ui.Image(FakeImage(2000, 2000)).Size(40, 40).Fit(ImageFit.None);
                ((Box)ui.root.children[0]).AppendChild(late);
                ui.Update();
                Check("image atlas: no room, so compaction is due", late.held == null && ui.images.compactionPending);
                ui.Update();
                Check("image atlas: compacted, then drawn", late.held != null && late.contentRecordCount == 1
                    && ui.images.pageCount == 3 && ui.images.copies.Count == 3 && dropped.source.copies[0] == null,
                    ui.images.pageCount + " pages, " + ui.images.copies.Count + " copies");
                Check("image atlas: the kept copies repainted where they moved", ui.painter.Verify(ui) == null
                    && big[2].held.page >= 0 && big[3].held.page >= 0, ui.painter.Verify(ui) ?? "");
            }
        }

        static void WebImageTests()
        {
            {
                // A download: no size before it arrives, then sized and drawn.
                var ui = NewContext(1f, 400, 400);
                var web = new ImageSource(ui.runtime, "https://example.com/a.png");
                ImageElement sized = null, byWidth = null;
                var showFirst = ui.Signal(true);
                Region first = ui.Show(() => showFirst.Value, () =>
                {
                    sized = ui.Image(web).Size(100, 50);
                    byWidth = ui.Image(web).Width(80);
                    return ui.Div().Children(sized, byWidth);
                });
                int loadedRuns = 0;
                bool seenLoaded = false;
                ui.Bind(() => { seenLoaded = web.IsLoaded; loadedRuns++; });
                Layout(ui, ui.Div().Children(first));
                ui.Update();
                Check("web image: before it arrives, the set size and nothing drawn", sized.Width == 100 && sized.Height == 50
                    && byWidth.Height == 0 && sized.contentRecordCount == 0 && !seenLoaded, R(byWidth));
                web.Arrived(null, 200, 100);
                ui.Update();
                Check("web image: arrived, its aspect ratio sizes and it draws", byWidth.Height == 40
                    && sized.contentRecordCount == 1 && byWidth.contentRecordCount == 1 && ui.painter.Verify(ui) == null, R(byWidth));
                Check("web image: IsLoaded is reactive", seenLoaded && loadedRuns == 2, loadedRuns.ToString());

                // Let go when compaction drops its last copy; drawn again, asked for again.
                var unloaded = new System.Collections.Generic.List<ImageSource>();
                var asked = new System.Collections.Generic.List<ImageSource>();
                ui.unloadImage = source => { unloaded.Add(source); source.texture = null; source.SetState(ImageSource.StateUnloaded); };
                ui.downloadImage = source => { asked.Add(source); source.SetState(ImageSource.StateLoading); };
                var show = ui.Signal(true);
                Region region = ui.Show(() => show.Value, () => ui.Image(web).Size(100, 50));
                ((Box)ui.root.children[0]).AppendChild(region);
                showFirst.Value = false;
                ui.Update();
                show.Value = false;
                ui.Update();
                ui.images.compactionPending = true;   // as when room is needed
                ui.Update();
                Check("web image: no copy left, its texture is let go", unloaded.Count == 1
                    && web.state == ImageSource.StateUnloaded && web.Width == 200, web.state.ToString());
                show.Value = true;
                ui.Update();
                ui.Update();
                Check("web image: drawn again, downloaded again", asked.Count == 1 && web.state == ImageSource.StateLoading);
                web.Arrived(null, 200, 100);
                ui.Update();
                Check("web image: back", ((Element)region.children[0]).contentRecordCount == 1 && ui.painter.Verify(ui) == null);
            }
        }

        static void PaintTests()
        {
            var random = new System.Random(4321);
            int mismatches = 0;
            string first = "";
            for (int round = 0; round < 150; round++)
            {
                var spec = RandomTree(random, 10 + random.Next(30));
                var live = NewContext(1f, 400, 400);
                var liveElements = BuildTree(live, spec);
                live.root.AppendChild(liveElements[0]);
                live.Update();
                for (int step = 0; step < 10; step++)
                {
                    Mutate(random, spec, liveElements);
                    live.Update();
                    var fresh = NewContext(1f, 400, 400);
                    var freshElements = BuildTree(fresh, spec);
                    fresh.root.AppendChild(freshElements[0]);
                    fresh.Update();
                    string difference = CompareRecords(live, fresh) ?? live.painter.Verify(live);
                    if (difference != null)
                    {
                        if (mismatches == 0) first = $"round {round} step {step}: {difference} (full paint {live.painter.lastPaintWasFull})";
                        mismatches++;
                        break;
                    }
                }
            }
            Check("paint: incremental records match a fresh paint", mismatches == 0, mismatches + " " + first);

            {
                // Inside a record range: children added, inserted, removed and
                // reordered, restyled, faded out and in — the range repaint
                // steps over its unchanged leading children and must still
                // leave exactly what a paint from scratch would.
                var rangeRandom = new System.Random(777);
                string problem = null;
                int skips = 0;
                for (int round = 0; round < 80 && problem == null; round++)
                {
                    var ui = NewContext(1f, 400, 400);
                    var spec = RandomTree(rangeRandom, 8 + rangeRandom.Next(12));
                    var elements = BuildTree(ui, spec);
                    var specOf = new System.Collections.Generic.Dictionary<Element, NodeSpec>();
                    for (int i = 0; i < spec.Count; i++) specOf[elements[i]] = spec[i];
                    Box owner = ui.Div().ZIndex(0);
                    owner.ownsRecordRange = true;
                    ui.rangeOwners.Add(owner);
                    owner.Child(elements[0]);
                    ui.root.AppendChild(ui.Div().Column().Children(ui.Div().Height(12).Background(Color.gray), owner,
                        ui.Div().Height(12).Background(Color.gray)));
                    ui.Update();
                    for (int step = 0; step < 25 && problem == null; step++)
                    {
                        var all = new System.Collections.Generic.List<Element>();
                        Walk(elements[0], all);
                        var boxes = new System.Collections.Generic.List<Element>();
                        foreach (var e in all) if (!specOf[e].text) boxes.Add(e);
                        Element target = boxes[rangeRandom.Next(boxes.Count)];
                        int kind = rangeRandom.Next(12);
                        if (kind < 5)
                        {
                            for (int a = 1 + rangeRandom.Next(3); a > 0; a--)
                            {
                                var node = RandomNode(rangeRandom);
                                node.position = 0;
                                Element e = NewElement(ui, node);
                                specOf[e] = node;
                                target.AppendChild(e);
                            }
                        }
                        else if (kind == 5)
                        {
                            var node = RandomNode(rangeRandom);
                            Element e = NewElement(ui, node);
                            specOf[e] = node;
                            target.InsertChild(rangeRandom.Next(target.children.Count + 1), e);
                        }
                        else if (kind == 6 && all.Count > 2)
                        {
                            Element victim = all[1 + rangeRandom.Next(all.Count - 1)];
                            victim.Parent.RemoveChild(victim);
                        }
                        else if (kind == 7 && target.children.Count > 1)
                        {
                            var ordered = new System.Collections.Generic.List<Element>(target.children);
                            Element moved = ordered[0];
                            ordered.RemoveAt(0);
                            ordered.Add(moved);
                            target.ReplaceChildren(ordered);
                        }
                        else if (kind == 8 && target != elements[0])
                        {
                            float[] opacities = { 0f, 0.5f, 1f };
                            ((Box)target).Opacity(opacities[rangeRandom.Next(3)]);
                        }
                        else if (kind == 9 && target != elements[0])
                        {
                            ((Box)target).ZIndex(rangeRandom.Next(2) == 0 ? 1 : 0);
                        }
                        else
                        {
                            Element changed = all[rangeRandom.Next(all.Count)];
                            var node = specOf[changed];
                            if (node.text) { node.content = RandomWords(rangeRandom); ((FakeText)changed).Set(node.content); }
                            else if (changed != elements[0]) { RandomSizes(rangeRandom, node); ApplySpec((Box)changed, node); }
                        }
                        ui.Update();
                        problem = ui.painter.Verify(ui);
                        if (problem != null) problem = $"round {round} step {step} kind {kind}: {problem} (full {ui.painter.lastPaintWasFull})";
                        if (problem != null) { var everything = new System.Collections.Generic.List<Element>(); Walk(elements[0], everything); foreach (var pe in everything) if (!ui.paintOrder.Contains(pe)) Console.WriteLine($"  NOTPAINTED {pe.tag} rect {pe.X},{pe.Y} {pe.Width}x{pe.Height} box {pe.boxRecordStart}+{pe.boxRecordCount} pg {pe.paintGeneration} disp {pe.computed.Display} op {pe.computed.Opacity} ancestorsHidden {IsHidden(pe)} parentOp {(pe.Parent != null ? pe.Parent.computed.Opacity : 1)}"); }
                    }
                    skips += ui.painter.prefixSkips;
                }
                Check("paint: range repaints step over what is unchanged, and stay right", problem == null && skips > 50,
                    (problem ?? "") + " skips " + skips);
            }

            {
                // Loading into a record range, then showing it, then Near switching:
                // the records stay right after every paint.
                var ui = NewContext(1.5f, 900, 900);
                MaterialDesign m3 = MaterialDesign.Of(ui);
                ui.BackgroundBudget = 0f;
                Region list = ui.Deferred(async load =>
                {
                    Box column = ui.Div().Column().Gap(2);
                    load.Content.Child(column);
                    for (int i = 0; i < 60; i++)
                    {
                        column.Child(ui.Div().Row().Gap(8).Padding(6).Background(i % 2 == 0 ? Color.white : Color.gray)
                            .BorderRadius(4).Children(ui.Div().Size(10, 10).Background(Color.red)));
                        await load.Yield();
                    }
                }, () => ui.Div().Height(40).Background(Color.blue));
                Region near = ui.Near(2f, () => ui.Div().Height(80).Background(Color.green).BorderRadius(8),
                    () => ui.Div().Height(20).Background(Color.yellow));
                Box page = ui.Div().FullSize().Column().Gap(8).Padding(8).Children(near,
                    ui.ScrollArea(ui.ScrollState(), list).FlexGrow(1).MinHeight(0));
                ui.root.AppendChild(page);
                string problem = null;
                int rangePaints = 0, fullPaints = 0;
                float[] distances = new float[120];
                for (int f = 0; f < distances.Length; f++) distances[f] = f < 60 ? 10f : (f / 10) % 2 == 0 ? 1f : 10f;
                for (int f = 0; f < distances.Length && problem == null; f++)
                {
                    ui.SetViewerDistance(distances[f]);
                    ui.Tick(1f / 60f);
                    bool requested = ui.rangeRequests.Count > 0;
                    bool structure = ui.paintStructureDirty;
                    ui.Update();
                    if (ui.paintedThisFrame)
                    {
                        if (ui.painter.lastPaintWasFull) fullPaints++;
                        else if (requested && !structure) rangePaints++;
                    }
                    problem = ui.painter.Verify(ui);
                    if (problem != null) problem = "frame " + f + ": " + problem;
                    ui.RunBackground(0);
                }
                Check("paint: records stay right through loading, showing and switching", problem == null, problem ?? "");
                Check("paint: loading paints its range, not the panel", rangePaints > 0 && fullPaints < 8,
                    "range paints " + rangePaints + ", full paints " + fullPaints);
            }
        }

        static string CompareRecords(MenUIContext a, MenUIContext b)
        {
            RecordBuffer x = a.painter.records, y = b.painter.records;
            if (x.RecordCount != y.RecordCount) return $"count {x.RecordCount} vs {y.RecordCount}";
            for (int i = 0; i < x.RecordCount * RecordBuffer.TexelsPerRecord; i++)
            {
                if (x.pixels[i] != y.pixels[i]) return $"record {i / RecordBuffer.TexelsPerRecord} texel {i % RecordBuffer.TexelsPerRecord}: {x.pixels[i]} vs {y.pixels[i]}";
            }
            return null;
        }

        static bool Near(float a, float b, float tolerance = 0.001f) => Mathf.Abs(a - b) <= tolerance;

        // ---- easing, motion, animated values ----
        static void AnimationTests()
        {
            {
                Check("easing: linear", Easing.Linear.Evaluate(0.3f) == 0.3f);
                Easing ease = Easing.Ease;
                Check("easing: ends", ease.Evaluate(0f) == 0f && ease.Evaluate(1f) == 1f && ease.Evaluate(-1f) == 0f && ease.Evaluate(2f) == 1f);
                // Reference values of CSS `ease` (cubic-bezier(0.25, 0.1, 0.25, 1)).
                Check("easing: css ease", Near(ease.Evaluate(0.5f), 0.8024f, 0.002f) && Near(ease.Evaluate(0.25f), 0.4094f, 0.002f),
                    ease.Evaluate(0.5f) + " " + ease.Evaluate(0.25f));
                Easing inOut = Easing.EaseInOut;
                Check("easing: symmetric ease-in-out", Near(inOut.Evaluate(0.5f), 0.5f, 0.001f)
                    && Near(inOut.Evaluate(0.2f), 1f - inOut.Evaluate(0.8f), 0.002f));
                Easing back = Easing.CubicBezier(0.34f, 1.56f, 0.64f, 1f);   // overshoots
                float peak = 0f;
                for (int i = 0; i <= 100; i++) peak = Mathf.Max(peak, back.Evaluate(i / 100f));
                Check("easing: bezier y may overshoot", peak > 1.05f, peak.ToString());
                Easing flat = Easing.CubicBezier(0f, 0f, 1f, 1f);   // x1 == y1, x2 == y2: linear
                Check("easing: degenerate bezier is linear", Near(flat.Evaluate(0.37f), 0.37f));
                float monotone = -1f;
                bool increasing = true;
                Easing steep = Easing.CubicBezier(0.9f, 0f, 0.1f, 1f);
                for (int i = 0; i <= 200; i++)
                {
                    float v = steep.Evaluate(i / 200f);
                    if (v < monotone - 0.0005f) increasing = false;
                    monotone = v;
                }
                Check("easing: steep curve stays monotone", increasing);
                Check("easing: custom", Easing.Custom(t => t * t).Evaluate(0.5f) == 0.25f);
            }

            {
                var ui = NewContext(1f, 100, 100);
                AnimatedFloat value = ui.Animated(0f);
                int runs = 0;
                float seen = -1f;
                ui.Effect(() => { seen = value.Value; runs++; });
                bool done = false;
                value.AnimateTo(10f, AnimationSpec.Tween(0.5f, Easing.Linear), () => done = true);
                Frames(ui, 15);   // 0.25 s
                Check("animated: tween halfway", Near(seen, 5f, 0.01f) && !done, seen.ToString());
                Frames(ui, 20);
                Check("animated: tween arrives and completes", seen == 10f && done && !value.IsAnimating);
                Check("animated: one binding run per frame", runs == 1 + 30, runs.ToString());

                value.AnimateTo(0f, AnimationSpec.Spring(400f, 1f));
                float previous = value.Peek();
                bool overshoot = false, monotone = true;
                for (int i = 0; i < 120; i++)
                {
                    Frames(ui, 1);
                    if (value.Peek() < -0.0001f) overshoot = true;
                    if (value.Peek() > previous + 0.0001f) monotone = false;
                    previous = value.Peek();
                }
                Check("animated: critical spring settles without overshoot", value.Peek() == 0f && !overshoot && monotone && !value.IsAnimating);

                value.AnimateTo(10f, AnimationSpec.Spring(400f, 0.3f));
                float highest = 0f;
                for (int i = 0; i < 300; i++) { Frames(ui, 1); highest = Mathf.Max(highest, value.Peek()); }
                Check("animated: bouncy spring overshoots, then rests", highest > 10.5f && value.Peek() == 10f, highest.ToString());

                // Retargeting a spring midway keeps it continuous (no jump, velocity kept).
                value.AnimateTo(0f, AnimationSpec.Spring(300f, 0.8f));
                Frames(ui, 6);
                float before = value.Peek();
                float velocity = value.Velocity;
                value.AnimateTo(20f, AnimationSpec.Spring(300f, 0.8f));
                Check("animated: retarget keeps value and velocity", value.Peek() == before && value.Velocity == velocity);
                Frames(ui, 1);
                Check("animated: and moves on smoothly", Mathf.Abs(value.Peek() - before) < 2f, before + " -> " + value.Peek());

                value.Set(3f);
                Check("animated: set jumps and stops", value.Peek() == 3f && !value.IsAnimating && seen == 3f);

                // Delay: nothing moves before it.
                value.AnimateTo(5f, AnimationSpec.Tween(0.2f, Easing.Linear).Delay(0.2f));
                Frames(ui, 6);
                Check("animated: delay holds", value.Peek() == 3f);
                Frames(ui, 30);
                Check("animated: then moves", value.Peek() == 5f);

                // Keyframes, looping.
                value.Play(new Keyframes(0f).To(1f, 0.5f).Hold(0.5f), true);
                Frames(ui, 15);
                Check("keyframes: first segment", Near(value.Peek(), 0.5f, 0.02f), value.Peek().ToString());
                Frames(ui, 30);
                Check("keyframes: hold", value.Peek() == 1f);
                Frames(ui, 30);
                Check("keyframes: loops", value.Peek() < 0.6f && value.IsAnimating, value.Peek().ToString());
                value.Stop();
                Check("keyframes: stop", !value.IsAnimating);

                // Chained: onComplete starts the next animation.
                int legs = 0;
                value.Set(0f);
                value.AnimateTo(1f, AnimationSpec.Tween(0.1f), () => { legs++; value.AnimateTo(2f, AnimationSpec.Tween(0.1f), () => legs++); });
                Frames(ui, 30);
                Check("animated: chained animations", legs == 2 && value.Peek() == 2f);

                AnimatedColor color = ui.Animated(Colors.Black);
                color.AnimateTo(Colors.White, AnimationSpec.Tween(0.2f, Easing.Linear));
                Frames(ui, 6);
                Check("animated color: midway", Near(color.Peek().r, 0.5f, 0.02f) && color.Peek().a == 1f, color.Peek().ToString());
                Frames(ui, 10);
                Check("animated color: arrives", color.Peek() == Colors.White);

                float total = 0f;
                float last = -1f;
                ui.Animate(0.5f, t => last = t, null, () => total = 1f);
                Frames(ui, 10);
                Check("animate: progress", last > 0.2f && last < 0.5f && total == 0f, last.ToString());
                Frames(ui, 30);
                Check("animate: ends with 1", last == 1f && total == 1f);
                int ticks = 0;
                Playback every = ui.EveryFrame(dt => ticks++);
                Frames(ui, 5);
                every.Stop();
                Frames(ui, 5);
                Check("every frame: until stopped", ticks == 5, ticks.ToString());

                // An animation made in a scope stops with it.
                var show = ui.Signal(true);
                AnimatedFloat inner = null;
                Layout(ui, ui.Div().Children(ui.Show(() => show.Value, () =>
                {
                    inner = ui.Animated(0f);
                    inner.AnimateTo(1f, AnimationSpec.Tween(1f));
                    return ui.Div();
                })));
                Frames(ui, 5);
                float stoppedAt = inner.Peek();
                show.Value = false;
                Frames(ui, 30);
                Check("animated: stops with its scope", inner.Peek() == stoppedAt && stoppedAt > 0f);
            }
        }

        // ---- style transitions, enter and exit ----
        static void TransitionTests()
        {
            {
                var ui = NewContext(1f, 200, 200);
                var on = ui.Signal(false);
                Box box = ui.Div().Size(50, 50).Background(() => on.Value ? Colors.White : Colors.Black)
                    .Transition(StyleProperty.Background, AnimationSpec.Tween(0.2f, Easing.Linear));
                Box after = ui.Div().Size(10, 10);
                Layout(ui, ui.Div().Children(box, after));
                Check("transition: no animation on the first style", box.computed.ColorOf(StyleProperty.Background) == Colors.Black
                    && box.animator == null);
                on.Value = true;
                Relayout(ui);
                Check("transition: starts where it was", box.computed.ColorOf(StyleProperty.Background) == Colors.Black);
                Frames(ui, 6);
                Color mid = box.computed.ColorOf(StyleProperty.Background);
                Check("transition: midway", Near(mid.r, 0.5f, 0.03f), mid.ToString());
                Check("transition: a color change does not lay out", ui.layout.arrangedCount == 0, "arranged=" + ui.layout.arrangedCount);
                Frames(ui, 10);
                Check("transition: arrives", box.computed.ColorOf(StyleProperty.Background) == Colors.White && !box.animator.IsRunning);
                on.Value = false;
                Relayout(ui);
                Frames(ui, 3);
                on.Value = true;   // back midway: from where it is, no jump
                Relayout(ui);
                Color turned = box.computed.ColorOf(StyleProperty.Background);
                Check("transition: retarget from the shown value", turned.r > 0.5f && turned.r < 1f, turned.ToString());
                Frames(ui, 20);
                Check("transition: retarget arrives", box.computed.ColorOf(StyleProperty.Background) == Colors.White);
            }

            {
                // A width that springs: the layout follows each frame.
                var ui = NewContext(1f, 400, 200);
                var wide = ui.Signal(false);
                Box grow = ui.Div().Height(20).Width(() => wide.Value ? (Length)200 : 50)
                    .Transition(StyleProperty.Width, AnimationSpec.Tween(0.25f, Easing.Linear));
                Box next = ui.Div().Size(10, 20);
                Layout(ui, ui.Div().Row().Children(grow, next));
                wide.Value = true;
                Relayout(ui);
                Check("width transition: starts at the old width", grow.Width == 50 && next.X == 50);
                Frames(ui, 8);
                Check("width transition: siblings follow midway", grow.Width > 80 && grow.Width < 170 && next.X == grow.Width, R(grow) + R(next));
                Frames(ui, 20);
                Check("width transition: arrives", grow.Width == 200 && next.X == 200);
                // auto -> px cannot interpolate: it jumps.
                grow.Width(Length.Auto);
                Relayout(ui);
                Check("width transition: auto jumps", grow.animator == null || !grow.animator.IsRunning);
            }

            {
                // Inherited: the text color reaches the children.
                var ui = NewContext(1f, 200, 200);
                var red = ui.Signal(false);
                var label = new FakeText(ui, "x");
                Box holder = ui.Div().TextColor(() => red.Value ? Color.red : Color.black)
                    .Transition(StyleProperty.TextColor, AnimationSpec.Tween(0.2f, Easing.Linear)).Children(label);
                Layout(ui, holder);
                red.Value = true;
                Relayout(ui);
                Frames(ui, 6);
                Check("inherited transition: children follow", Near(label.computed.ColorOf(StyleProperty.TextColor).r, 0.5f, 0.05f),
                    label.computed.ColorOf(StyleProperty.TextColor).ToString());
                Frames(ui, 10);
                Check("inherited transition: arrives", label.computed.ColorOf(StyleProperty.TextColor) == Color.red
                    && holder.animator.animations.Count == 0);
            }

            {
                // Enter and exit through Show.
                var ui = NewContext(1f, 200, 200);
                var open = ui.Signal(false);
                int cleanups = 0;
                Box panel = null;
                Region region = ui.Show(() => open.Value, () =>
                {
                    ui.OnCleanup(() => cleanups++);
                    panel = ui.Div().Size(40, 40).Background(Colors.White)
                        .EnterFrom(s => s.Opacity(0f)).ExitTo(s => s.Opacity(0f))
                        .Transition(StyleProperty.Opacity, AnimationSpec.Tween(0.2f, Easing.Linear));
                    return panel;
                });
                Layout(ui, ui.Div().Children(region));
                open.Value = true;
                Relayout(ui);
                Check("enter: starts from :enter-from", panel.computed.Opacity == 0f);
                Frames(ui, 6);
                Check("enter: fades in", panel.computed.Opacity > 0.3f && panel.computed.Opacity < 0.7f, panel.computed.Opacity.ToString());
                Frames(ui, 10);
                Check("enter: arrives", panel.computed.Opacity == 1f);
                open.Value = false;
                Relayout(ui);
                Check("exit: stays while exiting", region.children.Count == 1 && cleanups == 0
                    && (panel.pseudoStates & PseudoState.ExitTo) != 0);
                Frames(ui, 6);
                Check("exit: fades out", panel.computed.Opacity > 0.2f && panel.computed.Opacity < 0.8f && region.children.Count == 1,
                    panel.computed.Opacity.ToString());
                Frames(ui, 20);
                Check("exit: removed and disposed after", region.children.Count == 0 && cleanups == 1);

                // Reopened while the old one is still leaving: both are there for a moment.
                open.Value = true;
                Relayout(ui);
                Frames(ui, 20);
                Box first = panel;
                open.Value = false;
                Relayout(ui);
                Frames(ui, 2);
                open.Value = true;
                Relayout(ui);
                Check("exit: a new branch while the old leaves", region.children.Count == 2 && panel != first);
                Frames(ui, 30);
                Check("exit: the old one goes", region.children.Count == 1 && region.children[0] == panel);

                // Without :exit-to, removal is immediate.
                var plain = ui.Signal(true);
                Region quick = ui.Show(() => plain.Value, () => ui.Div().Size(5, 5));
                ui.root.children[0].AppendChild(quick);
                Relayout(ui);
                plain.Value = false;
                Check("exit: none means immediate", quick.children.Count == 0);
            }

            {
                // Exit from a keyed list keeps the element in its place.
                var ui = NewContext(1f, 200, 300);
                var items = ui.Signal(new System.Collections.Generic.List<int> { 1, 2, 3 });
                Region list = ui.For(() => items.Value, i => i, (item, index) =>
                    ui.Div().Height(20).ExitTo(s => s.Opacity(0f)).Transition(StyleProperty.Opacity, AnimationSpec.Tween(0.2f)));
                Layout(ui, ui.Div().Children(list));
                Element second = list.children[1];
                items.Value = new System.Collections.Generic.List<int> { 1, 3, 4 };
                Relayout(ui);
                Check("list exit: stays in place while leaving", list.children.Count == 4 && list.children[1] == second
                    && second.Y == 20, list.children.Count + " " + R(second));
                Frames(ui, 30);
                Check("list exit: then removed", list.children.Count == 3 && !list.children.Contains(second));
            }

            {
                // Dynamic: the old content exits while the new one is there.
                var ui = NewContext(1f, 200, 200);
                var page = ui.Signal(0);
                Region dynamic = ui.Dynamic(() => ui.Div().Height(10 + page.Value)
                    .ExitTo(s => s.Opacity(0f)).Transition(StyleProperty.Opacity, AnimationSpec.Tween(0.1f)));
                Layout(ui, ui.Div().Children(dynamic));
                page.Value = 1;
                Relayout(ui);
                Check("dynamic exit: both for a moment", dynamic.children.Count == 2);
                Frames(ui, 20);
                Check("dynamic exit: then the new one", dynamic.children.Count == 1 && dynamic.children[0].Height == 11);
            }

            {
                // Style sheet rules can declare transitions and :enter-from.
                var ui = NewContext(1f, 200, 200);
                ui.AddStyleSheet(new StyleSheet()
                    .Rule("card", s => s.Size(30, 30).BorderRadius(0).Transition(TransitionGroup.Corners, AnimationSpec.Tween(0.2f, Easing.Linear)))
                    .Rule("card:enter-from", s => s.BorderRadius(15))
                    .Rule("card:hover", s => s.BorderRadius(8)));
                Box card = ui.Div().Tag("card");
                Layout(ui, ui.Div().Children(card));
                Check("rule transition: enter from a rule", card.computed.numbers[StyleProperty.CornerTopLeft] == 15f);
                Frames(ui, 20);
                Check("rule transition: arrives", card.computed.numbers[StyleProperty.CornerTopLeft] == 0f);
                card.SetPseudoState(PseudoState.Hover, true);
                Relayout(ui);
                Frames(ui, 6);
                float corner = card.computed.numbers[StyleProperty.CornerBottomLeft];
                Check("rule transition: hover animates the group", corner > 2f && corner < 6f, corner.ToString());
            }
        }

        // ---- early cutoff: a change stops where sizes stop changing ----
        static void CutoffTests()
        {
            {
                var ui = NewContext(1f, 300, 300);
                var w = ui.Signal((Length)10);
                Box changing = ui.Div().Height(10).Width(() => w.Value);
                Box fixedRow = ui.Div().Row().Height(30).Children(changing, ui.Div().Size(10, 10));   // auto width: not a boundary
                Box[] siblings = new Box[4];
                Box column = ui.Div().Column().Child(fixedRow);
                for (int i = 0; i < siblings.Length; i++) { siblings[i] = ui.Div().Height(20); column.Child(siblings[i]); }
                Layout(ui, column);
                w.Value = 60;
                Relayout(ui);
                Check("cutoff: inside updated", changing.Width == 60 && fixedRow.children[1].X == 60, R(fixedRow.children[1]));
                Check("cutoff: stops at the row (same size)", ui.layout.arrangedCount == 3, "arranged=" + ui.layout.arrangedCount);
            }

            {
                // Text in a wrapping paragraph: a longer text grows its box, and
                // the change reaches up only while sizes change.
                var ui = NewContext(1f, 300, 300);
                var label = new FakeText(ui, "abc");
                Box card = ui.Div().Width(200).Padding(5).Children(label);   // fixed width, auto height
                Box outer = ui.Div().Column().Children(card, ui.Div().Height(10));
                Layout(ui, outer);
                label.Set("abcdef");   // wider, same height
                Relayout(ui);
                Check("cutoff: text width change stays in the card", ui.layout.arrangedCount == 1, "arranged=" + ui.layout.arrangedCount);
            }

            // Randomized: incremental layout must equal a layout from scratch.
            var random = new System.Random(1234);
            int mismatches = 0;
            string firstMismatch = "";
            for (int round = 0; round < 40; round++)
            {
                var spec = RandomTree(random, 18);
                var live = NewContext(1f, 400, 400);
                var liveElements = BuildTree(live, spec);
                Layout(live, liveElements[0]);
                for (int step = 0; step < 12; step++)
                {
                    Mutate(random, spec, liveElements);
                    Relayout(live);
                    var fresh = NewContext(1f, 400, 400);
                    var freshElements = BuildTree(fresh, spec);
                    Layout(fresh, freshElements[0]);
                    for (int i = 0; i < spec.Count; i++)
                    {
                        Element a = liveElements[i], b = freshElements[i];
                        if (IsHidden(b)) continue;   // display: none — no box to compare
                        if (a.X != b.X || a.Y != b.Y || a.Width != b.Width || a.Height != b.Height)
                        {
                            if (mismatches == 0) firstMismatch = $"round {round} step {step} node {i}: {R(a)} vs {R(b)}";
                            mismatches++;
                            break;
                        }
                    }
                }
            }
            Check("cutoff: incremental layout matches a fresh layout", mismatches == 0, mismatches + " " + firstMismatch);

            // Randomized, with children added, inserted, removed and reordered
            // (columns lay out only what follows their settled children).
            var structureRandom = new System.Random(4321);
            int structureMismatches = 0;
            string firstStructureMismatch = "";
            for (int round = 0; round < 60 && structureMismatches == 0; round++)
            {
                var spec = RandomTree(structureRandom, 12);
                var live = NewContext(1f, 400, 400);
                var liveElements = BuildTree(live, spec);
                var specOf = new System.Collections.Generic.Dictionary<Element, NodeSpec>();
                for (int i = 0; i < spec.Count; i++) specOf[liveElements[i]] = spec[i];
                Layout(live, liveElements[0]);
                for (int step = 0; step < 15 && structureMismatches == 0; step++)
                {
                    var boxes = new System.Collections.Generic.List<Element>();
                    var all = new System.Collections.Generic.List<Element>();
                    Walk(liveElements[0], all);
                    foreach (var e in all) if (!specOf[e].text) boxes.Add(e);
                    int kind = structureRandom.Next(10);
                    Element target = boxes[structureRandom.Next(boxes.Count)];
                    if (kind < 5)
                    {
                        // Append a few (the common case: a list growing).
                        int added = 1 + structureRandom.Next(3);
                        for (int a = 0; a < added; a++)
                        {
                            var node = RandomNode(structureRandom);
                            Element e = NewElement(live, node);
                            specOf[e] = node;
                            target.AppendChild(e);
                        }
                    }
                    else if (kind == 5)
                    {
                        var node = RandomNode(structureRandom);
                        Element e = NewElement(live, node);
                        specOf[e] = node;
                        target.InsertChild(structureRandom.Next(target.children.Count + 1), e);
                    }
                    else if (kind == 6 && all.Count > 2)
                    {
                        Element victim = all[1 + structureRandom.Next(all.Count - 1)];
                        victim.Parent.RemoveChild(victim);
                    }
                    else if (kind == 7 && target.children.Count > 1)
                    {
                        var ordered = new System.Collections.Generic.List<Element>(target.children);
                        int from = structureRandom.Next(ordered.Count);
                        Element moved = ordered[from];
                        ordered.RemoveAt(from);
                        ordered.Insert(structureRandom.Next(ordered.Count + 1), moved);
                        if (structureRandom.Next(2) == 0)
                        {
                            var node = RandomNode(structureRandom);
                            Element e = NewElement(live, node);
                            specOf[e] = node;
                            ordered.Add(e);
                        }
                        target.ReplaceChildren(ordered);
                    }
                    else
                    {
                        Element changed = all[structureRandom.Next(all.Count)];
                        var node = specOf[changed];
                        if (node.text) { node.content = RandomWords(structureRandom); ((FakeText)changed).Set(node.content); }
                        else if (changed != liveElements[0])
                        {
                            if (structureRandom.Next(3) == 0) node.display = structureRandom.Next(3);
                            else RandomSizes(structureRandom, node);
                            ApplySpec((Box)changed, node);
                        }
                    }
                    Relayout(live);
                    var fresh = NewContext(1f, 400, 400);
                    Element freshRoot = CloneTree(fresh, liveElements[0], specOf);
                    Layout(fresh, freshRoot);
                    var liveAll = new System.Collections.Generic.List<Element>();
                    var freshAll = new System.Collections.Generic.List<Element>();
                    Walk(liveElements[0], liveAll);
                    Walk(freshRoot, freshAll);
                    for (int i = 0; i < liveAll.Count; i++)
                    {
                        Element a = liveAll[i], b = freshAll[i];
                        if (IsHidden(b)) continue;
                        if (a.X != b.X || a.Y != b.Y || a.Width != b.Width || a.Height != b.Height)
                        {
                            firstStructureMismatch = $"round {round} step {step} kind {kind} node {i}: {R(a)} vs {R(b)}";
                            structureMismatches++;
                            break;
                        }
                    }
                }
            }
            Check("stack: incremental layout matches a fresh one through added, moved and removed children",
                structureMismatches == 0, firstStructureMismatch);

            // The fit-content shortcut for flex rows gives the same layout as the full measure.
            int differences = 0;
            string firstDifference = "";
            var shortcutRandom = new System.Random(99);
            for (int round = 0; round < 300; round++)
            {
                var spec = RandomTree(shortcutRandom, 10 + shortcutRandom.Next(30));
                var fast = NewContext(1f, 400, 400);
                var fastElements = BuildTree(fast, spec);
                Layout(fast, fastElements[0]);
                var slow = NewContext(1f, 400, 400);
                slow.layout.fitShortcut = false;
                var slowElements = BuildTree(slow, spec);
                Layout(slow, slowElements[0]);
                for (int i = 0; i < spec.Count; i++)
                {
                    Element a = fastElements[i], b = slowElements[i];
                    if (IsHidden(b)) continue;
                    if (a.X != b.X || a.Y != b.Y || a.Width != b.Width || a.Height != b.Height)
                    {
                        if (differences == 0) firstDifference = $"round {round} node {i}: {R(a)} vs {R(b)}";
                        differences++;
                        break;
                    }
                }
            }
            Check("fit-content shortcut: same layout", differences == 0, differences + " " + firstDifference);
        }

        static void Walk(Element e, System.Collections.Generic.List<Element> into)
        {
            into.Add(e);
            for (int i = 0; i < e.children.Count; i++) Walk(e.children[i], into);
        }

        static NodeSpec RandomNode(System.Random random)
        {
            var node = new NodeSpec { text = random.Next(3) == 0 };
            if (node.text) node.content = RandomWords(random);
            else
            {
                node.display = random.Next(3);
                node.wrap = random.Next(4) == 0;
                node.align = random.Next(3);
                RandomSizes(random, node);
            }
            return node;
        }

        static Element NewElement(MenUIContext ui, NodeSpec node)
        {
            if (node.text) return new FakeText(ui, node.content);
            Box box = ui.Div();
            ApplySpec(box, node);
            return box;
        }

        static Element CloneTree(MenUIContext ui, Element source, System.Collections.Generic.Dictionary<Element, NodeSpec> specOf)
        {
            Element copy = NewElement(ui, specOf[source]);
            for (int i = 0; i < source.children.Count; i++) copy.AppendChild(CloneTree(ui, source.children[i], specOf));
            return copy;
        }

        static bool IsHidden(Element e)
        {
            for (Element a = e; a != null; a = a.Parent)
            {
                if (a.computed.Display == DisplayMode.None) return true;
            }
            return false;
        }

        sealed class NodeSpec
        {
            public int parent = -1;
            public bool text;
            public string content = "";
            public int display;       // 0 block, 1 flex row, 2 flex column
            public bool wrap;
            public int width = -1;    // -1 auto, -2 50%
            public int height = -1;
            public int padding, margin, gap;
            public float grow;
            public int align;         // 0 stretch, 1 start, 2 center
            public int position;      // 0 in flow, 1 absolute at left/top, 2 absolute inset on both sides
            public bool hidden, clip, noShrink;
            public int minWidth = -1;
            public int look;          // background / border / radius / shadow variant
        }

        static System.Collections.Generic.List<NodeSpec> RandomTree(System.Random random, int count)
        {
            var nodes = new System.Collections.Generic.List<NodeSpec>();
            nodes.Add(new NodeSpec { display = 2, width = 380 });
            for (int i = 1; i < count; i++)
            {
                int parent;
                do { parent = random.Next(i); } while (nodes[parent].text);
                var node = new NodeSpec { parent = parent, text = random.Next(3) == 0 };
                if (node.text)
                {
                    node.content = RandomWords(random);
                }
                else
                {
                    node.display = random.Next(3);
                    node.wrap = random.Next(4) == 0;
                    node.align = random.Next(3);
                    RandomSizes(random, node);
                }
                nodes.Add(node);
            }
            return nodes;
        }

        static string RandomWords(System.Random random)
        {
            int words = 1 + random.Next(4);
            string text = "";
            for (int w = 0; w < words; w++) text += (w > 0 ? " " : "") + new string('a', 1 + random.Next(6));
            return text;
        }

        static void RandomSizes(System.Random random, NodeSpec node)
        {
            int kind = random.Next(5);
            node.width = kind == 0 ? 20 + random.Next(120) : kind == 1 ? -2 : -1;
            node.height = random.Next(4) == 0 ? 10 + random.Next(60) : -1;
            node.padding = random.Next(3) * 3;
            node.margin = random.Next(3) * 2;
            node.gap = random.Next(3) * 4;
            node.grow = random.Next(3) == 0 ? 1f : 0f;
            node.position = random.Next(8) == 0 ? 1 + random.Next(2) : 0;
            node.hidden = random.Next(10) == 0;
            node.clip = random.Next(6) == 0;
            node.noShrink = random.Next(4) == 0;
            node.minWidth = random.Next(6) == 0 ? random.Next(80) : -1;
            node.look = random.Next(6);
        }

        static System.Collections.Generic.List<Element> BuildTree(MenUIContext ui, System.Collections.Generic.List<NodeSpec> spec)
        {
            var elements = new System.Collections.Generic.List<Element>();
            foreach (var node in spec)
            {
                Element e;
                if (node.text)
                {
                    e = new FakeText(ui, node.content);
                }
                else
                {
                    Box box = ui.Div();
                    ApplySpec(box, node);
                    e = box;
                }
                elements.Add(e);
                if (node.parent >= 0) elements[node.parent].AppendChild(e);
            }
            return elements;
        }

        static void ApplySpec(Box box, NodeSpec node)
        {
            if (node.display == 0) box.Display(DisplayMode.Block);
            else box.Flex().FlexDirection(node.display == 1 ? Direction.Row : Direction.Column);
            box.FlexWrap(node.wrap ? MenUI.WrapMode.Wrap : MenUI.WrapMode.NoWrap);
            box.AlignItems(node.align == 0 ? Align.Stretch : node.align == 1 ? Align.Start : Align.Center);
            box.Width(node.width == -1 ? Length.Auto : node.width == -2 ? Length.Percent(50) : (Length)node.width);
            box.Height(node.height < 0 ? Length.Auto : (Length)node.height);
            box.Padding(node.padding).Margin(node.margin).Gap(node.gap).FlexGrow(node.grow);
            box.FlexShrink(node.noShrink ? 0f : 1f);
            box.MinWidth(node.minWidth < 0 ? Length.Auto : (Length)node.minWidth);
            box.Overflow(node.clip ? OverflowMode.Hidden : OverflowMode.Visible);
            if (node.hidden && node.display == 0) box.Display(DisplayMode.None);
            box.Position(node.position == 0 ? PositionMode.Relative : PositionMode.Absolute);
            // Something to draw: a background, sometimes a border, radius or shadow.
            box.Background(node.look == 0 ? Colors.Transparent : Color.HSVToRGB(node.look / 6f, 0.5f, 0.8f))
                .BorderRadius(node.look * 2).BorderWidth(node.look == 3 ? 2 : 0).BorderColor(Color.black)
                .BoxShadow(node.look == 5 ? new[] { Shadow.Small } : new Shadow[0]);
            if (node.position == 2) box.Inset(3);
            else if (node.position == 1) box.Left(5).Top(7).Right(Length.Auto).Bottom(Length.Auto);
            else box.Left(Length.Auto).Top(Length.Auto).Right(Length.Auto).Bottom(Length.Auto);
        }

        static void Mutate(System.Random random, System.Collections.Generic.List<NodeSpec> spec, System.Collections.Generic.List<Element> elements)
        {
            int changes = 1 + random.Next(3);
            for (int c = 0; c < changes; c++)
            {
                int i = random.Next(spec.Count);
                var node = spec[i];
                if (node.text)
                {
                    node.content = RandomWords(random);
                    ((FakeText)elements[i]).Set(node.content);
                }
                else if (i > 0)
                {
                    if (random.Next(3) == 0) node.display = random.Next(3);
                    else RandomSizes(random, node);
                    ApplySpec((Box)elements[i], node);
                }
            }
        }

        public static int Main()
        {
            Debug.unityLogger.logHandler = log;
            // ---- reactive ----
            {
                var ui = NewContext(1f, 100, 100);
                var a = ui.Signal(1);
                var b = ui.Signal(10);
                var sum = ui.Computed(() => a.Value + b.Value);
                int runs = 0; int seen = 0;
                ui.Effect(() => { runs++; seen = sum.Value; });
                Check("effect initial", runs == 1 && seen == 11, $"runs={runs} seen={seen}");
                a.Value = 2;
                Check("effect after set", runs == 2 && seen == 12, $"runs={runs} seen={seen}");
                a.Value = 2;
                Check("equal set is ignored", runs == 2);
                ui.Batch(() => { a.Value = 5; b.Value = 20; });
                Check("batch runs once", runs == 3 && seen == 25, $"runs={runs} seen={seen}");
                var flag = ui.Signal(true);
                int aReads = 0;
                ui.Effect(() => { if (flag.Value) { aReads = a.Value; } else { aReads = -b.Value; } });
                flag.Value = false;
                int before = aReads;
                a.Value = 99;   // no longer a dependency
                Check("dynamic dependencies", aReads == before && aReads == -20, $"aReads={aReads}");
            }

            // ---- Show disposes the old branch ----
            {
                var ui = NewContext(1f, 100, 100);
                var on = ui.Signal(true);
                var label = ui.Signal("x");
                int branchRuns = 0;
                Region region = ui.Show(() => on.Value, () => { var t = ui.Div(); ui.Effect(() => { branchRuns++; var s = label.Value; }); return t; }, () => ui.Div());
                Check("show builds branch", region.children.Count == 1 && branchRuns == 1);
                on.Value = false;
                int afterSwitch = branchRuns;
                label.Value = "y";
                Check("old branch effect disposed", branchRuns == afterSwitch, $"runs={branchRuns}");
                on.Value = true;
                Check("branch rebuilt", branchRuns == afterSwitch + 1 && region.children.Count == 1);
            }

            // ---- flex-grow cumulative rounding: 3 x grow 1 in 100px ----
            {
                var ui = NewContext(1f, 100, 50);
                Box a = ui.Div().FlexGrow(1), b = ui.Div().FlexGrow(1), c = ui.Div().FlexGrow(1);
                var row = ui.Div().Flex().FullSize().Children(a, b, c);
                Layout(ui, row);
                Check("grow 33/34/33", a.Width == 33 && b.Width == 34 && c.Width == 33 && b.X == 33 && c.X == 67, R(a) + R(b) + R(c));
                Check("stretch cross", a.Height == 50, R(a));
            }

            // ---- scale snapping: padding 12.5 at scale 1.5 = 19 ----
            {
                var ui = NewContext(1.5f, 300, 300);
                Box inner = ui.Div().Height(10);
                Box outer = ui.Div().Padding(12.5f).Children(inner);
                Layout(ui, outer);
                Check("padding snapped", inner.X == 19 && inner.Y == 19 && inner.Width == 300 - 38 && inner.Height == 15, R(inner));
                Check("block height from content", outer.Height == 15 + 38, R(outer));
            }

            // ---- block flow stacks, width fills ----
            {
                var ui = NewContext(1f, 200, 200);
                Box a = ui.Div().Height(20), b = ui.Div().Height(30).Margin(5);
                Layout(ui, ui.Div().Children(a, b));
                Check("block stacking", a.Y == 0 && a.Width == 200 && b.Y == 25 && b.X == 5 && b.Width == 190, R(a) + R(b));
            }

            // ---- justify-content / align-items / gap ----
            {
                var ui = NewContext(1f, 100, 40);
                Box a = ui.Div().Size(10, 10), b = ui.Div().Size(10, 10), c = ui.Div().Size(10, 10);
                Layout(ui, ui.Div().Row().FullSize().JustifyContent(Justify.SpaceBetween).AlignItems(Align.Center).Children(a, b, c));
                Check("space-between", a.X == 0 && b.X == 45 && c.X == 90, R(a) + R(b) + R(c));
                Check("align center", a.Y == 15, R(a));
            }
            {
                var ui = NewContext(1f, 100, 40);
                Box a = ui.Div().Size(10, 10), b = ui.Div().Size(10, 10);
                Layout(ui, ui.Div().Row().Gap(7).JustifyContent(Justify.End).FullWidth().Children(a, b));
                Check("gap + end", a.X == 73 && b.X == 90, R(a) + R(b));
            }
            {
                var ui = NewContext(1f, 100, 40);
                Box a = ui.Div().Size(10, 10), b = ui.Div().Size(20, 10);
                Layout(ui, ui.Div().Flex().FlexDirection(Direction.RowReverse).FullWidth().Children(a, b));
                Check("row-reverse", a.X == 90 && b.X == 70, R(a) + R(b));
            }

            // ---- shrink ----
            {
                var ui = NewContext(1f, 100, 40);
                Box a = ui.Div().Width(80), b = ui.Div().Width(80);
                Layout(ui, ui.Div().Row().FullWidth().Children(a, b));
                Check("shrink evenly", a.Width == 50 && b.Width == 50 && b.X == 50, R(a) + R(b));
            }

            // ---- wrap ----
            {
                var ui = NewContext(1f, 100, 100);
                Box a = ui.Div().Size(40, 10), b = ui.Div().Size(40, 20), c = ui.Div().Size(40, 10);
                Layout(ui, ui.Div().Row().FlexWrap(WrapMode.Wrap).Gap(5).Width(90).Children(a, b, c));
                Check("wrap lines", a.Y == 0 && b.X == 45 && c.X == 0 && c.Y == 25, R(a) + R(b) + R(c));
            }

            // ---- column + grow in fixed height ----
            {
                var ui = NewContext(1f, 100, 100);
                Box header = ui.Div().Height(20), body = ui.Div().FlexGrow(1), footer = ui.Div().Height(10);
                Layout(ui, ui.Div().Column().FullSize().Children(header, body, footer));
                Check("column grow", body.Y == 20 && body.Height == 70 && footer.Y == 90 && body.Width == 100, R(body) + R(footer));
            }

            // ---- absolute + relative + percent ----
            {
                var ui = NewContext(1f, 200, 100);
                Box abs = ui.Div().Absolute().Right(10).Bottom(5).Size(Length.Percent(25), 20);
                Box stretched = ui.Div().Absolute().Left(10).Right(10).Top(0).Height(10);
                Box rel = ui.Div().Height(10).Left(3).Top(4).Position(PositionMode.Relative);
                Layout(ui, ui.Div().FullSize().Border(2, Color.white).Children(abs, stretched, rel));
                Check("absolute right/bottom %", abs.Width == 49 && abs.X == 200 - 2 - 10 - 49 && abs.Y == 100 - 2 - 5 - 20, R(abs));
                Check("absolute left+right stretch", stretched.X == 12 && stretched.Width == 176, R(stretched));
                Check("relative offset", rel.X == 5 && rel.Y == 6 && rel.Width == 196, R(rel));
            }

            // ---- display: none and contents ----
            {
                var ui = NewContext(1f, 100, 100);
                Box hidden = ui.Div().Height(50).Display(DisplayMode.None);
                Box a = ui.Div().Size(10, 10);
                Region region = ui.Show(() => true, () => ui.Div().Size(20, 10));
                Layout(ui, ui.Div().Row().Children(hidden, a, region));
                Check("display none skipped", a.X == 0);
                Check("contents flattened", region.children[0].X == 10 && region.children[0].Width == 20, R(region.children[0]));
            }

            // ---- incremental relayout after a signal ----
            {
                var ui = NewContext(1f, 100, 100);
                var w = ui.Signal((Length)10);
                Box a = ui.Div().Height(10).Width(() => w.Value);
                Box b = ui.Div().Size(10, 10);
                Layout(ui, ui.Div().Row().Children(a, b));
                w.Value = 30;
                Relayout(ui);
                Check("reactive width relayout", a.Width == 30 && b.X == 30, R(a) + R(b));
            }

            // ---- max-width and min-width in flex ----
            {
                var ui = NewContext(1f, 100, 100);
                Box a = ui.Div().FlexGrow(1).MaxWidth(20), b = ui.Div().Width(10).MinWidth(30);
                Layout(ui, ui.Div().Row().FullWidth().Children(a, b));
                Check("max/min", a.Width == 20 && b.Width == 30 && b.X == 20, R(a) + R(b));
            }

            // ---- clip from overflow hidden ----
            {
                var ui = NewContext(1f, 100, 100);
                Box child = ui.Div().Size(200, 200);
                Box clip = ui.Div().Size(50, 40).Margin(5).OverflowHidden().Border(1, Color.white).Children(child);
                Layout(ui, clip);
                Check("clip rect", child.clipLeft == 6 && child.clipTop == 6 && child.clipRight == 54 && child.clipBottom == 44,
                    $"{child.clipLeft},{child.clipTop},{child.clipRight},{child.clipBottom}");
            }


            // ---- relayout boundary: a change inside a fixed-size box stays inside ----
            {
                var ui = NewContext(1f, 200, 200);
                var w = ui.Signal((Length)10);
                Box inner = ui.Div().Height(10).Width(() => w.Value);
                Box innerSibling = ui.Div().Size(10, 10);
                // flex-shrink 0: a fixed size its content cannot change (a
                // shrinkable flex item has a content-based minimum).
                Box fixedBox = ui.Div().Row().Size(100, 50).FlexShrink(0).Children(inner, innerSibling);
                Box outsideA = ui.Div().Size(30, 30);
                Box outsideB = ui.Div().Size(30, 30);
                Layout(ui, ui.Div().Row().Gap(5).Children(outsideA, fixedBox, outsideB));
                int before = outsideB.X;
                w.Value = 40;
                Relayout(ui);
                Check("boundary: inside updated", inner.Width == 40 && innerSibling.X == fixedBox.X + 40, R(inner) + R(innerSibling));
                Check("boundary: outside untouched", outsideB.X == before, R(outsideB));
                Check("boundary: only the boundary subtree arranged", ui.layout.arrangedCount == 3, "arranged=" + ui.layout.arrangedCount);
                Check("queue drained", ui.layoutQueue.Count == 0 && !fixedBox.queuedForLayout);
            }

            // ---- Pages: built once, then only hidden and shown ----
            {
                var ui = NewContext(1f, 200, 200);
                var page = ui.Signal(0);
                int builds = 0;
                Box first = null;
                Region pages = ui.Pages(() => page.Value,
                    () => { builds++; return first = ui.Div().Height(30).Children(new FakeText(ui, "one")); },
                    () => { builds++; return ui.Div().Height(50); });
                Box tail = ui.Div().Height(10);
                Layout(ui, ui.Div().Children(pages, tail));
                Check("pages: only the shown page is built", builds == 1 && tail.Y == 30, R(tail));
                page.Value = 1;
                Relayout(ui);
                Check("pages: the second one built on first visit", builds == 2 && tail.Y == 50, R(tail));
                page.Value = 0;
                Relayout(ui);
                Check("pages: switching back rebuilds nothing, restyles and relays out little",
                    builds == 2 && tail.Y == 30 && first.children[0].computed.FontSize == 16f && ui.layout.arrangedCount <= 4,
                    "arranged=" + ui.layout.arrangedCount);
            }

            // ---- a fixed-size flex item still shrinks to its content minimum ----
            {
                var ui = NewContext(1f, 200, 200);
                var tall = ui.Signal(true);
                Box content = ui.Div().Width(10).Height(() => tall.Value ? (Length)60 : 20);
                Box item = ui.Div().Size(50, 80).Children(content);          // min-height: auto = min(80, content)
                Box other = ui.Div().Height(60).FlexShrink(0);
                Layout(ui, ui.Div().Column().Height(100).Children(item, other));
                Check("content minimum: before", item.Height == 60, R(item));   // shrunk from 80 to its content
                tall.Value = false;
                Relayout(ui);
                Check("content minimum: follows the content", item.Height == 40 && other.Y == 40, R(item) + R(other));
            }

            // ---- a size change on a boundary itself reaches its parent ----
            {
                var ui = NewContext(1f, 200, 200);
                var w = ui.Signal((Length)50);
                Box fixedBox = ui.Div().Height(20).Width(() => w.Value);
                Box after = ui.Div().Size(10, 10);
                Layout(ui, ui.Div().Row().Children(fixedBox, after));
                w.Value = 70;
                Relayout(ui);
                Check("boundary resize moves siblings", fixedBox.Width == 70 && after.X == 70, R(fixedBox) + R(after));
            }

            // ---- clean siblings are skipped when an auto-sized element changes ----
            {
                var ui = NewContext(1f, 300, 300);
                var h = ui.Signal((Length)10);
                Box growing = ui.Div().Width(50).Height(() => h.Value);
                Box[] others = new Box[5];
                Box column = ui.Div().Column();
                column.Child(ui.Div().Children(growing));
                for (int i = 0; i < others.Length; i++) { others[i] = ui.Div().Size(20, 20).Children(ui.Div().Size(5, 5)); column.Child(others[i]); }
                Layout(ui, column);
                h.Value = 30;
                Relayout(ui);
                Check("siblings shifted", others[0].Y == 30 && others[4].Y == 110, R(others[0]) + R(others[4]));
                // root, column, wrapper, growing, and the 5 moved siblings with their children (moved = rearranged).
                Check("unmoved subtrees skipped", ui.layout.arrangedCount <= 14, "arranged=" + ui.layout.arrangedCount);
                int moved = ui.layout.arrangedCount;
                h.Value = 30;   // no change
                Relayout(ui);
                Check("no-op relayout", ui.layout.arrangedCount == 0, "arranged=" + ui.layout.arrangedCount);
            }

            // ---- Show inside a boundary; overlay content ----
            {
                var ui = NewContext(1f, 200, 200);
                var on = ui.Signal(false);
                Box host = ui.Div().Size(100, 100).Children(ui.Show(() => on.Value, () => ui.Div().Size(30, 30)));
                Box tail = ui.Div().Size(10, 10);
                Layout(ui, ui.Div().Children(host, tail));
                on.Value = true;
                Relayout(ui);
                Element shown = ((Region)host.children[0]).children[0];
                Check("show inside boundary", shown.Width == 30 && shown.Y == host.Y && tail.Y == 100, R(shown) + R(tail));
                ui.Overlay(() => ui.Div().Absolute().Right(0).Bottom(0).Size(20, 20));
                Relayout(ui);
                Element overlay = ui.overlayRoot.children[0];
                Check("overlay placed on panel", overlay.X == 180 && overlay.Y == 180, R(overlay));
            }


            // ---- input routing (device independent) ----
            {
                var ui = NewContext(1f, 200, 100);
                var log = new System.Collections.Generic.List<string>();
                Box inner = ui.Div().Size(40, 40).Background(Color.red)
                    .OnPointerEnter(e => log.Add("enter inner")).OnPointerLeave(e => log.Add("leave inner"))
                    .OnClick(e => log.Add("click inner"));
                Box outer = ui.Div().Size(100, 100).Hover(st => st.Opacity(0.5f))
                    .OnPointerEnter(e => log.Add("enter outer"))
                    .OnClick(e => log.Add("click outer @" + Mathf.Round(e.LocalX) + "," + Mathf.Round(e.LocalY)))
                    .Children(inner);
                Box other = ui.Div().Size(50, 50).Background(Color.blue).OnClick(e => log.Add("click other"));
                Layout(ui, ui.Div().Row().Children(outer, other));
                PublishPaintOrder(ui);

                Pointer(ui, 10, 10, false);
                Check("enter outer then inner", string.Join("|", log) == "enter outer|enter inner", string.Join("|", log));
                Check(":hover on ancestors", outer.hoverCount == 1 && (outer.pseudoStates & PseudoState.Hover) != 0);
                log.Clear();
                Pointer(ui, 10, 10, true);
                Check(":active while pressed", (inner.pseudoStates & PseudoState.Active) != 0 && (outer.pseudoStates & PseudoState.Active) != 0);
                Pointer(ui, 12, 12, false);
                Check("click bubbles inner -> outer", string.Join("|", log) == "click inner|click outer @12,12", string.Join("|", log));
                Check(":active cleared", inner.pseudoStates == PseudoState.Hover);

                log.Clear();
                Pointer(ui, 50, 50, false);
                Check("leave inner only", string.Join("|", log) == "leave inner", string.Join("|", log));

                log.Clear();
                Pointer(ui, 10, 10, true);           // press on inner
                Pointer(ui, 120, 10, true);          // drag onto other (captured)
                Pointer(ui, 120, 10, false);         // release there: no click anywhere
                Check("no click when released elsewhere", !log.Exists(l => l.StartsWith("click")), string.Join("|", log));

                log.Clear();
                inner.OnClick(e => { log.Add("stop"); e.StopPropagation(); });
                Pointer(ui, 10, 10, false);
                Pointer(ui, 10, 10, true);
                Pointer(ui, 10, 10, false);
                Check("stopPropagation", log.Contains("stop") && !log.Exists(l => l.StartsWith("click outer")), string.Join("|", log));

                log.Clear();
                Pointer(ui, 10, 10, false, over: false);
                Check("leaving the panel clears hover", outer.hoverCount == 0 && inner.hoverCount == 0 && (outer.pseudoStates & PseudoState.Hover) == 0);

                // pointer-events: none lets the pointer through
                Box through = ui.Div().Absolute().Left(0).Top(0).Size(200, 100).Background(Color.green).PointerEvents(PointerEventsMode.None);
                ui.root.children[0].AppendChild(through);
                Relayout(ui);
                PublishPaintOrder(ui);
                log.Clear();
                Pointer(ui, 120, 10, false);
                Pointer(ui, 120, 10, true);
                Pointer(ui, 120, 10, false);
                Check("pointer-events none", log.Contains("click other"), string.Join("|", log));

                // a click handler that writes a signal updates bindings once
                var count = ui.Signal(0);
                int runs = 0;
                ui.Effect(() => { runs++; var c = count.Value; });
                other.OnClick(() => { count.Value++; count.Value++; });
                runs = 0;
                Pointer(ui, 120, 10, true);
                Pointer(ui, 120, 10, false);
                Check("handlers are batched", count.Peek() == 2 && runs == 1, "runs=" + runs);
            }


            // ---- scrolling ----
            {
                var ui = NewContext(1f, 200, 200);
                var scroll = ui.ScrollState();
                var log = new System.Collections.Generic.List<string>();
                Box[] rows = new Box[10];
                ScrollView view = ui.ScrollView(scroll).Size(100, 100).Padding(5);
                for (int i = 0; i < rows.Length; i++)
                {
                    int index = i;
                    rows[i] = ui.Div().Height(30).Background(Color.gray).OnClick(() => log.Add("row " + index));
                    view.Child(rows[i]);
                }
                Box after = ui.Div().Size(10, 10);
                Layout(ui, ui.Div().Children(view, after));
                ui.groups.Resolve();
                PublishPaintOrder(ui);

                Check("scroll content not squeezed", rows[9].Y == 5 + 270 && rows[9].Height == 30 && after.Y == 100, R(rows[9]) + R(after));
                Check("extent reported", scroll.ContentHeight == 310 && scroll.ViewportHeight == 100 && scroll.MaxOffsetY == 210,
                    scroll.ContentHeight + "/" + scroll.ViewportHeight);
                Check("content in its own group", rows[0].group == view.scrollGroup && view.group == TransformGroups.Root);
                Check("content clip left to the group", rows[0].clipTop < -1000);

                // Scrolling touches no layout: only the group offset changes.
                scroll.ScrollTo(95);
                Check("scroll does not invalidate layout", ui.layoutQueue.Count == 0 && !ui.needsStyle && ui.groups.dirty,
                    "queue=" + ui.layoutQueue.Count + " style=" + ui.needsStyle);
                ui.groups.Resolve();
                Check("group offset", ui.groups.offsets[view.scrollGroup].y == -95, "" + ui.groups.offsets[view.scrollGroup].y);
                Check("group clip = padding box", ui.groups.clips[view.scrollGroup] == new Vector4(0, 0, 100, 100), "" + ui.groups.clips[view.scrollGroup]);

                // Hit testing follows the offset: y=10 on screen is content y=105 -> row 3 (5 + 90..120).
                Pointer(ui, 50, 10, false);
                Pointer(ui, 50, 10, true);
                Pointer(ui, 50, 10, false);
                Check("click through scroll offset", log.Count == 1 && log[0] == "row 3", string.Join("|", log));
                // Outside the viewport the scrolled content is not hittable.
                log.Clear();
                Pointer(ui, 50, 150, true);
                Pointer(ui, 50, 150, false);
                Check("clipped content not hittable", log.Count == 0, string.Join("|", log));

                // Wheel.
                Pointer(ui, 50, 50, false, scroll: 40f);
                Check("wheel scrolls", scroll.OffsetY == 135, "" + scroll.OffsetY);
                Pointer(ui, 50, 50, false, scroll: 1000f);
                Check("clamped at the end", scroll.OffsetY == 210, "" + scroll.OffsetY);

                // Drag: past the threshold it scrolls and the press is not a click.
                log.Clear();
                scroll.ScrollTo(0);
                ui.groups.Resolve();
                Pointer(ui, 50, 80, true);
                Pointer(ui, 50, 78, true);           // below the threshold
                Check("small move keeps :active", (rows[2].pseudoStates & PseudoState.Active) != 0);
                Pointer(ui, 50, 40, true);           // 40 px up: scroll down by 40
                Check("drag scrolls", Mathf.Abs(scroll.OffsetY - 40) < 0.01f, "" + scroll.OffsetY);
                Check("drag cancels :active", (rows[2].pseudoStates & PseudoState.Active) == 0);
                Pointer(ui, 50, 40, false);
                Check("drag cancels click", log.Count == 0, string.Join("|", log));

                // ScrollIntoView.
                scroll.ScrollTo(0);
                scroll.ScrollIntoView(rows[8]);
                Check("scroll into view", scroll.OffsetY == 5 + 270 - 100, "" + scroll.OffsetY);

                // Content shrinking clamps the offset.
                for (int i = 4; i < rows.Length; i++) view.RemoveChild(rows[i]);
                Relayout(ui);
                Check("offset clamped after shrink", scroll.MaxOffsetY == 30 && scroll.OffsetY == 30, scroll.MaxOffsetY + "/" + scroll.OffsetY);
            }

            // ---- translate ----
            {
                var ui = NewContext(1f, 200, 200);
                var shift = ui.Signal(0f);
                Box moved = ui.Div().Size(20, 20).Background(Color.red).Translate(() => shift.Value, () => 0f).OnClick(() => { });
                Layout(ui, ui.Div().Children(moved));
                PublishPaintOrder(ui);
                shift.Value = 50f;
                Check("translate is GPU-only", ui.layoutQueue.Count == 0 && ui.groups.dirty && moved.group == moved.translateGroup && moved.group > 0);
                ui.groups.Resolve();
                Pointer(ui, 60, 10, false);
                Check("hit test follows translate", moved.hoverCount == 1);
            }


            // ---- horizontal scroll: content keeps its max-content width; scrollbar thumb ----
            {
                var ui = NewContext(1f, 200, 200);
                var scroll = ui.ScrollState();
                Box strip = ui.Div().Row().Gap(10);
                for (int i = 0; i < 6; i++) strip.Child(ui.Div().Size(40, 20));
                ScrollView view = ui.ScrollView(scroll, ScrollAxis.Horizontal).Width(100).Children(strip);
                Box bar = ui.Scrollbar(scroll, ScrollAxis.Horizontal).Width(100).Height(6);
                Layout(ui, ui.Div().Children(view, bar));
                Relayout(ui);   // the bar reacts to the extent written by layout
                Check("horizontal content not squeezed", strip.Width == 290 && strip.children[5].X == 250, R(strip));
                Check("horizontal extent", scroll.ContentWidth == 290 && scroll.MaxOffsetX == 190 && scroll.MaxOffsetY == 0);
                Element thumb = bar.children[0];
                Check("thumb size from proportion", thumb.Width == 34 && thumb.Height == 6, R(thumb));   // 100 * 100/290
                Check("thumb moves on the GPU", thumb.translateGroup > 0 && thumb.group == thumb.translateGroup);
                scroll.ScrollTo(190, 0);
                ui.groups.Resolve();
                Check("thumb at the end", ui.groups.offsets[thumb.group].x == 66, "" + ui.groups.offsets[thumb.group].x);
            }
            {
                var ui = NewContext(1f, 200, 200);
                var scroll = ui.ScrollState();
                ScrollView view = ui.ScrollView(scroll).Size(100, 100).Children(ui.Div().Height(5000));
                Box bar = ui.Scrollbar(scroll).Height(100);
                Layout(ui, ui.Div().Row().Children(view, bar));
                Relayout(ui);
                Check("min-height on a tiny thumb", bar.children[0].Height == 32, R(bar.children[0]));
            }


            // ---- style resolution only touches what changed ----
            {
                var ui = NewContext(1f, 200, 200);
                Box parent = ui.Div().TextColor(Color.red);
                Box child = ui.Div();
                Box[] others = new Box[20];
                Box list = ui.Div();
                for (int i = 0; i < others.Length; i++) { others[i] = ui.Div().Hover(st => st.Background(Color.blue)); list.Child(others[i]); }
                Layout(ui, ui.Div().Children(parent.Children(child), list));
                Check("initial resolve", child.computed.ColorOf(StyleProperty.TextColor) == Color.red && others[3].computed.Opacity == 1f);
                others[3].SetPseudoState(PseudoState.Hover, true);
                Check("hover queues one element", ui.styleQueue.Count == 1);
                others[5].computed.numbers[StyleProperty.Opacity] = 0.123f;   // canary: must not be recomputed
                Relayout(ui);
                Check("hover resolved", others[3].computed.ColorOf(StyleProperty.Background) == Color.blue);
                Check("siblings untouched", others[5].computed.numbers[StyleProperty.Opacity] == 0.123f);
                others[5].computed.numbers[StyleProperty.Opacity] = 1f;
                parent.TextColor(Color.green);
                Relayout(ui);
                Check("inherited change reaches children", child.computed.ColorOf(StyleProperty.TextColor) == Color.green);
                Box late = ui.Div().Children(ui.Div().Padding(3));
                list.Child(late);
                Relayout(ui);
                Check("attached subtree resolved", late.children[0].computed.numbers[StyleProperty.PaddingTop] == 3f);
            }


            // ---- For (keyed) ----
            {
                var ui = NewContext(1f, 200, 200);
                var list = ui.Signal(new System.Collections.Generic.List<string> { "a", "b", "c" });
                int builds = 0;
                int bindingRuns = 0;
                var tick = ui.Signal(0);
                Region region = ui.For(() => list.Value, x => x, (item, index) =>
                {
                    builds++;
                    ui.Effect(() => { var t = tick.Value; bindingRuns++; });
                    return ui.Div().Height(10);
                });
                Layout(ui, ui.Div().Children(region));
                Element a = region.children[0], c = region.children[2];
                Check("for builds each key once", builds == 3 && region.children.Count == 3);
                list.Value = new System.Collections.Generic.List<string> { "c", "a", "b" };
                Check("for moves, does not rebuild", builds == 3 && region.children[0] == c && region.children[1] == a);
                Relayout(ui);
                Check("for reorder lays out", c.Y == 0 && a.Y == 10, R(c) + R(a));
                list.Value = new System.Collections.Generic.List<string> { "c", "d" };
                Check("for adds and removes", builds == 4 && region.children.Count == 2 && region.children[0] == c && a.parent == null);
                bindingRuns = 0;
                tick.Value = 1;
                Check("removed items' bindings disposed", bindingRuns == 2, "runs=" + bindingRuns);

                var records = ui.Signal(new System.Collections.Generic.List<Vector2Int> { new Vector2Int(1, 10), new Vector2Int(2, 20) });
                var texts = new System.Collections.Generic.List<string>();
                Region region2 = ui.For(() => records.Value, r => r.x, (item, index) =>
                {
                    Box b = ui.Div();
                    ui.Effect(() => texts.Add(item.Value.x + ":" + item.Value.y + "@" + index.Value));
                    return b;
                });
                texts.Clear();
                records.Value = new System.Collections.Generic.List<Vector2Int> { new Vector2Int(2, 21), new Vector2Int(1, 10) };
                Check("same key, new value: updated in place", texts.Contains("2:21@0") && texts.Contains("1:10@1"), string.Join("|", texts));
            }

            // ---- VirtualList ----
            {
                var ui = NewContext(1f, 300, 300);
                var scroll = ui.ScrollState();
                var data = new System.Collections.Generic.List<int>();
                for (int i = 0; i < 10000; i++) data.Add(i);
                int builds = 0;
                var clicked = new System.Collections.Generic.List<int>();
                ScrollView view = ui.VirtualList(scroll, () => data, 20f, (item, index) =>
                {
                    builds++;
                    return ui.Div().FullSize().Background(Color.gray).OnClick(() => clicked.Add(item.Value));
                }, 0f, 2).Size(100, 100);
                Layout(ui, ui.Div().Children(view));
                Relayout(ui);   // metrics -> window -> slots
                Relayout(ui);
                ui.groups.Resolve();
                Box content = (Box)view.children[0];
                int slotCount = content.children.Count;
                Check("virtual: content height", content.Height == 200000 && scroll.MaxOffsetY == 199900, R(content));
                Check("virtual: few slots", slotCount >= 5 && slotCount <= 10, "slots=" + slotCount);
                Check("virtual: builds bounded", builds == slotCount, "builds=" + builds);

                // Scroll far: rows are reassigned to existing slots, positioned on the GPU.
                scroll.ScrollTo(123450);
                int queued = ui.layoutQueue.Count;
                Frames(ui, 5);
                Check("virtual: no new slots when scrolling", content.children.Count == slotCount && builds == slotCount,
                    "slots " + slotCount + " -> " + content.children.Count + ", builds " + builds + ", viewport " + scroll.ViewportHeight);
                Check("virtual: scrolling does not lay out", queued == 0, "queue=" + queued);
                ui.groups.Resolve();
                Element anySlot = content.children[0];
                float slotY = ui.groups.offsets[anySlot.group].y;
                float scrollY = ui.groups.offsets[view.scrollGroup].y;
                Check("virtual: slot offset = row position", Mathf.Abs(slotY - scrollY) % 20f < 0.01f && anySlot.group > 0,
                    "slot " + slotY + " scroll " + scrollY);

                // Click: screen y 10 is content y 123460 -> row 6173.
                Frames(ui, 1);
                PublishPaintOrder(ui);
                Pointer(ui, 50, 10, false);
                Pointer(ui, 50, 10, true);
                Pointer(ui, 50, 10, false);
                Check("virtual: click hits the right row", clicked.Count == 1 && clicked[0] == 6173, string.Join(",", clicked));

                // Data change: rows update in place.
                data[6173] = -1;
                var data2 = new System.Collections.Generic.List<int>(data);
                // (a new list instance tells the list its data changed)
            }


            // ---- VirtualList: reassigned rows lay out only themselves; data updates in place ----
            {
                var ui = NewContext(1f, 300, 300);
                var scroll = ui.ScrollState();
                var data = ui.Signal(new System.Collections.Generic.List<int>());
                var initial = new System.Collections.Generic.List<int>();
                for (int i = 0; i < 1000; i++) initial.Add(i);
                data.Value = initial;
                var widths = new System.Collections.Generic.Dictionary<int, Box>();
                ScrollView view = ui.VirtualList(scroll, () => data.Value, 20f, (item, index) =>
                {
                    Box bar = ui.Div().Height(10).Width(() => (Length)(10 + item.Value % 50));
                    return ui.Div().Children(bar);
                }, 0f, 1).Size(100, 100);
                Box outside = ui.Div().Size(10, 10);
                Layout(ui, ui.Div().Children(view, outside));
                Relayout(ui);
                Relayout(ui);
                scroll.ScrollTo(200);    // 10 rows further: some slots get new rows
                Frames(ui, 1);
                Box content = (Box)view.children[0];
                Check("reassigned rows lay out only themselves", ui.layout.arrangedCount > 0 && ui.layout.arrangedCount <= 3 * 8,   // two frames of budget: the scroll frame and the next
                    "arranged=" + ui.layout.arrangedCount + " slots=" + content.children.Count);
                Check("outside untouched", outside.Y == 100, R(outside));
                Frames(ui, 4);
                Element shownRow = null;
                foreach (var slot in content.children) { var bar = slot.children[0].children[0]; if (bar.Width == 10 + 12 % 50) shownRow = bar; }
                Check("row 12 shows its width", shownRow != null);

                var changed = new System.Collections.Generic.List<int>(initial);
                changed[12] = 49;   // width 10 + 49
                data.Value = changed;
                Relayout(ui);
                Check("data update in place", shownRow != null && shownRow.Width == 59, shownRow == null ? "none" : R(shownRow));
            }


            // ---- VirtualList: scrolling by one row reassigns one slot ----
            {
                var ui = NewContext(1f, 300, 600);
                var scroll = ui.ScrollState();
                var data = new System.Collections.Generic.List<int>();
                for (int i = 0; i < 10000; i++) data.Add(i);
                int itemChanges = 0;
                ScrollView view = ui.VirtualList(scroll, () => data, 36f, (item, index) =>
                {
                    ui.Effect(() => { var v = item.Value; itemChanges++; });
                    return ui.Div().FullSize();
                }, 4f, 3).Size(200, 480);
                Layout(ui, ui.Div().Children(view));
                Relayout(ui);
                Relayout(ui);
                scroll.ScrollTo(4005);
                Relayout(ui);
                Frames(ui, 10);
                itemChanges = 0;
                scroll.ScrollBy(0, 40);   // exactly one row
                Check("one row scrolled -> one slot reassigned", itemChanges == 1, "changes=" + itemChanges);
                itemChanges = 0;
                scroll.ScrollBy(0, 10);   // within a row, no row enters or leaves
                Check("sub-row scroll reassigns nothing", itemChanges == 0, "changes=" + itemChanges);
            }


            // ---- VirtualList: a big jump fills in-view rows first, a few per frame ----
            {
                var ui = NewContext(1f, 300, 600);
                var scroll = ui.ScrollState();
                var data = new System.Collections.Generic.List<int>();
                for (int i = 0; i < 10000; i++) data.Add(i);
                int fills = 0;
                ScrollView view = ui.VirtualList(scroll, () => data, 36f, (item, index) =>
                {
                    ui.Effect(() => { var v = item.Value; fills++; });
                    return ui.Div().FullSize();
                }, 4f, 3, 4).Size(200, 480);
                Layout(ui, ui.Div().Children(view));
                Frames(ui, 10);
                fills = 0;
                scroll.ScrollTo(200000);   // every slot needs a new row
                Check("jump: at most 4 rows filled in the frame", fills <= 4, "fills=" + fills);
                Frames(ui, 1);
                Check("next frame: 4 more", fills <= 8, "fills=" + fills);
                Frames(ui, 10);
                Box content = (Box)view.children[0];
                int visibleShown = 0;
                foreach (var slot in content.children)
                {
                    float y = ui.groups.offsets[slot.group].y - ui.groups.offsets[view.scrollGroup].y;
                    if (y >= 199000) visibleShown++;
                }
                Check("eventually every slot filled", visibleShown == content.children.Count, visibleShown + "/" + content.children.Count);
            }


            // ---- content change that keeps the size lays out only the leaf ----
            {
                var ui = NewContext(1f, 300, 300);
                var label = new FakeText(ui, "Row 1234");
                Box row = ui.Div().Row().AlignItems(Align.Center).Padding(4).Children(label, ui.Div().Size(10, 10));
                Box sibling = ui.Div().Height(20);
                Layout(ui, ui.Div().Children(row, sibling));
                label.Set("Row 1238");   // same length -> same size
                Relayout(ui);
                Check("same-size text: only the leaf arranged", ui.layout.arrangedCount == 1 && label.arranges == 2,
                    "arranged=" + ui.layout.arrangedCount);
                label.Set("Row 12345");  // longer -> climbs
                Relayout(ui);
                Check("resized text: parent relaid", ui.layout.arrangedCount > 1 && row.children[1].X == 4 + 63,
                    "arranged=" + ui.layout.arrangedCount + " " + R(row.children[1]));
                label.Set("Row 12346");  // same size again: fast path works after a climb
                Relayout(ui);
                Check("fast path again after a climb", ui.layout.arrangedCount == 1, "arranged=" + ui.layout.arrangedCount);
            }


            // ---- min-height/min-width: auto ----
            {
                // A column too short for its items: the header keeps its content
                // height, the big item (min-height 0 via overflow hidden) absorbs it all.
                var ui = NewContext(1f, 200, 100);
                var title = new FakeText(ui, "Title");
                Box header = ui.Div().Padding(5).Children(title);           // content height 20
                Box body = ui.Div().Height(500).OverflowHidden();
                Box footer = ui.Div().Height(10);
                Layout(ui, ui.Div().Column().FullSize().Children(header, body, footer));
                Check("header keeps content height", header.Height == 20, R(header));
                // Deficit 430 is shared 500:10 by body and footer (an empty box's
                // automatic minimum is min(specified, content) = 0), as in CSS.
                Check("shrink shares by base size", body.Height == 78 && footer.Height == 2 && footer.Y == 98, R(body) + R(footer));
            }
            {
                // Without overflow hidden the big item cannot shrink below its content either.
                var ui = NewContext(1f, 200, 100);
                Box a = ui.Div().Children(ui.Div().Height(80));
                Box b = ui.Div().Children(ui.Div().Height(80));
                Layout(ui, ui.Div().Column().FullSize().Children(a, b));
                Check("items overflow instead of squashing", a.Height == 80 && b.Height == 80 && b.Y == 80, R(a) + R(b));
                // min-height: 0 opts out, like the CSS idiom.
                var ui2 = NewContext(1f, 200, 100);
                Box c = ui2.Div().MinHeight(0).Children(ui2.Div().Height(80));
                Box d = ui2.Div().MinHeight(0).Children(ui2.Div().Height(80));
                Layout(ui2, ui2.Div().Column().FullSize().Children(c, d));
                Check("min-height 0 shrinks", c.Height == 50 && d.Height == 50, R(c) + R(d));
            }
            {
                // Row: text does not shrink below its longest word; a sized box
                // with no content does (min = min(specified, content)).
                var ui = NewContext(1f, 100, 50);
                var words = new FakeText(ui, "hello wonderful world");  // longest word 9 chars = 63
                Box box = ui.Div().Width(80).Height(10);
                Layout(ui, ui.Div().Row().Width(70).Children(words, box));
                // Deficit 157 by 147:80 would take the text to 45; it stops at 63
                // and the box takes the rest of the deficit.
                Check("text stops at its longest word", words.Width == 63, R(words));
                Check("the rest of the deficit goes to the box", box.Width == 7, R(box));
            }
            {
                // Redistribution: a max-width item gives its unused share to the others.
                var ui = NewContext(1f, 300, 50);
                Box a = ui.Div().FlexGrow(1).MaxWidth(50), b = ui.Div().FlexGrow(1), c = ui.Div().FlexGrow(1);
                Layout(ui, ui.Div().Row().Width(300).Children(a, b, c));
                Check("grow redistributes after max", a.Width == 50 && b.Width == 125 && c.Width == 125, R(a) + R(b) + R(c));
                // Shrink: an item that hits its min passes the rest of the deficit on.
                var ui2 = NewContext(1f, 300, 50);
                Box d = ui2.Div().Width(200).MinWidth(180), e = ui2.Div().Width(200), f = ui2.Div().Width(200);
                Layout(ui2, ui2.Div().Row().Width(300).Children(d, e, f));
                Check("shrink redistributes after min", d.Width == 180 && e.Width == 60 && f.Width == 60, R(d) + R(e) + R(f));
            }
            {
                // fit-content never goes below min-content.
                var ui = NewContext(1f, 100, 100);
                var words = new FakeText(ui, "abcdefghij klm");   // min-content 70, max-content 98
                Box wrapper = ui.Div().Children(words);
                Layout(ui, ui.Div().Width(40).Children(ui.Div().Row().Children(wrapper)));
                Check("fit-content >= min-content", wrapper.Width == 70, R(wrapper));
            }


            // ---- style sheets ----
            {
                var ui = NewContext(1f, 300, 300);
                Color red = Color.red, blue = Color.blue, green = Color.green, gray = Color.gray;
                var sheet = new StyleSheet()
                    .Rule(".card", st => st.Padding(10).Background(gray))
                    .Rule(".card.primary", st => st.Background(blue))          // more specific
                    .Rule(".button", st => st.Background(green))
                    .Rule(".button:hover", st => st.Background(red))
                    .Rule("text", st => st.FontSize(12))
                    .Rule(".card .title", st => st.FontSize(20))
                    .Rule(".list > .item", st => st.Height(15))
                    .Rule(".late", st => st.Background(red))
                    .Rule(".late", st => st.Background(blue))                  // same specificity: later wins
                    .Rule(".a, .b", st => st.MarginTop(3))
                    .Rule(".card:hover .title", st => st.FontSize(99));        // unsupported: ignored with a warning
                ui.AddStyleSheet(sheet);

                var title = new FakeText(ui, "t").Class("title");
                var plainTitle = new FakeText(ui, "t").Class("title");
                Box card = ui.Div().Class("card").Children(title);
                Box primary = ui.Div().Class("card primary");
                Box inline = ui.Div().Class("card").Background(red);            // inline beats the sheet
                Box button = ui.Div().Class("button").Size(50, 20);
                Box late = ui.Div().Class("late");
                Box a = ui.Div().Class("a"), b = ui.Div().Class("b");
                var show = ui.Signal(true);
                Box direct = ui.Div().Class("item");
                Box nested = ui.Div().Children(ui.Div().Class("item"));
                Box list = ui.Div().Class("list").Children(ui.Show(() => show.Value, () => direct), nested);
                Layout(ui, ui.Div().Children(card, plainTitle, primary, inline, button, late, a, b, list));

                Check("sheet: class rule", card.computed.ColorOf(StyleProperty.Background) == gray && card.computed.Number(StyleProperty.PaddingTop) == 10);
                Check("sheet: compound is more specific", primary.computed.ColorOf(StyleProperty.Background) == blue);
                Check("sheet: inline wins", inline.computed.ColorOf(StyleProperty.Background) == red && inline.computed.Number(StyleProperty.PaddingTop) == 10);
                Check("sheet: later rule wins ties", late.computed.ColorOf(StyleProperty.Background) == blue);
                Check("sheet: comma list", a.computed.Number(StyleProperty.MarginTop) == 3 && b.computed.Number(StyleProperty.MarginTop) == 3);
                Check("sheet: descendant beats tag", title.computed.FontSize == 20 && plainTitle.computed.FontSize == 12,
                    title.computed.FontSize + "/" + plainTitle.computed.FontSize);
                Check("sheet: ancestor pseudo rule ignored with a warning", sheet.rules.Count == 11 && log.messages.Exists(m => m.Contains("pseudo-class on an ancestor")),
                    "rules=" + sheet.rules.Count);
                Check("sheet: child combinator looks through regions", direct.Height == 15 && nested.children[0].Height == 0,
                    R(direct) + R(nested.children[0]));

                // :hover switches rules already matched, without matching again.
                PublishPaintOrder(ui);
                Pointer(ui, button.X + 5, button.Y + 5, false);
                Check("sheet: :hover makes it hittable and styles it", button.hoverCount == 1 && button.styleDirty);
                Relayout(ui);
                Check("sheet: :hover applied", button.computed.ColorOf(StyleProperty.Background) == red && !button.matchDirty);
                Pointer(ui, 290, 290, false);
                Relayout(ui);
                Check("sheet: hover off", button.computed.ColorOf(StyleProperty.Background) == green);

                // Dynamic classes: the element, and descendants when the class is an ancestor class.
                var selected = ui.Signal(false);
                Box toggled = ui.Div().Class("primary", () => selected.Value).Class("card");
                var innerTitle = new FakeText(ui, "x").Class("title");
                Box host = ui.Div().Children(innerTitle);
                host.Class(() => selected.Value ? "card" : "plain");
                ui.root.children[0].AppendChild(toggled);
                ui.root.children[0].AppendChild(host);
                Relayout(ui);
                Check("dynamic class off", toggled.computed.ColorOf(StyleProperty.Background) == gray && innerTitle.computed.FontSize == 12);
                selected.Value = true;
                Relayout(ui);
                Check("dynamic class on", toggled.computed.ColorOf(StyleProperty.Background) == blue);
                Check("ancestor class change restyles descendants", innerTitle.computed.FontSize == 20, "" + innerTitle.computed.FontSize);

                // Disabled: :disabled rules, handlers skipped.
                int clicks = 0;
                var off = ui.Signal(true);
                sheet.Rule(".button:disabled", st => st.Opacity(0.5f));
                ui.styleSheets.Clear();
                ui.AddStyleSheet(sheet);
                Box disabledButton = ui.Div().Class("button").Size(30, 30).Disabled(() => off.Value).OnClick(() => clicks++);
                ui.root.children[0].AppendChild(disabledButton);
                Relayout(ui);
                PublishPaintOrder(ui);
                Check("disabled style", disabledButton.computed.Opacity == 0.5f);
                Pointer(ui, disabledButton.X + 5, disabledButton.Y + 5, true);
                Pointer(ui, disabledButton.X + 5, disabledButton.Y + 5, false);
                off.Value = false;
                Pointer(ui, disabledButton.X + 5, disabledButton.Y + 5, true);
                Pointer(ui, disabledButton.X + 5, disabledButton.Y + 5, false);
                Check("disabled skips handlers", clicks == 1, "clicks=" + clicks);
            }


            // ---- the demo case: a fixed-height box with only absolute children ----
            {
                // In a too-short column it is shrunk (its automatic minimum is 0), as in CSS...
                var ui = NewContext(1f, 100, 100);
                Box stack = ui.Div().Height(90).Children(ui.Div().Absolute().Size(80, 80));
                Box tall = ui.Div().Children(ui.Div().Height(60));
                Layout(ui, ui.Div().Column().FullSize().Children(tall, stack));
                Check("column: fixed box with absolute content shrinks", stack.Height == 40 && tall.Height == 60, R(stack) + R(tall));
                // ...and keeps its height when the page scrolls instead.
                var ui2 = NewContext(1f, 100, 100);
                Box stack2 = ui2.Div().Height(90).Children(ui2.Div().Absolute().Size(80, 80));
                Box tall2 = ui2.Div().Children(ui2.Div().Height(60));
                var scroll = ui2.ScrollState();
                Layout(ui2, ui2.ScrollArea(scroll, ui2.Div().Column().Children(tall2, stack2)).FullSize());
                Check("scrolling page: nothing shrinks", stack2.Height == 90 && tall2.Height == 60 && scroll.MaxOffsetY == 50,
                    R(stack2) + " max " + scroll.MaxOffsetY);
            }


            // ---- drag and drop ----
            {
                var ui = NewContext(1f, 300, 200);
                var log = new System.Collections.Generic.List<string>();
                Box card = ui.Div().Size(40, 40).Background(Color.red)
                    .Draggable(() => 7)
                    .OnClick(() => log.Add("click"))
                    .OnDragEnd(dropped => log.Add("end " + dropped));
                Box source = ui.Div().Size(100, 100).Children(card);
                Box target = ui.Div().Size(100, 100).Background(Color.gray)
                    .DropTarget((int id, PointerEvent e) => log.Add("drop " + id),
                        (int id, PointerEvent e) => log.Add("enter " + id),
                        (int id, PointerEvent e) => log.Add("leave " + id));
                Box stringsOnly = ui.Div().Size(100, 100).Background(Color.blue)
                    .DropTarget((string text, PointerEvent e) => log.Add("wrong drop"));
                Layout(ui, ui.Div().Row().Children(source, target, stringsOnly));
                PublishPaintOrder(ui);

                Pointer(ui, 20, 20, false);
                Pointer(ui, 20, 20, true);
                Pointer(ui, 23, 20, true);                     // below the threshold: still a press
                Check("drag: below threshold is a press", (card.pseudoStates & PseudoState.Dragging) == 0 && (card.pseudoStates & PseudoState.Active) != 0);
                Pointer(ui, 40, 20, true);                     // past it: dragging
                Check("drag: started", (card.pseudoStates & PseudoState.Dragging) != 0 && (card.pseudoStates & PseudoState.Active) == 0);
                Check("drag: the element moves as a group", card.translateGroup > 0 && ui.layoutQueue.Count > 0);
                Relayout(ui);
                ui.groups.Resolve();
                Check("drag: offset follows the pointer", ui.groups.offsets[card.group].x == 20, "" + ui.groups.offsets[card.group].x);
                PublishPaintOrder(ui);
                Pointer(ui, 150, 50, true);                    // over the target
                Check("drag: enter + :drag-over", log.Contains("enter 7") && (target.pseudoStates & PseudoState.DragOver) != 0, string.Join("|", log));
                Pointer(ui, 250, 50, true);                    // over a target for strings only
                Check("drag: leave; wrong type is not a target", log.Contains("leave 7") && (stringsOnly.pseudoStates & PseudoState.DragOver) == 0);
                Pointer(ui, 150, 60, true);
                Pointer(ui, 150, 60, false);                   // release over the target
                Check("drag: drop, no click", log.Contains("drop 7") && !log.Contains("click") && log.Contains("end True"), string.Join("|", log));
                Check("drag: cleaned up", card.translateGroup == 0 && (card.pseudoStates & (PseudoState.Dragging | PseudoState.DragOver)) == 0
                    && (target.pseudoStates & PseudoState.DragOver) == 0);

                log.Clear();
                Relayout(ui);
                PublishPaintOrder(ui);
                Pointer(ui, 20, 20, true);
                Pointer(ui, 60, 150, true);                    // nowhere
                Pointer(ui, 60, 150, false);
                Check("drag: released elsewhere cancels", log.Count == 1 && log[0] == "end False", string.Join("|", log));
            }
            {
                // A preview on the overlay layer; a draggable inside a scroll view wins over scrolling.
                var ui = NewContext(1f, 300, 200);
                var scroll = ui.ScrollState();
                Box item = ui.Div().Height(30).Background(Color.gray).Draggable(() => "item")
                    .DragPreview(() => ui.Div().Background(Color.white));
                ScrollView view = ui.ScrollView(scroll).Size(100, 60).Children(item, ui.Div().Height(300));
                Layout(ui, ui.Div().Children(view));
                ui.groups.Resolve();
                PublishPaintOrder(ui);
                Pointer(ui, 20, 10, true);
                Pointer(ui, 20, 40, true);                     // 30px down: a drag, not a scroll
                Check("drag in scroll view: no scroll", scroll.OffsetY == 0, "" + scroll.OffsetY);
                Check("drag preview on the overlay", ui.overlayRoot.children.Count == 1 && ui.overlayRoot.children[0].translateGroup > 0);
                Relayout(ui);
                Element preview = ui.overlayRoot.children[0];
                Check("preview placed over the source", preview.X == item.X && preview.Y == item.Y && preview.Width == item.Width, R(preview) + R(item));
                Pointer(ui, 20, 40, false);
                Check("preview removed", ui.overlayRoot.children.Count == 0 && item.translateGroup == 0);
            }
            {
                // Insertion index among children (through a For region).
                var ui = NewContext(1f, 300, 300);
                var ids = ui.Signal(new System.Collections.Generic.List<int> { 1, 2, 3 });
                int index = -1;
                Box list = ui.Div().Column().Children(ui.For(() => ids.Value, i => i, (id, idx) => ui.Div().Height(20)));
                Box column = ui.Div().Size(100, 200).Background(Color.gray)
                    .DropTarget((int id, PointerEvent e) => index = e.InsertionIndexIn(list))
                    .Children(list);
                Box chip = ui.Div().Size(20, 20).Background(Color.red).Draggable(() => 9);
                Layout(ui, ui.Div().Row().Children(column, chip));
                PublishPaintOrder(ui);
                Pointer(ui, 110, 10, true);
                Pointer(ui, 50, 35, true);                     // between the 2nd (20..40, middle 30) and 3rd
                Pointer(ui, 50, 35, false);
                Check("insertion index", index == 2, "index=" + index);
            }


            // ---- font-family ----
            {
                var ui = NewContext(1f, 100, 100);
                var inner = new FakeText(ui, "x");
                Box styled = ui.Div().FontFamily("Unknown, 'inter'").Children(inner);
                Box plain = ui.Div();
                Layout(ui, ui.Div().Children(styled, plain));
                Check("font-family: first registered candidate, case-insensitive, quotes", styled.computed.FontIndex == 1);
                Check("font-family is inherited", inner.computed.FontIndex == 1 && plain.computed.FontIndex == 0);
                Box unknown = ui.Div().FontFamily("Comic Sans");
                ui.root.children[0].AppendChild(unknown);
                Relayout(ui);
                Check("unknown family falls back with a warning", unknown.computed.FontIndex == 0
                    && log.messages.Exists(m => m.Contains("Comic Sans")));
                styled.FontFamily("Noto Sans JP");
                Relayout(ui);
                Check("font change relays out the text", inner.computed.FontIndex == 0 && ui.layout.arrangedCount > 0);
            }


            // ---- text boxes: size, default styles ----
            {
                var ui = NewContext(1f, 400, 300);
                var name = ui.Signal("");
                TextBox box = ui.TextBox(name).Placeholder("Name");
                Box row = ui.Div().Flex().Row().Children(box);
                Layout(ui, ui.Div().Children(row));
                // content 160x24 (20 characters, one line) + padding 6/10 + border 1
                Check("text box: intrinsic size of about 20 characters", box.Width == 182 && box.Height == 38, R(box));
                name.Value = "a very long value that is much wider than the box could ever be";
                Relayout(ui);
                Check("text box: size does not follow the text", box.Width == 182 && box.Height == 38, R(box));
                Check("text box: default look from the built-in sheet", box.computed.numbers[StyleProperty.BorderTopWidth] == 1f
                    && box.computed.ColorOf(StyleProperty.Background) == Colors.White);

                var notes = ui.Signal("");
                TextBox area = ui.TextArea(notes).Rows(4);
                var sheet = new StyleSheet().Rule(".field", s => s.Background(Colors.Hex(0x222222)).Padding(0).Border(0, Colors.Black));
                ui.AddStyleSheet(sheet);
                area.Class("field");
                ui.root.children[0].AppendChild(area);
                Relayout(ui);
                Check("text area: fills a block container, Rows(4) high", area.Width == 400 && area.Height == 93, R(area));
                Check("text box: a class rule overrides the built-in sheet", area.computed.ColorOf(StyleProperty.Background) == Colors.Hex(0x222222));
                Check("text box: tags input / textarea", box.tag == "input" && area.tag == "textarea");
            }

            // ---- text boxes: what the text input host is offered, and what it reports ----
            {
                var ui = NewContext(1f, 400, 300);
                var host = new RecordingTextInputHost();
                ui.SetTextInputHost(host);
                var name = ui.Signal("");
                var off = ui.Signal(false);
                string committedValue = null;
                TextBox box = ui.TextBox(name).MaxLength(5).OnCommit(v => committedValue = v).Disabled(() => off.Value)
                    .Absolute().Left(20).Top(30).Width(100).Height(30);
                Box other = ui.Div().Absolute().Left(200).Top(200).Size(50, 50).Background(Colors.Black);
                Layout(ui, ui.Div().FullSize().Children(box, other));
                PublishPaintOrder(ui);

                Pointer(ui, 300, 20, false);
                ui.textInput.Update();
                Check("text input: nothing offered while nothing is hovered", host.offered.Count == 0);
                Pointer(ui, 50, 40, false);
                ui.textInput.Update();
                Check("text input: the hovered box is offered with its rectangle", host.offered.Count == 1
                    && host.offered[0].Left == 20 && host.offered[0].Top == 30 && host.offered[0].Width == 100 && host.offered[0].Height == 30,
                    host.offered.Count == 1 ? $"{host.offered[0].Left},{host.offered[0].Top} {host.offered[0].Width}x{host.offered[0].Height}" : "");
                Pointer(ui, 50, 40, true);
                Pointer(ui, 50, 40, false);
                Pointer(ui, 300, 20, false);
                ui.textInput.Update();
                Check("text input: the last pressed box stays offered", host.offered.Count == 1);
                Pointer(ui, 220, 220, true);
                Pointer(ui, 220, 220, false);
                ui.textInput.Update();
                Check("text input: a press elsewhere withdraws it", host.offered.Count == 0);

                Pointer(ui, 50, 40, false);
                ui.textInput.Update();
                TextInputTarget target = host.offered[0];
                target.ReportFocus(true, false);
                Relayout(ui);
                Check("text input: editing sets :focus", (box.pseudoStates & PseudoState.Focus) != 0 && box.IsEditing);
                target.ReportText("hello world");
                Check("text input: typed text reaches the signal, cut to MaxLength", name.Peek() == "hello");
                Pointer(ui, 220, 220, true);
                Pointer(ui, 220, 220, false);
                ui.textInput.Update();
                Check("text input: a box being edited stays offered", host.offered.Count == 1);
                target.ReportFocus(false, false);
                Check("text input: finishing commits the value", committedValue == "hello" && (box.pseudoStates & PseudoState.Focus) == 0);
                committedValue = null;
                target.ReportFocus(true, false);
                target.ReportFocus(false, true);
                Check("text input: cancelling does not commit", committedValue == null);
                name.Value = "set by code";
                Check("text input: the host sees the program's value", target.Text == "set by code");

                off.Value = true;
                Relayout(ui);
                Pointer(ui, 60, 45, false);
                ui.textInput.Update();
                Check("text input: a disabled box is not offered", host.offered.Count == 0);

                var lines = ui.Signal("");
                TextBox single = ui.TextBox(lines);
                single.target.ReportText("one\ntwo");
                Check("text input: a one-line box turns newlines into spaces", lines.Peek() == "one two");
            }

            // ---- fit-content ----
            {
                var ui = NewContext(1f, 400, 300);
                Element fixedChild = new FakeText(ui, "abcdefghij");   // 70 wide
                Box fit = ui.Div().Width(Length.FitContent).Padding(5).Children(fixedChild);
                Box column = ui.Div().Flex().Column().Children(ui.Div().Width(Length.FitContent).Children(new FakeText(ui, "abc")));
                Layout(ui, ui.Div().Children(fit, column));
                Check("fit-content: a block child shrinks to its content", fit.Width == 80, R(fit));
                Check("fit-content: a stretching flex item does not stretch", column.children[0].Width == 21, R(column.children[0]));
            }

            // ---- components ----
            {
                var ui = NewContext(1f, 600, 800);
                var clicks = ui.Signal(0);
                var agreed = ui.Signal(false);
                var on = ui.Signal(false);
                var size = ui.Signal(1);
                var volume = ui.Signal(0f);
                var steps = ui.Signal(0f);
                var tab = ui.Signal(0);
                var fruit = ui.Signal("Apple");
                var dialogOpen = ui.Signal(false);

                Box button = ui.Button("Save", ButtonVariant.Primary).OnClick(() => clicks.Value++);
                Box checkbox = ui.Checkbox(agreed, "Agree");
                Box toggle = ui.Switch(on, "On");
                Box small = ui.Radio(size, 0, "Small");
                Box large = ui.Radio(size, 2, "Large");
                Box slider = ui.Slider(volume, 0f, 10f).Width(200);
                Box stepped = ui.Slider(steps, 0f, 10f, 2f).Width(200);
                Box tabs = ui.Tabs(tab, "One", "Two");
                Box select = ui.Select(fruit, "Apple", "Banana", "Cherry").Width(150);
                Region dialog = ui.Dialog(dialogOpen, () => ui.Div().Size(100, 50));
                Box page = ui.Div().Flex().Column().AlignItems(Align.Start).Gap(10).Padding(10)
                    .Children(button, checkbox, toggle, small, large, slider, stepped, tabs, select, dialog);
                Layout(ui, ui.Div().FullSize().Children(page));
                ui.groups.Resolve();
                PublishPaintOrder(ui);

                Check("button: tag, variant class, default look", button.tag == "button" && button.HasClass("primary")
                    && button.computed.ColorOf(StyleProperty.Background) == Theme.Light.Accent);
                Box inBlock = ui.Button("Wide?");
                ui.root.children[0].AppendChild(inBlock);
                Refresh(ui);
                Check("button: fit-content even in a block container", inBlock.Width < 200, R(inBlock));
                Click(ui, CenterX(ui, button), CenterY(ui, button));
                Check("button: click", clicks.Peek() == 1);

                Click(ui, CenterX(ui, checkbox), CenterY(ui, checkbox));
                Settle(ui);
                Check("checkbox: click toggles, class and look follow", agreed.Peek()
                    && checkbox.children[0].HasClass("checked")
                    && checkbox.children[0].computed.ColorOf(StyleProperty.Background) == Theme.Light.Accent);
                Click(ui, CenterX(ui, checkbox), CenterY(ui, checkbox));
                Check("checkbox: second click unchecks", !agreed.Peek());

                Click(ui, CenterX(ui, toggle), CenterY(ui, toggle));
                Element thumb = toggle.children[0].children[0];
                int thumbBefore = thumb.X;
                Frames(ui, 30);
                Check("switch: toggles and the thumb slides over", on.Peek() && thumb.X == thumbBefore + 16,
                    $"{thumbBefore} -> {thumb.X}");

                Check("radio: initial selection", !small.children[0].HasClass("checked") && !large.children[0].HasClass("checked"));
                Click(ui, CenterX(ui, large), CenterY(ui, large));
                Check("radio: click selects its value", size.Peek() == 2 && large.children[0].HasClass("checked"));
                Click(ui, CenterX(ui, small), CenterY(ui, small));
                Check("radio: the group moves", size.Peek() == 0 && small.children[0].HasClass("checked")
                    && !large.children[0].HasClass("checked"));

                Pointer(ui, slider.X + 50, CenterY(ui, slider), true);
                Check("slider: press sets the value", Mathf.Abs(volume.Peek() - 2.5f) < 0.01f, volume.Peek().ToString());
                Pointer(ui, slider.X + 150, CenterY(ui, slider), true);
                Check("slider: dragging follows", Mathf.Abs(volume.Peek() - 7.5f) < 0.01f, volume.Peek().ToString());
                Pointer(ui, slider.X + 400, CenterY(ui, slider), true);
                Check("slider: clamped past the end", volume.Peek() == 10f);
                Pointer(ui, slider.X + 400, CenterY(ui, slider), false);
                Refresh(ui);
                Click(ui, stepped.X + 58, CenterY(ui, stepped));
                Check("slider: steps", steps.Peek() == 2f, steps.Peek().ToString());

                Click(ui, CenterX(ui, tabs.children[1]), CenterY(ui, tabs.children[1]));
                Check("tabs: click selects", tab.Peek() == 1 && tabs.children[1].HasClass("selected")
                    && !tabs.children[0].HasClass("selected"));

                Click(ui, CenterX(ui, select), CenterY(ui, select));
                Check("select: click opens the menu on the overlay", select.HasClass("open") && ui.overlayRoot.children.Count == 1);
                Settle(ui);
                Element layer = ui.overlayRoot.children[0];
                Element menu = layer.children[0];
                Check("select: menu under the select, as wide", menu.X == select.X && menu.Y >= select.Y + select.Height
                    && menu.Width == select.Width, R(menu) + " vs " + R(select));
                Element banana = menu.children[1];
                Click(ui, CenterX(ui, banana), CenterY(ui, banana));
                Check("select: the menu fades out", ui.overlayRoot.children.Count == 1 && !select.HasClass("open"));
                Settle(ui);
                Check("select: choosing sets the value and closes", fruit.Peek() == "Banana" && ui.overlayRoot.children.Count == 0
                    && !select.HasClass("open"));
                Click(ui, CenterX(ui, select), CenterY(ui, select));
                Settle(ui);
                Click(ui, 590, 790);
                Settle(ui);
                Check("select: a press elsewhere closes", ui.overlayRoot.children.Count == 0 && fruit.Peek() == "Banana");

                dialogOpen.Value = true;
                Refresh(ui);
                Settle(ui);
                Element backdrop = ui.overlayRoot.children.Count == 1 ? ui.overlayRoot.children[0] : null;
                Check("dialog: opens on the overlay, centered", backdrop != null && backdrop.Width == 600
                    && backdrop.children[0].X == (600 - backdrop.children[0].Width) / 2, backdrop != null ? R(backdrop.children[0]) : "");
                Click(ui, 300, 400);
                Check("dialog: a click inside keeps it open", dialogOpen.Peek());
                Click(ui, 10, 10);
                Settle(ui);
                Check("dialog: a click on the backdrop closes it", !dialogOpen.Peek() && ui.overlayRoot.children.Count == 0);

                ui.Toast("Hello", 1f);
                Refresh(ui);
                Element stack = ui.overlayRoot.children.Count > 0 ? ui.overlayRoot.children[ui.overlayRoot.children.Count - 1] : null;
                Check("toast: shown", stack != null && stack.tag == "toast-stack" && stack.children[0].children.Count == 1);
                Frames(ui, 70);
                Check("toast: fading out after its time", stack.children[0].children.Count == 1);
                Frames(ui, 30);
                Check("toast: gone after its time", stack.children[0].children.Count == 0);

                ui.SetTheme(Theme.Dark);
                Refresh(ui);
                Settle(ui);
                Check("theme: existing components restyle", button.computed.ColorOf(StyleProperty.Background) == Theme.Dark.Accent
                    && inBlock.computed.ColorOf(StyleProperty.Background) == Theme.Dark.Surface);
                var themeSeen = Theme.Light.Background;
                ui.Effect(() => themeSeen = ui.Theme.Background);
                ui.SetTheme(Theme.Light);
                Check("theme: bindings follow SetTheme", themeSeen == Theme.Light.Background);

                Box helped = ui.Div().Size(40, 20).Background(Colors.Black).Tooltip("Help");
                ui.root.children[0].AppendChild(helped);
                Refresh(ui);
                int overlaysBefore = ui.overlayRoot.children.Count;
                Pointer(ui, CenterX(ui, helped), CenterY(ui, helped), false);
                Frames(ui, 10);
                Check("tooltip: not yet", ui.overlayRoot.children.Count == overlaysBefore);
                Frames(ui, 30);
                Check("tooltip: shown after resting", ui.overlayRoot.children.Count == overlaysBefore + 1);
                Pointer(ui, 590, 10, false);
                Settle(ui);
                Check("tooltip: hidden when the pointer leaves", ui.overlayRoot.children.Count == overlaysBefore);
            }

            // ---- pointer positions inside scrolled content ----
            {
                var ui = NewContext(1f, 300, 200);
                var state = ui.ScrollState();
                var value = ui.Signal(0f);
                Box slider = ui.Slider(value, 0f, 100f).Width(200);
                ScrollView view = ui.ScrollView(state, ScrollAxis.Horizontal).Size(300, 200).Flex().Row().AlignItems(Align.Start);
                view.Children(ui.Div().Width(100).FlexShrink(0), slider.FlexShrink(0), ui.Div().Width(400).FlexShrink(0));
                Layout(ui, view);
                state.ScrollTo(80f, 0f);
                Refresh(ui);
                float screenX = slider.X + ui.groups.offsets[slider.group].x;
                Click(ui, screenX + 60, CenterY(ui, slider));
                Check("scrolled content: local pointer positions include the scroll", Mathf.Abs(value.Peek() - 30f) < 0.5f
                    && screenX < slider.X, $"value {value.Peek()} at x {screenX} (layout x {slider.X})");
            }

            // ---- live resize ----
            {
                var ui = NewContext(1f, 400, 300);
                Box full = ui.Div().FullWidth().Height(50);
                Box padded = ui.Div().Padding(10).Children(new FakeText(ui, "abc"));
                Box moved = ui.Div().Size(20, 20).Translate(5f, 0f);
                var state = ui.ScrollState();
                ScrollView view = ui.ScrollView(state).Size(100, 50);
                view.Children(ui.Div().Height(300));
                Layout(ui, ui.Div().Children(full, padded, moved, view));
                state.ScrollTo(0f, 40f);
                Relayout(ui);
                ui.groups.Resolve();
                Check("resize: before", full.Width == 400 && padded.Height == 30 && view.Width == 100);

                ui.Resize(600, 300, 1f);
                Relayout(ui);
                Check("resize: layout follows the new width", full.Width == 600, R(full));

                ui.Resize(600, 300, 2f);
                Relayout(ui);
                ui.groups.Resolve();
                Check("resize: the scale snaps lengths again", full.Width == 600 && full.Height == 100
                    && padded.Height == 40 + 10 && view.Width == 200, R(full) + " " + R(padded) + " " + R(view));
                Check("resize: translation follows the scale", ui.groups.offsets[moved.group].x == 10f,
                    ui.groups.offsets[moved.group].x.ToString());
                // The content is laid out from the view's corner: its group
                // adds the view's position to the scroll offset.
                Check("resize: scroll offset follows the scale", ui.groups.offsets[view.children[0].group].y == view.Y - 80f,
                    ui.groups.offsets[view.children[0].group].y + " view " + view.Y);
                Check("resize: logical size", ui.LogicalWidth == 300f && ui.LogicalHeight == 150f);
            }

            // ---- scroll area: a bar beside the content, only when needed ----
            {
                var ui = NewContext(1f, 300, 200);
                var scroll = ui.ScrollState();
                Box content = ui.Div().Height(100);
                Box area = ui.ScrollArea(scroll, content).FullSize();
                Layout(ui, area);
                Relayout(ui);
                Element bar = area.children[1];
                Check("scroll area: no bar while it all fits", bar.computed.Display == DisplayMode.None && content.Width == 300, R(content));
                content.Height(1000);
                Relayout(ui);
                Relayout(ui);
                Check("scroll area: the bar takes its own room", bar.computed.Display != DisplayMode.None
                    && bar.Width == 12 && bar.X == 300 - 2 - 12 && content.Width == 300 - 16 && bar.Height == 196,
                    R(bar) + " " + R(content));
                Check("scroll area: the bar uses the theme", bar.children[0].computed.ColorOf(StyleProperty.Background) == Theme.Light.BorderHover);
            }

            AnimationTests();
            TransitionTests();
            CutoffTests();
            LayerTests();
            MaterialTests();
            LoadingTests();
            PaintTests();
            ImageTests();
            WebImageTests();

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            return failures;
        }
    }
}
