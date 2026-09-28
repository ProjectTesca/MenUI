using System.Collections.Generic;
using MenUI;
using UnityEngine;

// Animation: style transitions (hover, size, enter and exit), easing curves
// and springs, keyframes, and values animated by hand.
public class MenUIAnimationDemo : MenUIApp
{
    private const float Travel = 220f;
    private int nextItem = 4;

    protected override Element Build()
    {
        Theme theme = UI.Theme;
        AnimationSpec spring = AnimationSpec.Spring(380f, 0.7f);
        AnimationSpec quick = AnimationSpec.Tween(0.2f, Easing.EaseOut);

        AddStyleSheet(new StyleSheet()
            // Hover a tile: its color and corners move (a spring overshoots a little).
            .Rule("tile", s => s.Size(96, 72).Center().BorderRadius(8).Background(theme.Surface)
                .Border(1, theme.Border).Transition(TransitionGroup.Colors, quick).Transition(TransitionGroup.Corners, spring))
            .Rule("tile:hover", s => s.BorderRadius(28).Background(theme.Accent).BorderColor(theme.Accent).TextColor(theme.AccentText))
            .Rule("tile:active", s => s.BorderRadius(8))
            // List items slide and fade in, and fade out.
            .Rule("item", s => s.Row().AlignItems(Align.Center).Gap(8).Padding(8, 12).BorderRadius(8)
                .Background(theme.Surface).Border(1, theme.Border)
                .Transition(StyleProperty.Opacity, quick).Transition(StyleProperty.MarginLeft, spring))
            .Rule("item:enter-from", s => s.Opacity(0f).MarginLeft(-40))
            .Rule("item:exit-to", s => s.Opacity(0f)));

        Box tiles = Div().Row().Gap(12).FlexWrap(MenUI.WrapMode.Wrap);
        string[] names = { "Hover", "me", "and", "press" };
        for (int i = 0; i < names.Length; i++)
        {
            tiles.Child(Div().Tag("tile").Child(Text(names[i])));
        }

        return ScrollArea(ScrollState(), Div().Column().Gap(22).Padding(24).Children(
            Text("Animation").FontSize(26),
            Section("Transitions on states", tiles),
            Section("Size", SizeDemo(spring)),
            Section("Enter and exit", ListDemo()),
            Section("Curves (press Play)", CurvesDemo()),
            Section("Keyframes and colors", KeyframesDemo(theme))))
            .FullSize().FontSize(16).Background(theme.Background).TextColor(theme.Text);
    }

    private Box Section(string title, Element content)
    {
        return Div().Column().Gap(10).Children(Text(title).FontSize(18), content);
    }

    // A panel that opens with a spring. It sits in a fixed-size box (flex-shrink
    // 0: a relayout boundary), so each frame lays out only that box, not the page.
    private Element SizeDemo(AnimationSpec spring)
    {
        Signal<bool> open = Signal(false);
        Box drawer = Div().Row().AlignItems(Align.Center).Gap(10).Padding(10).OverflowHidden()
            .BorderRadius(12).Background(() => UI.Theme.Accent).TextColor(() => UI.Theme.AccentText)
            .Width(() => open.Value ? (Length)320 : 52)
            .Transition(StyleProperty.Width, spring)
            .Children(Text("≡").FontSize(20), Text("A drawer that springs open"));
        return Div().Row().Gap(12).AlignItems(Align.Center).Children(
            Button(Text(() => open.Value ? "Close" : "Open")).OnClick(() => open.Value = !open.Peek()),
            Div().Size(340, 52).FlexShrink(0).Child(drawer));
    }

    private Element ListDemo()
    {
        Signal<List<int>> items = Signal(new List<int> { 1, 2, 3 });
        Box list = Div().Column().Gap(6).Width(260).Child(For(() => items.Value, item => item, (item, index) =>
        {
            int id = item.Peek();
            return Div().Tag("item").Children(
                Text("Item " + id).FlexGrow(1),
                Button("×", ButtonVariant.Ghost).OnClick(() =>
                {
                    List<int> next = new List<int>(items.Peek());
                    next.Remove(id);
                    items.Value = next;
                }));
        }));
        return Div().Column().Gap(10).Children(
            Button("Add item").OnClick(() =>
            {
                List<int> next = new List<int>(items.Peek());
                next.Add(nextItem++);
                items.Value = next;
            }),
            list);
    }

    // Dots crossing a track with different curves. They move on the GPU
    // (Translate), so this costs no layout or repaint per frame.
    private Element CurvesDemo()
    {
        string[] labels = { "Linear", "Ease", "Ease in-out", "Bézier overshoot", "Spring" };
        AnimationSpec[] motions =
        {
            AnimationSpec.Tween(1f, Easing.Linear),
            AnimationSpec.Tween(1f, Easing.Ease),
            AnimationSpec.Tween(1f, Easing.EaseInOut),
            AnimationSpec.Tween(1f, Easing.CubicBezier(0.34f, 1.56f, 0.64f, 1f)),
            AnimationSpec.Spring(120f, 0.45f),
        };
        AnimatedFloat[] positions = new AnimatedFloat[labels.Length];
        Box rows = Div().Column().Gap(8);
        for (int i = 0; i < labels.Length; i++)
        {
            AnimatedFloat position = Animated(0f);
            positions[i] = position;
            Box dot = Div().Absolute().Left(0).Top(2).Size(16, 16).BorderRadius(8).Background(() => UI.Theme.Accent)
                .Translate(() => position.Value, () => 0f);
            rows.Child(Div().Row().AlignItems(Align.Center).Gap(12).Children(
                Text(labels[i]).Width(130).FontSize(13),
                Div().Position(PositionMode.Relative).Size(Travel + 16, 20).BorderRadius(10)
                    .Background(() => UI.Theme.Track).Child(dot)));
        }
        bool atEnd = false;
        return Div().Column().Gap(10).Children(
            Button("Play", ButtonVariant.Primary).OnClick(() =>
            {
                atEnd = !atEnd;
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i].AnimateTo(atEnd ? Travel : 0f, motions[i]);
                }
            }),
            rows);
    }

    private Element KeyframesDemo(Theme theme)
    {
        // A heartbeat: two quick beats, then a rest, forever.
        AnimatedFloat beat = Animated(0f);
        beat.Play(new Keyframes(0f).To(1f, 0.12f, Easing.EaseOut).To(0.3f, 0.12f).To(1f, 0.12f, Easing.EaseOut)
            .To(0f, 0.4f, Easing.EaseIn).Hold(0.5f), true);
        Box heart = Div().Size(28, 28).BorderRadius(14).Background(theme.Danger)
            .Opacity(() => 0.35f + 0.65f * beat.Value);

        // A swatch that moves through colors when clicked.
        Color[] palette = { Colors.Hex(0x3B82F6), Colors.Hex(0x22C55E), Colors.Hex(0xF59E0B), Colors.Hex(0xEF4444) };
        AnimatedColor color = Animated(palette[0]);
        int current = 0;
        Box swatch = Div().Size(120, 28).BorderRadius(6).Center().Background(() => color.Value)
            .TextColor(Colors.White).Child(Text("Click me").FontSize(13))
            .OnClick(() =>
            {
                current = (current + 1) % palette.Length;
                color.AnimateTo(palette[current], AnimationSpec.Tween(0.5f, Easing.EaseInOut));
            });

        // A value driven by hand every frame.
        Signal<float> seconds = Signal(0f);
        EveryFrame(dt => seconds.Value = seconds.Peek() + dt);
        TextElement clock = Text(() => "Running for " + seconds.Value.ToString("0.0") + " s").FontSize(13)
            .TextColor(theme.MutedText);

        return Div().Row().Gap(20).AlignItems(Align.Center).Children(heart, swatch, clock);
    }
}
