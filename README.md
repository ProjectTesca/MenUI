# MenUI

A modern UI library for VRChat worlds, written in [MenSharp](https://github.com/ProjectTesca/MenSharp).
Styling, layout, drawing and input are MenUI's own (not a uGUI wrapper): a
panel's elements are laid out in whole physical pixels and drawn by the GPU
into one texture.

```csharp
using MenUI;

public class MyMenu : MenUIApp
{
    protected override Element Build()
    {
        var count = Signal(0);
        return Div().Flex().Column().Gap(12).Padding(24).Children(
            Text(() => $"Clicked {count.Value} times").FontSize(20),
            Div().Class("button").OnClick(() => count.Value++).Children(Text("Click me")));
    }
}
```

Add the behaviour to a GameObject: the panel (display quad, offscreen
renderer, text shaping) is set up automatically.

## Features

- **Reactivity** — `Signal`, `Computed`, `Effect`, `Show`, `Dynamic`, keyed `For`,
  `Pages` (tabs: each page built once and kept, so switching back is only a
  hide and a show; optionally built ahead); components run once, bindings
  are lambdas, only what changed updates.
- **Styling** — chained CSS properties (`.Padding(16).BorderRadius(12)`), `Style`
  objects, style sheets with classes, descendant/child selectors and
  `:hover` `:active` `:focus` `:disabled` `:dragging` `:drag-over`.
- **Layout** — block and flexbox (grow/shrink with CSS's freeze-and-redistribute,
  wrap, justify/align, gap, `min-*: auto`), absolute/relative positioning,
  `overflow: hidden`, `z-index` stacking contexts and an overlay layer; logical
  pixels (fractions allowed) snapped to whole physical pixels. Incremental: a
  change is laid out again only as far up as sizes actually change.
- **Animation** — CSS-style transitions (states, classes, bindings, style
  sheets), `:enter-from` / `:exit-to` for things appearing and leaving, tweens
  with any cubic Bézier, springs, keyframes, and animated values for anything
  else.
- **Drawing** — rounded borders, box shadows (inset too), opacity, text through
  TextMeshPro's dynamic atlases (Japanese included), all in one GPU draw;
  incremental: only changed records are rewritten.
- **Scrolling** — `ScrollView` + separate `Scrollbar` sharing a `ScrollState`,
  `ScrollArea` (a view with its bar beside it), momentum. In VRChat the mouse
  wheel turns the view, so the bar (drag the thumb, press the track to page)
  and dragging the content are how players scroll; scrolling and `Translate` move content on the GPU
  without layout. `VirtualList` for long lists.
- **Input** — device-independent routing (hit testing, capture, bubbling
  events, drag and drop) behind `IPointerSource`; a desktop source is included.
- **Text boxes** — `TextBox` / `TextArea` bound to a `Signal<string>`, typed
  through VRChat's own input UI (desktop window or VR keyboard, IME included)
  behind `ITextInputHost`.
- **Components** — buttons, check boxes, switches, radio buttons, sliders,
  progress bars, spinners, tabs, selects, dialogs, tooltips and toasts, with
  light and dark themes.
- **Material Design 3** — the M3 Expressive components, color schemes (from
  one seed color too), shapes, springs and Material Symbols icons
  (`MenUI.Material3`).

## Components

```csharp
Button("Save", ButtonVariant.Primary).OnClick(Save);   // Secondary, Danger, Ghost
Checkbox(agreed, "I agree");
Switch(muted, "Mute");
Radio(quality, Quality.High, "High");                  // same signal: one group
Slider(volume, 0f, 1f);                                // step optional
ProgressBar(() => loaded.Value / total);
Tabs(page, "General", "Audio");                        // show the page with Dynamic
Select(fruit, "Apple", "Banana");                      // or Select(value, list, label)
Dialog(open, () => Div().Children(...));
Button("?").Tooltip("Help");
Toast("Saved");
SetTheme(Theme.Dark);                                  // or a Theme of your own
```

Each control is plain elements with tags (`button`, `checkbox`, `switch`,
`radio`, `slider`, `select`, `tab`, `dialog`, ...), so style sheets restyle
them; MenUI's own rules are the lowest, like a browser's defaults. Values
live in your signals. `.Disabled(() => ...)` works on all of them.
`Length.FitContent` (CSS `width: fit-content`) keeps buttons from stretching.

## Text boxes

```csharp
var name = Signal("");
TextBox(name).Placeholder("Your name").MaxLength(40).OnCommit(value => Save(value));
TextArea(notes).Rows(4);
TextBox(password).Password();
```

VRChat opens its input UI only when a player clicks a real uGUI `InputField`
(activating one from code does not). So MenUI draws the box, and the default
host (`InputFieldTextInputHost`) lays a transparent `InputField` over each box
a pointer is on, following MenUI's hit testing (nothing covering the box is
shadowed); what is typed is read back into the signal. There is no caret: the
editing happens in VRChat's UI. Style boxes with the `input` / `textarea`
tags (`:hover`, `:focus`, `:disabled`); MenUI's defaults are the lowest rules.

## Animation

```csharp
// Transitions: changes animate instead of jumping — from :hover, classes,
// bindings or rules alike.
sheet.Rule("card", s => s.Background(gray).BorderRadius(8)
        .Transition(TransitionGroup.Colors, AnimationSpec.Tween(0.15f, Easing.EaseOut))
        .Transition(TransitionGroup.Corners, AnimationSpec.Spring(380f, 0.7f)))
     .Rule("card:hover", s => s.Background(blue).BorderRadius(24));
Div().Width(() => open.Value ? 320 : 56).Transition(StyleProperty.Width, AnimationSpec.Spring());

// Appearing and leaving (Show, Dynamic and For wait for the exit).
Div().EnterFrom(s => s.Opacity(0).MarginTop(-8)).ExitTo(s => s.Opacity(0))
     .Transition(TransitionGroup.All, AnimationSpec.Tween(0.2f));

// Values: read .Value in bindings; Translate moves on the GPU.
AnimatedFloat x = Animated(0f);
dot.Translate(() => x.Value, () => 0f);
x.AnimateTo(200f, AnimationSpec.Tween(0.6f, Easing.CubicBezier(0.34f, 1.56f, 0.64f, 1f)));
x.Play(new Keyframes(0f).To(1f, 0.2f).Hold(0.3f).To(0f, 0.5f), loop: true);
AnimatedColor tint = Animated(Colors.White);
Animate(0.5f, t => progress.Value = t, Easing.EaseInOut);   // or EveryFrame(dt => ...)
```

`AnimationSpec` is how a value moves: `Tween(seconds, easing)` (CSS's named
curves, `Easing.CubicBezier(...)` or `Easing.Custom(t => ...)`) or
`Spring(stiffness, dampingRatio)` — a spring keeps its velocity when the target
changes midway, so interrupted animations never jump. `.Delay(seconds)` on
either.

What each frame costs depends on what moves: `Translate` and scrolling move
on the GPU (no layout, no repaint); colors, radii and opacity rewrite only the
records of the element (opacity: its subtree); lengths lay out again, but
only as far up as a size changes — keep an animated size inside a box of fixed
size (a relayout boundary: pixel width and height, and `flex-shrink: 0` for a
flex item) and the rest of the panel is not touched. Transitions write the
moving values straight into the computed style (no signals, no restyling),
except inherited ones (text color, font size), which children must follow.
Shadows, enums and fonts do not animate. A fading subtree keeps its draw
records at zero opacity, so fading in and out never repaints everything.

## Material Design 3

```csharp
using MenUI.Material3;

public class MyMenu : MaterialApp            // or MenUIApp + MaterialDesign.Of(UI)
{
    protected override Element Build()
    {
        var going = Signal(0);
        return M3.Surface().FullSize().Padding(24).Column().Gap(16).Children(
            M3.Text("Hello", TypeRole.HeadlineSmall),
            M3.Button("Save", MaterialSymbol.Check).OnClick(Save),
            M3.SegmentedGroup(going, new[] { "Going", "Maybe" }),
            M3.LinearProgress(() => progress.Value, wavy: true),
            M3.Fab(MaterialSymbol.Add, FabSize.Large));
    }
}
```

Components (M3 Expressive): buttons in five sizes (round or square, toggles
that morph between them), icon buttons, FABs, extended FABs, the FAB menu,
button groups (standard and connected), split buttons, floating and docked
toolbars, linear and circular progress (flat or wavy), the loading
indicator, checkbox, radio, switch, slider, text fields (filled, outlined),
chips, cards, lists, dividers, badges, tabs, navigation bar, navigation rail
(narrow ↔ wide), top app bars, menus, dialogs, snackbars and tooltips.

- Colors: `M3.SetColorScheme(MaterialColorScheme.BaselineDark)`, or
  `MaterialColorScheme.FromSeed(color, dark)` (tonal palettes like
  Material's "tonal spot", computed in CIELAB — close to, not exactly,
  Material's own HCT results), or any scheme you fill in. Changing it
  animates.
- Motion: `M3.SetMotionScheme(MotionScheme.Expressive)` (the default, a
  little bouncy) or `MotionScheme.Standard`.
- Icons: `MaterialSymbol.*` (Material Symbols Rounded; `M3.Icon(symbol,
  filled: true)`).
- Everything is plain elements with `m3-...` tags; the look is a style sheet
  under yours, so your rules override it.
- MenUI has one font weight, so the type scale sets sizes only.

## Transforms and graphics

`.Translate(x, y)` and `.Scale(s)` are style properties applied by the GPU
(no layout, no repaint), and they transition like any other; an element
that moves, scales or fades gets a transform group of its own while it does,
so its opacity is applied by the GPU as well. `Graphic(g => ...)` draws what
boxes cannot — arcs, wavy lines, ripples, and shapes that morph
(`ShapeSpec.Cookie9`, `Sunny`, `Pentagon`, ...) — rerunning like a binding.

## Live resizing

`ResizePanel(width, height, scale)` (or editing **bufferWidth**, **bufferHeight**
and **scale** in the inspector while playing) resizes a running panel: a
bigger pixel size makes a bigger panel whose layout reflows; a bigger scale
zooms the content, drawn sharp. `resizePivot` is the point that stays in
place. The render texture keeps spare room, so animating the size is cheap.

## Fonts

MenUI ships Noto Sans JP (SIL Open Font License, see
`Runtime/Fonts/NotoSansJP-OFL.txt`) as the default font, and the icon fonts
"Material Symbols Rounded" and "Material Symbols Rounded Filled" (Apache
License 2.0; static instances of Google's variable font, without ligatures —
see `Runtime/Fonts/MaterialSymbols-NOTICE.txt`). To add fonts, open
**MenUI > Settings** and add an entry with a `.ttf`/`.otf` (or a TMP font
asset); then use it by name:

```csharp
Text("Title").FontFamily("Inter");
sheet.Rule(".title", s => s.FontFamily("M PLUS Rounded, Noto Sans JP"));
```

## Samples

Package Manager › MenUI › Samples › **Demos**: a CSS tour, scrolling with a
10,000-row virtual list, a drag-and-drop kanban board, a form with text
boxes, every built-in component with a theme switch, live resizing,
animation (transitions, enter and exit, curves, keyframes), and Material
Design 3 Expressive.

## Developing MenUI

- Work on the package inside a VRChat world project (`Packages/io.tesca.menui`).
- The demos live in `Samples~/Demos`; to run them in the development project,
  link them into `Assets`:
  `ln -s ../Packages/io.tesca.menui/Samples~/Demos Assets/MenUISamples`
- Headless tests (reactivity, layout, input routing; no Unity instance needed),
  from the project root: `Packages/io.tesca.menui/Tests~/run.sh`
- Assets the package ships (quad pool mesh, materials, the default font asset)
  are created in the package the first time a panel is set up, if missing.
