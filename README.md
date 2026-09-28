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

- **Reactivity** — `Signal`, `Computed`, `Effect`, `Show`, `Dynamic`, keyed `For`;
  components run once, bindings are lambdas, only what changed updates.
- **Styling** — chained CSS properties (`.Padding(16).BorderRadius(12)`), `Style`
  objects, style sheets with classes, descendant/child selectors and
  `:hover` `:active` `:focus` `:disabled` `:dragging` `:drag-over`.
- **Layout** — block and flexbox (grow/shrink with CSS's freeze-and-redistribute,
  wrap, justify/align, gap, `min-*: auto`), absolute/relative positioning,
  `overflow: hidden`, `z-index` stacking contexts and an overlay layer; logical
  pixels (fractions allowed) snapped to whole physical pixels.
- **Drawing** — rounded borders, box shadows (inset too), opacity, text through
  TextMeshPro's dynamic atlases (Japanese included), all in one GPU draw;
  incremental: only changed records are rewritten.
- **Scrolling** — `ScrollView` + separate `Scrollbar` sharing a `ScrollState`,
  `ScrollArea`, momentum; scrolling and `Translate` move content on the GPU
  without layout. `VirtualList` for long lists.
- **Input** — device-independent routing (hit testing, capture, bubbling
  events, drag and drop) behind `IPointerSource`; a desktop source is included.
- **Text boxes** — `TextBox` / `TextArea` bound to a `Signal<string>`, typed
  through VRChat's own input UI (desktop window or VR keyboard, IME included)
  behind `ITextInputHost`.
- **Components** — buttons, check boxes, switches, radio buttons, sliders,
  progress bars, spinners, tabs, selects, dialogs, tooltips and toasts, with
  light and dark themes.

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

## Fonts

MenUI ships Noto Sans JP (SIL Open Font License, see
`Runtime/Fonts/NotoSansJP-OFL.txt`) as the default font. To add fonts, open
**MenUI > Settings** and add an entry with a `.ttf`/`.otf` (or a TMP font
asset); then use it by name:

```csharp
Text("Title").FontFamily("Inter");
sheet.Rule(".title", s => s.FontFamily("M PLUS Rounded, Noto Sans JP"));
```

## Samples

Package Manager › MenUI › Samples › **Demos**: a CSS tour, scrolling with a
10,000-row virtual list, a drag-and-drop kanban board, a form with text
boxes, and every built-in component with a theme switch.

## Developing MenUI

- Work on the package inside a VRChat world project (`Packages/io.tesca.menui`).
- The demos live in `Samples~/Demos`; to run them in the development project,
  link them into `Assets`:
  `ln -s ../Packages/io.tesca.menui/Samples~/Demos Assets/MenUISamples`
- Headless tests (reactivity, layout, input routing; no Unity instance needed),
  from the project root: `Packages/io.tesca.menui/Tests~/run.sh`
- Assets the package ships (quad pool mesh, materials, the default font asset)
  are created in the package the first time a panel is set up, if missing.
