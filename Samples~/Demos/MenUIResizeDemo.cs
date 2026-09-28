using MenUI;
using UnityEngine;

// Live resizing: the panel changes its pixel size (it grows in the world, and
// the layout reflows) and its scale (the content zooms, drawn sharp at every
// size). Drag the sliders, pick a scale, or switch on "Animate" for a smooth
// sweep through sizes and scales. Editing bufferWidth / bufferHeight / scale
// in the inspector while playing works too.
//
// Nothing scrolls: it is all flexbox (wrapping cards, a growing column, a
// sidebar that folds away below 820 logical px) and overflow: hidden. The
// sizes the controls and the animation reach all fit (at least about
// 630 x 480 logical).
public class MenUIResizeDemo : MenUIApp
{
    // The panel starts this big (1067 x 667 logical: everything fits).
    private const int InitialWidth = 1600;
    private const int InitialHeight = 1000;
    private const float InitialScale = 1.5f;
    // With the largest scale (1.75) these still fit everything.
    private const float MinimumWidth = 1100f;
    private const float MinimumHeight = 840f;

    private Signal<float> width;
    private Signal<float> height;
    private Signal<float> zoom;
    private Signal<bool> animate;
    private float animationTime;
    private int baseWidth, baseHeight;
    private float baseScale;

    protected override Element Build()
    {
        // The top-left corner stays put, so the controls stay under the pointer.
        resizePivot = new Vector2(0f, 1f);
        ResizePanel(InitialWidth, InitialHeight, InitialScale);
        baseWidth = InitialWidth;
        baseHeight = InitialHeight;
        baseScale = InitialScale;
        width = Signal((float)bufferWidth);
        height = Signal((float)bufferHeight);
        zoom = Signal(scale);
        animate = Signal(false);
        Signal<string> note = Signal("");
        Effect(() => ResizePanel(Mathf.RoundToInt(width.Value), Mathf.RoundToInt(height.Value), zoom.Value));
        // The light theme (the default); the page uses its colors too.
        Theme theme = UI.Theme;
        Color muted = theme.MutedText;

        // Logical width, for the responsive parts (the sidebar folds away).
        Computed<float> logicalWidth = Computed(() => width.Value / zoom.Value);

        Box controls = Div().Flex().Column().Gap(10).Padding(16).Width(280).FlexShrink(0)
            .Background(theme.Surface).BorderWidth(0, 1, 0, 0).BorderColor(theme.Border).Children(
                Text("Live resize").FontSize(20),
                Text(() => Mathf.RoundToInt(width.Value) + " x " + Mathf.RoundToInt(height.Value) + " px, scale "
                    + zoom.Value.ToString("0.00") + " → " + Mathf.RoundToInt(logicalWidth.Value) + " x "
                    + Mathf.RoundToInt(height.Value / zoom.Value) + " logical").FontSize(13).TextColor(muted),
                Text("Width").FontSize(13),
                Slider(width, MinimumWidth, 2000f, 2f).Disabled(() => animate.Value),
                Text("Height").FontSize(13),
                Slider(height, MinimumHeight, 1200f, 2f).Disabled(() => animate.Value),
                Text("Scale").FontSize(13),
                Div().Row().Gap(6).Children(
                    ScaleButton(1f), ScaleButton(1.25f), ScaleButton(1.5f), ScaleButton(1.75f)),
                Switch(animate, "Animate"),
                Button("Reset").Disabled(() => animate.Value).OnClick(() => Batch(() =>
                {
                    width.Value = baseWidth;
                    height.Value = baseHeight;
                    zoom.Value = baseScale;
                })));

        Box cards = Div().Flex().Row().FlexWrap(MenUI.WrapMode.Wrap).Gap(12);
        string[] titles = { "Layout", "Text", "Components", "Scrolling", "Reactivity", "Themes" };
        Color[] accents = { Colors.Hex(0x3B82F6), Colors.Hex(0x22C55E), Colors.Hex(0xF59E0B),
            Colors.Hex(0xEF4444), Colors.Hex(0x8B5CF6), Colors.Hex(0x14B8A6) };
        for (int i = 0; i < titles.Length; i++)
        {
            cards.Child(Div().Flex().Column().Gap(6).FlexGrow(1).FlexBasis(150).Padding(14).BorderRadius(10)
                .Background(theme.Surface).BorderWidth(0, 0, 0, 4).BorderColor(accents[i]).BoxShadow(Shadow.Small).Children(
                    Text(titles[i]).FontSize(17),
                    Text("Wraps as the panel narrows.").FontSize(13).TextColor(muted)));
        }

        Box content = Div().Flex().Column().Gap(14).Padding(20).FlexGrow(1).MinWidth(0).OverflowHidden().Children(
            Text("MenUI — every size, every scale").FontSize(24),
            Text("吾輩は猫である。名前はまだ無い。どこで生れたかとんと見当がつかぬ。何でも薄暗いじめじめした所で"
                + "ニャーニャー泣いていた事だけは記憶している。 Text rewraps at every width, and stays crisp at every scale.")
                .TextColor(muted),
            cards,
            Div().Row().Gap(8).AlignItems(Align.Center).Children(
                TextBox(note).Placeholder("Type something").FlexGrow(1),
                Button("Send", ButtonVariant.Primary).OnClick(() => Toast(note.Peek().Length > 0 ? note.Peek() : "Hello"))),
            ProgressBar(() => Mathf.InverseLerp(MinimumWidth, 2000f, width.Value)));

        // Narrow panels fold the sidebar away.
        Box page = Div().Flex().Row().FullSize().Children(
            Show(() => logicalWidth.Value >= 820f, () => controls,
                () => Div().Flex().Column().Padding(8).Background(theme.Surface)
                    .BorderWidth(0, 1, 0, 0).BorderColor(theme.Border).Children(
                    Switch(animate))),
            content);
        return page.Background(theme.Background).TextColor(theme.Text).FontSize(16);
    }

    private Box ScaleButton(float value)
    {
        return Button(value.ToString("0.##") + "x").Class("primary", () => Mathf.Abs(zoom.Value - value) < 0.001f)
            .Disabled(() => animate.Value)
            .OnClick(() => zoom.Value = value);
    }

    private void Update()
    {
        if (animate == null || !animate.Peek())
        {
            animationTime = 0f;
            return;
        }
        // A slow, looping sweep: wide and narrow, tall and short, zooming in and out.
        animationTime += Time.deltaTime;
        float t = animationTime;
        // The narrow end folds the sidebar away.
        width.Value = Mathf.Round(Mathf.Lerp(MinimumWidth, 2000f, 0.5f + 0.5f * Mathf.Sin(t * 0.6f)));
        height.Value = Mathf.Round(Mathf.Lerp(MinimumHeight, 1200f, 0.5f + 0.5f * Mathf.Sin(t * 0.43f + 1f)));
        zoom.Value = Mathf.Lerp(1.1f, 1.75f, 0.5f + 0.5f * Mathf.Sin(t * 0.31f + 2f));
    }
}
