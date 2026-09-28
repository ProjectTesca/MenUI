// The colors and shapes of MenUI's built-in components, and the rules that
// give them their default look.
//
//   UI.SetTheme(Theme.Dark);          // before or after building: both work
//   var theme = Theme.Light;          // or start from one and adjust it
//   theme.Accent = Colors.Hex(0x8B5CF6);
//   UI.SetTheme(theme);
//
// The rules live in the panel's built-in style sheet (the first sheet, like
// a browser's user-agent sheet), keyed by each component's tag: `button`,
// `checkbox`, `switch`, `radio`, `slider`, `progress`, `select`, `tab`,
// `dialog`, ... Any rule or chained style of yours overrides them; e.g.
// `.Rule("button.primary", s => s.BorderRadius(999))`. A component's rules
// are added the first time one is built.

using UnityEngine;

namespace MenUI
{
    public sealed class Theme
    {
        /// Primary actions, selection, focus.
        public Color Accent;
        public Color AccentHover;
        /// Text and marks on the accent color.
        public Color AccentText;
        public Color Danger;
        public Color DangerHover;
        /// A page or panel background that suits the theme (not used by the
        /// components themselves).
        public Color Background;
        /// Controls: buttons, text boxes, check boxes.
        public Color Surface;
        public Color SurfaceHover;
        public Color SurfaceActive;
        /// Menus and dialogs.
        public Color Popup;
        public Color Text;
        public Color MutedText;
        public Color Border;
        public Color BorderHover;
        /// The unfilled part of switches, sliders and progress bars.
        public Color Track;
        /// Behind a dialog.
        public Color Backdrop;
        /// Tooltips and toasts.
        public Color Inverse;
        public Color InverseText;
        /// Corner radius of controls, in logical pixels.
        public float Radius = 6f;

        /// A new copy of the light theme (the default).
        public static Theme Light
        {
            get
            {
                Theme t = new Theme();
                t.Accent = Colors.Hex(0x0969DA);
                t.AccentHover = Colors.Hex(0x0860CA);
                t.AccentText = Colors.White;
                t.Danger = Colors.Hex(0xCF222E);
                t.DangerHover = Colors.Hex(0xA40E26);
                t.Background = Colors.Hex(0xF6F8FA);
                t.Surface = Colors.White;
                t.SurfaceHover = Colors.Hex(0xF3F4F6);
                t.SurfaceActive = Colors.Hex(0xE6E8EB);
                t.Popup = Colors.White;
                t.Text = Colors.Hex(0x1F2328);
                t.MutedText = Colors.Hex(0x656D76);
                t.Border = Colors.Hex(0xD0D7DE);
                t.BorderHover = Colors.Hex(0x8C959F);
                t.Track = Colors.Hex(0xD0D7DE);
                t.Backdrop = Colors.Rgba(0, 0, 0, 0.45f);
                t.Inverse = Colors.Hex(0x1F2328);
                t.InverseText = Colors.White;
                return t;
            }
        }

        /// A new copy of the dark theme.
        public static Theme Dark
        {
            get
            {
                Theme t = new Theme();
                t.Accent = Colors.Hex(0x3B82F6);
                t.AccentHover = Colors.Hex(0x60A5FA);
                t.AccentText = Colors.White;
                t.Danger = Colors.Hex(0xEF4444);
                t.DangerHover = Colors.Hex(0xF87171);
                t.Background = Colors.Hex(0x0F172A);
                t.Surface = Colors.Hex(0x1E293B);
                t.SurfaceHover = Colors.Hex(0x334155);
                t.SurfaceActive = Colors.Hex(0x475569);
                t.Popup = Colors.Hex(0x1E293B);
                t.Text = Colors.Hex(0xE2E8F0);
                t.MutedText = Colors.Hex(0x94A3B8);
                t.Border = Colors.Hex(0x475569);
                t.BorderHover = Colors.Hex(0x64748B);
                t.Track = Colors.Hex(0x334155);
                t.Backdrop = Colors.Rgba(0, 0, 0, 0.6f);
                t.Inverse = Colors.Hex(0xE2E8F0);
                t.InverseText = Colors.Hex(0x0F172A);
                return t;
            }
        }
    }

    /// Which components' rules are in the built-in sheet (bits), and the rules.
    internal static class ComponentStyles
    {
        internal const int TextBox = 1;
        internal const int Button = 2;
        internal const int Checkbox = 4;
        internal const int Switch = 8;
        internal const int Radio = 16;
        internal const int Slider = 32;
        internal const int Progress = 64;
        internal const int Spinner = 128;
        internal const int Select = 256;
        internal const int Tabs = 512;
        internal const int Dialog = 1024;
        internal const int Tooltip = 2048;
        internal const int Toast = 4096;
        internal const int Divider = 8192;
        internal const int Last = Divider;

        internal static void Add(StyleSheet sheet, Theme t, int kind)
        {
            float r = t.Radius;
            if (kind == TextBox)
            {
                sheet
                    .Rule("input, textarea", s => s.Padding(6, 10).Background(t.Surface).TextColor(t.Text)
                        .Border(1, t.Border).BorderRadius(r))
                    .Rule("input:hover, textarea:hover", s => s.BorderColor(t.BorderHover))
                    .Rule("input:focus, textarea:focus", s => s.BorderColor(t.Accent).BoxShadow(Ring(t.Accent)))
                    .Rule("input:disabled, textarea:disabled", s => s.Opacity(0.5f));
            }
            else if (kind == Button)
            {
                sheet
                    .Rule("button", s => s.Center().Row().Gap(6).Padding(6, 14).Width(Length.FitContent)
                        .BorderRadius(r).Border(1, t.Border).Background(t.Surface).TextColor(t.Text))
                    .Rule("button:hover", s => s.Background(t.SurfaceHover))
                    .Rule("button:active", s => s.Background(t.SurfaceActive))
                    .Rule("button.primary", s => s.Background(t.Accent).BorderColor(t.Accent).TextColor(t.AccentText))
                    .Rule("button.primary:hover", s => s.Background(t.AccentHover).BorderColor(t.AccentHover))
                    .Rule("button.danger", s => s.Background(t.Danger).BorderColor(t.Danger).TextColor(t.AccentText))
                    .Rule("button.danger:hover", s => s.Background(t.DangerHover).BorderColor(t.DangerHover))
                    .Rule("button.ghost", s => s.Background(Colors.Transparent).BorderColor(Colors.Transparent))
                    .Rule("button.ghost:hover", s => s.Background(t.SurfaceHover))
                    .Rule("button:disabled", s => s.Opacity(0.5f));
            }
            else if (kind == Checkbox)
            {
                AddLabel(sheet);
                sheet
                    .Rule("checkbox", s => s.Center().Size(18, 18).FlexShrink(0).BorderRadius(4).Border(1, t.Border)
                        .Background(t.Surface).TextColor(Colors.Transparent).FontSize(13))
                    .Rule("checkbox:hover", s => s.BorderColor(t.BorderHover))
                    .Rule("checkbox.checked", s => s.Background(t.Accent).BorderColor(t.Accent).TextColor(t.AccentText));
            }
            else if (kind == Switch)
            {
                AddLabel(sheet);
                sheet
                    .Rule("switch", s => s.Position(PositionMode.Relative).Size(36, 20).FlexShrink(0)
                        .BorderRadius(10).Background(t.Track))
                    .Rule("switch.checked", s => s.Background(t.Accent))
                    .Rule("switch-thumb", s => s.Absolute().Top(2).Size(16, 16).BorderRadius(8)
                        .Background(Colors.White).BoxShadow(Shadow.Small));
            }
            else if (kind == Radio)
            {
                AddLabel(sheet);
                sheet
                    .Rule("radio", s => s.Center().Size(18, 18).FlexShrink(0).BorderRadius(9).Border(1, t.Border)
                        .Background(t.Surface))
                    .Rule("radio:hover", s => s.BorderColor(t.BorderHover))
                    .Rule("radio.checked", s => s.Background(t.Accent).BorderColor(t.Accent))
                    .Rule("radio-dot", s => s.Size(6, 6).BorderRadius(3).Background(Colors.Transparent))
                    .Rule("radio.checked > radio-dot", s => s.Background(t.AccentText));
            }
            else if (kind == Slider)
            {
                sheet
                    .Rule("slider", s => s.Position(PositionMode.Relative).Height(20).MinWidth(120))
                    .Rule("slider:disabled", s => s.Opacity(0.5f))
                    .Rule("slider-rail", s => s.Absolute().Left(0).Right(0).Top(8).Height(4).BorderRadius(2)
                        .Background(t.Track))
                    .Rule("slider-fill", s => s.Absolute().Left(0).Top(8).Height(4).BorderRadius(2).Background(t.Accent))
                    .Rule("slider-thumb", s => s.Absolute().Top(2).MarginLeft(-8).Size(16, 16).BorderRadius(8)
                        .Background(Colors.White).Border(1, t.Border).BoxShadow(Shadow.Small))
                    .Rule("slider-thumb:hover, slider-thumb:active", s => s.BorderColor(t.Accent));
            }
            else if (kind == Progress)
            {
                sheet
                    .Rule("progress", s => s.Height(8).BorderRadius(4).Background(t.Track).OverflowHidden())
                    .Rule("progress-fill", s => s.FullHeight().BorderRadius(4).Background(t.Accent));
            }
            else if (kind == Spinner)
            {
                sheet
                    .Rule("spinner", s => s.Row().AlignItems(Align.Center).Gap(4).Width(Length.FitContent))
                    .Rule("spinner-dot", s => s.Size(8, 8).BorderRadius(4).Background(t.Accent));
            }
            else if (kind == Select)
            {
                sheet
                    .Rule("select", s => s.Row().AlignItems(Align.Center).JustifyContent(Justify.SpaceBetween).Gap(8)
                        .Padding(6, 10).MinWidth(120).BorderRadius(r).Border(1, t.Border)
                        .Background(t.Surface).TextColor(t.Text))
                    .Rule("select:hover", s => s.BorderColor(t.BorderHover))
                    .Rule("select.open", s => s.BorderColor(t.Accent).BoxShadow(Ring(t.Accent)))
                    .Rule("select:disabled", s => s.Opacity(0.5f))
                    .Rule("select-arrow", s => s.FontSize(10).TextColor(t.MutedText))
                    .Rule("select-menu", s => s.Absolute().Padding(4).BorderRadius(r).Border(1, t.Border)
                        .Background(t.Popup).TextColor(t.Text).BoxShadow(Shadow.Medium))
                    .Rule("option", s => s.Padding(6, 10).BorderRadius(4))
                    .Rule("option:hover", s => s.Background(t.SurfaceHover))
                    .Rule("option.selected", s => s.TextColor(t.Accent));
            }
            else if (kind == Tabs)
            {
                sheet
                    .Rule("tabs", s => s.Row().Gap(4).BorderWidth(0, 0, 1, 0).BorderColor(t.Border))
                    .Rule("tab", s => s.Padding(8, 14).MarginBottom(-1).BorderWidth(0, 0, 2, 0)
                        .BorderColor(Colors.Transparent).TextColor(t.MutedText))
                    .Rule("tab:hover", s => s.TextColor(t.Text))
                    .Rule("tab.selected", s => s.BorderColor(t.Accent).TextColor(t.Text))
                    .Rule("tab:disabled", s => s.Opacity(0.5f));
            }
            else if (kind == Dialog)
            {
                sheet
                    .Rule("dialog-backdrop", s => s.Absolute().Inset(0).Center().Background(t.Backdrop)
                        .PointerEvents(PointerEventsMode.Auto))
                    .Rule("dialog", s => s.Column().Gap(12).Padding(20).MinWidth(280).MaxWidth(Length.Percent(90))
                        .BorderRadius(12).Background(t.Popup).TextColor(t.Text).BoxShadow(Shadow.Large));
            }
            else if (kind == Tooltip)
            {
                sheet.Rule("tooltip", s => s.Absolute().Padding(4, 8).BorderRadius(4).FontSize(13)
                    .Background(t.Inverse).TextColor(t.InverseText).PointerEvents(PointerEventsMode.None));
            }
            else if (kind == Toast)
            {
                sheet
                    .Rule("toast-stack", s => s.Absolute().Left(0).Right(0).Bottom(24).Column()
                        .AlignItems(Align.Center).Gap(8).PointerEvents(PointerEventsMode.None))
                    .Rule("toast", s => s.Padding(10, 16).BorderRadius(8).Background(t.Inverse).TextColor(t.InverseText)
                        .BoxShadow(Shadow.Medium));
            }
            else if (kind == Divider)
            {
                sheet.Rule("divider", s => s.Height(1).FlexShrink(0).Background(t.Border));
            }
        }

        /// The row that holds a check box, switch or radio button and its label.
        private static void AddLabel(StyleSheet sheet)
        {
            if (sheet.byTag.ContainsKey("label"))
            {
                return;
            }
            sheet
                .Rule("label", s => s.Row().AlignItems(Align.Center).Gap(8).Width(Length.FitContent))
                .Rule("label:disabled", s => s.Opacity(0.5f));
        }

        /// A focus ring.
        private static Shadow Ring(Color accent)
        {
            return new Shadow(0f, 0f, 0f, 3f, new Color(accent.r, accent.g, accent.b, 0.3f));
        }
    }
}
