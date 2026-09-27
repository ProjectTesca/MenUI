using MenUI;
using UnityEngine;

// A tour of the CSS features MenUI implements so far. Look at a button and
// click (desktop); everything that depends on the counter updates. Cards and
// buttons are styled by a style sheet (classes, :hover, :active, :disabled).
public class MenUIDemo : MenUIApp
{
    private Signal<int> count;

    protected override Element Build()
    {
        count = Signal(0);
        Computed<bool> many = Computed(() => count.Value >= 5);

        Color surface = Colors.Hex(0x1E293B);
        Color raised = Colors.Hex(0x334155);
        Color text = Colors.Hex(0xE2E8F0);
        Color accent = Colors.Hex(0x3B82F6);
        Color warning = Colors.Hex(0xF59E0B);

        AddStyleSheet(new StyleSheet()
            .Rule(".card", s => s.Padding(16).BorderRadius(12).Background(raised).BoxShadow(Shadow.Medium))
            .Rule(".card.interactive:hover", s => s.Background(Colors.Hex(0x475569)).BoxShadow(Shadow.Large))
            .Rule(".card.selected", s => s.Border(2, accent))
            .Rule(".button", s => s.Center().Padding(8, 16).MinWidth(64).BorderRadius(8).Background(raised))
            .Rule(".button:hover", s => s.Background(Colors.Hex(0x475569)))
            .Rule(".button:active", s => s.Background(Colors.Hex(0x1E293B)))
            .Rule(".button.primary", s => s.Background(accent))
            .Rule(".button.primary:hover", s => s.Background(Colors.Hex(0x60A5FA)))
            .Rule(".button:disabled", s => s.Opacity(0.4f))
            .Rule(".card .caption", s => s.FontSize(13).TextColor(Colors.Hex(0x94A3B8))));
        Signal<int> selectedCard = Signal(-1);

        // The page scrolls: it is taller than the panel. (In a column of fixed
        // height, items with a fixed height and no in-flow content — or with
        // overflow: hidden — would be shrunk to fit, exactly as in CSS.)
        Box page = Div().Flex().Column().Gap(16).Padding(24)
            .Children(
                // Header row: title on the left, badge on the right.
                Div().Row().JustifyContent(Justify.SpaceBetween).AlignItems(Align.Center).Children(
                    Text("MenUI — CSS tour").FontSize(28),
                    Div().Padding(4, 12).BorderRadius(Length.Percent(50))
                        .Background(() => many.Value ? warning : accent)
                        .Children(Text(() => $"count {count.Value}").FontSize(14))),

                // Three cards sharing the width 1:2:1 (flex-grow), with gap.
                // Click one: the `selected` class follows a signal.
                Div().Row().Gap(12).Children(
                    Card(0, 1, "flex-grow: 1", selectedCard),
                    Card(1, 2, "flex-grow: 2 — 吾輩は猫である。名前はまだ無い。", selectedCard),
                    Card(2, 1, "flex-grow: 1", selectedCard)),

                // Borders, per-corner radius, inset shadow, opacity.
                Div().Row().Gap(12).AlignItems(Align.Start).Children(
                    Div().Size(120, 64).Border(2, accent).BorderRadius(16, 0, 16, 0).Center()
                        .Children(Text("border")),
                    Div().Size(120, 64).Background(raised).BorderRadius(8)
                        .BoxShadow(new Shadow(0, 2, 8, 0, new Color(0, 0, 0, 0.6f), true)).Center()
                        .Children(Text("inset")),
                    Div().Size(120, 64).Background(accent).BorderRadius(8).Opacity(0.5f).Center()
                        .Children(Text("opacity .5")),
                    Div().Size(64, 64).Background(warning).BorderRadius(Length.Percent(50))),

                // Stacking: z-index lifts the middle square above its later sibling.
                Div().Height(90).Children(
                    Div().Absolute().Left(0).Top(0).Size(80, 80).Background(Colors.Hex(0xEF4444)).BorderRadius(8),
                    Div().Absolute().Left(40).Top(10).Size(80, 80).Background(Colors.Hex(0x22C55E)).BorderRadius(8)
                        .ZIndex(1).BoxShadow(Shadow.Large),
                    Div().Absolute().Left(80).Top(0).Size(80, 80).Background(Colors.Hex(0x8B5CF6)).BorderRadius(8)),

                // overflow: hidden clips the long text; text-align center.
                Div().Width(260).Height(48).OverflowHidden().Border(1, raised).BorderRadius(6).Padding(8)
                    .Children(Text("overflow: hidden keeps this long line inside its box, whatever happens").TextAlign(TextAlignMode.Center)),

                // Buttons from the sheet; "−1" is disabled at zero.
                Div().Row().Gap(8).Children(
                    Div().Class("button").Disabled(() => count.Value <= 0).OnClick(() => count.Value--).Children(Text("−1")),
                    Div().Class("button primary").OnClick(() => count.Value++).Children(Text("+1")),
                    Div().Class("button").OnClick(() => count.Value = 0).Children(Text("Reset"))),

                // Appears once the counter reaches 5.
                Show(() => many.Value,
                    () => Div().Class("card").Background(warning).TextColor(Colors.Hex(0x1F2937))
                        .Children(Text("Five or more! (Show)")),
                    () => Text("Click +1 to count up.")));
        return ScrollArea(ScrollState(), page).FullSize()
            .Background(surface).TextColor(text).FontSize(16);
    }

    private Box Card(int index, float grow, string label, Signal<int> selected)
    {
        return Div().Class("card interactive").Class("selected", () => selected.Value == index)
            .FlexGrow(grow).OnClick(() => selected.Value = selected.Peek() == index ? -1 : index)
            .Children(Text(label), Text("click to select").Class("caption"));
    }
}
