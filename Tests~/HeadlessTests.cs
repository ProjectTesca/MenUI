using System;
using MenUI;
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
                Box fixedBox = ui.Div().Row().Size(100, 50).Children(inner, innerSibling);
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
                Check("min-height on a tiny thumb", bar.children[0].Height == 24, R(bar.children[0]));
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

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            return failures;
        }
    }
}
