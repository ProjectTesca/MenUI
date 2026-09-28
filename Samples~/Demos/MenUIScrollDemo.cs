using System.Collections.Generic;
using MenUI;
using UnityEngine;

// Lists and scrolling:
//   - 10,000 rows in a VirtualList (only the rows in view exist), with a
//     separate Scrollbar bound to the same ScrollState;
//   - a horizontal strip of chips rendered by For (keyed), with buttons that
//     add, remove and rotate chips.
// Drag the scrollbar thumb, press its track to page, or drag the content
// (hold click and look up/down). The wheel works in ClientSim; in VRChat it
// turns the view.
public class MenUIScrollDemo : MenUIApp
{
    private const int RowCount = 10000;
    private const float RowHeight = 36f;
    private const float RowGap = 4f;

    protected override Element Build()
    {
        Color surface = Colors.Hex(0x0F172A);
        Color raised = Colors.Hex(0x1E293B);
        Color hover = Colors.Hex(0x334155);
        Color text = Colors.Hex(0xE2E8F0);
        Color accent = Colors.Hex(0x3B82F6);

        SetTheme(Theme.Dark);   // the scrollbars take the theme's colors
        ScrollState list = ScrollState();
        ScrollState chips = ScrollState();
        Signal<int> selected = Signal(-1);

        List<int> rows = new List<int>();
        for (int i = 0; i < RowCount; i++)
        {
            rows.Add(i);
        }

        int nextChip = 8;
        List<int> initialChips = new List<int>();
        for (int i = 0; i < nextChip; i++)
        {
            initialChips.Add(i);
        }
        Signal<List<int>> chipIds = Signal(initialChips);

        return Div().Flex().Column().Gap(12).Padding(20).FullSize().Background(surface).TextColor(text).Children(
            // (min-height: auto keeps these rows from being squashed.)
            Div().Row().Gap(8).AlignItems(Align.Center).Children(
                Text(() => selected.Value < 0 ? "Click a row" : $"Selected row {selected.Value}").FlexGrow(1),
                Button("Top", raised, hover, () => list.ScrollTo(0)),
                Button("Row 5000", raised, hover, () => list.ScrollTo(5000 * (RowHeight + RowGap))),
                Button("Bottom", raised, hover, () => list.ScrollTo(list.MaxOffsetY))),

            // The virtual list and its scrollbar, composed by hand. CSS `flex: 1`
            // (grow 1, basis 0) plus `min-height: 0`: its content is far taller
            // than the panel and must not count as its minimum.
            Div().Flex().Row().FlexGrow(1).FlexBasis(0).MinHeight(0).BorderRadius(8).Border(1, raised).Children(
                VirtualList(list, () => rows, RowHeight, (item, index) =>
                    Div().Row().AlignItems(Align.Center).FullSize().PaddingHorizontal(12).BorderRadius(6)
                        .Background(() => selected.Value == item.Value ? accent : raised)
                        .Hover(s => s.Background(hover))
                        .OnClick(() => selected.Value = item.Value)
                        .Children(Text(() => "Row " + item.Value)),
                    RowGap).FlexGrow(1).MinWidth(0),
                Scrollbar(list).Margin(3).FlexShrink(0)),

            // For: chips keep their elements as the list changes.
            Div().Row().Gap(8).Children(
                Button("Add chip", raised, hover, () =>
                {
                    List<int> next = new List<int>(chipIds.Peek());
                    next.Add(nextChip++);
                    chipIds.Value = next;
                }),
                Button("Remove first", raised, hover, () =>
                {
                    List<int> next = new List<int>(chipIds.Peek());
                    if (next.Count > 0) next.RemoveAt(0);
                    chipIds.Value = next;
                }),
                Button("Rotate", raised, hover, () =>
                {
                    List<int> next = new List<int>(chipIds.Peek());
                    if (next.Count > 1)
                    {
                        int last = next[next.Count - 1];
                        next.RemoveAt(next.Count - 1);
                        next.Insert(0, last);
                    }
                    chipIds.Value = next;
                })),
            // A scroll view's automatic minimum is 0 (CSS), so keep it from shrinking.
            ScrollView(chips, ScrollAxis.Horizontal).FlexShrink(0).Children(
                Div().Row().Gap(8).Padding(4).Children(
                    For(() => chipIds.Value, id => id, (id, index) =>
                        Div().Padding(6, 14).BorderRadius(Length.Percent(50)).Background(raised)
                            .Hover(s => s.Background(hover))
                            .Children(Text(() => $"chip {id.Value} (#{index.Value})").FontSize(14))))),
            Scrollbar(chips, ScrollAxis.Horizontal).FlexShrink(0));
    }

    private Box Button(string label, Color background, Color hover, System.Action onClick)
    {
        return Div().Center().Padding(6, 12).BorderRadius(6).Background(background)
            .Hover(s => s.Background(hover)).OnClick(onClick).Children(Text(label).FontSize(14));
    }
}
