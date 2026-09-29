// Material 3 Expressive buttons: common buttons (five sizes, round or square,
// toggles that morph between the two), icon buttons, FABs, extended FABs, the
// FAB menu, button groups (standard and connected) and split buttons.
//
// Pressing morphs the corners (a spring from the motion scheme); hovering
// and pressing lay the state layer over the colors; a ripple follows the
// pointer.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MenUI.Material3
{
    public enum ButtonStyle { Filled, Tonal, Elevated, Outlined, Text }
    public enum IconButtonStyle { Standard, Filled, Tonal, Outlined }
    public enum IconButtonWidth { Narrow, Default, Wide }
    public enum FabSize { Regular, Medium, Large }
    public enum FabColor { PrimaryContainer, SecondaryContainer, TertiaryContainer, Primary, Secondary, Tertiary }
    public enum ButtonGroupKind { Standard, Connected }

    /// An action of a FAB menu.
    public sealed class FabMenuItem
    {
        public readonly string Label;
        public readonly string Icon;
        public readonly Action OnClick;

        public FabMenuItem(string label, string icon, Action onClick)
        {
            Label = label;
            Icon = icon;
            OnClick = onClick;
        }
    }

    internal static class MaterialButtons
    {
        private const int SizeCount = 5;

        // ---- common buttons --------------------------------------------------------------

        internal static Box Button(MaterialDesign m3, string label, string icon, ButtonStyle style, ComponentSize size,
            ButtonShape shape, Signal<bool> selected)
        {
            m3.Ensure(MaterialStyles.Button);
            MenUIContext ui = m3.ui;
            Box button = ui.Div().Tag("m3-button").Class(StyleClass(style)).Class(MaterialParts.SizeClass(size));
            if (shape == ButtonShape.Square)
            {
                button.Class("square");
            }
            button.Child(MaterialParts.Ripple(m3, button));
            TextElement iconText = null;
            if (!string.IsNullOrEmpty(icon))
            {
                iconText = MaterialParts.Icon(m3, icon, 0f, false);
                button.Child(iconText);
            }
            if (!string.IsNullOrEmpty(label))
            {
                button.Child(ui.Text(label).Tag("m3-label"));
            }
            if (selected != null)
            {
                button.Class("toggle").Class("selected", () => selected.Value)
                    .OnClick(() => selected.Value = !selected.Peek());
                if (iconText != null)
                {
                    iconText.Class("filled", () => selected.Value);
                }
            }
            return button;
        }

        private static string StyleClass(ButtonStyle style)
        {
            if (style == ButtonStyle.Tonal) return "tonal";
            if (style == ButtonStyle.Elevated) return "elevated";
            if (style == ButtonStyle.Outlined) return "outlined";
            if (style == ButtonStyle.Text) return "text";
            return "filled";
        }

        internal static void ButtonRules(StyleSheet sheet, MaterialColorScheme c, MotionScheme motion)
        {
            sheet.Rule("m3-button", s => s.Row().AlignItems(Align.Center).JustifyContent(Justify.Center)
                .Width(Length.FitContent).FlexShrink(0).Position(PositionMode.Relative)
                .Transition(TransitionGroup.Corners, motion.FastSpatial)
                .Transition(TransitionGroup.Colors, motion.DefaultEffects)
                .Transition(TransitionGroup.Spacing, motion.FastSpatial));
            for (int i = 0; i < SizeCount; i++)
            {
                string size = "m3-button." + MaterialParts.SizeClass((ComponentSize)i);
                ComponentSize component = (ComponentSize)i;
                float height = MaterialParts.BySize(component, 32f, 40f, 56f, 96f, 136f);
                float padding = MaterialParts.BySize(component, 12f, 16f, 24f, 48f, 64f);
                float gap = MaterialParts.BySize(component, 4f, 8f, 8f, 12f, 16f);
                float font = MaterialParts.BySize(component, 14f, 14f, 16f, 24f, 32f);
                float iconSize = MaterialParts.BySize(component, 20f, 20f, 24f, 32f, 40f);
                float square = MaterialParts.BySize(component, 12f, 12f, 16f, 28f, 28f);
                float pressed = MaterialParts.BySize(component, 8f, 8f, 12f, 16f, 16f);
                float round = height * 0.5f;
                float grow = MaterialParts.BySize(component, 4f, 6f, 8f, 12f, 16f);
                sheet
                    .Rule(size, s => s.Height(height).PaddingHorizontal(padding).Gap(gap).FontSize(font).BorderRadius(round))
                    .Rule(size + " > m3-icon", s => s.FontSize(iconSize))
                    .Rule(size + ".square", s => s.BorderRadius(square))
                    .Rule(size + ".toggle.selected", s => s.BorderRadius(square))
                    .Rule(size + ".square.toggle.selected", s => s.BorderRadius(round))
                    .Rule(size + ":active", s => s.BorderRadius(pressed))
                    .Rule(size + ".selected:active", s => s.BorderRadius(pressed))
                    // In a standard button group: the pressed one widens, its neighbors give way.
                    .Rule(size + ".grow", s => s.PaddingHorizontal(padding + grow))
                    .Rule(size + ".shrink", s => s.PaddingHorizontal(Mathf.Max(4f, padding - grow * 0.5f)));
            }
            ColorRules(sheet, "m3-button.filled", c.Primary, c.OnPrimary);
            ColorRules(sheet, "m3-button.tonal", c.SecondaryContainer, c.OnSecondaryContainer);
            ColorRules(sheet, "m3-button.elevated", c.SurfaceContainerLow, c.Primary);
            ColorRules(sheet, "m3-button.outlined", Colors.Transparent, c.OnSurfaceVariant);
            ColorRules(sheet, "m3-button.text", Colors.Transparent, c.Primary);
            sheet
                .Rule("m3-button.elevated", s => s.BoxShadow(Elevation.Level(1)))
                .Rule("m3-button.elevated:hover", s => s.BoxShadow(Elevation.Level(2)))
                .Rule("m3-button.outlined", s => s.Border(1, c.OutlineVariant))
                .Rule("m3-button.text", s => s.PaddingHorizontal(12));
            // Toggles: unselected, then selected.
            ColorRules(sheet, "m3-button.filled.toggle", c.SurfaceContainer, c.OnSurfaceVariant);
            ColorRules(sheet, "m3-button.filled.toggle.selected", c.Primary, c.OnPrimary);
            ColorRules(sheet, "m3-button.tonal.toggle.selected", c.Secondary, c.OnSecondary);
            ColorRules(sheet, "m3-button.elevated.toggle.selected", c.Primary, c.OnPrimary);
            ColorRules(sheet, "m3-button.outlined.toggle.selected", c.InverseSurface, c.InverseOnSurface);
            Disabled(sheet, "m3-button", c);
        }

        /// Container and content colors with their hover and pressed state layers.
        internal static void ColorRules(StyleSheet sheet, string selector, Color container, Color content)
        {
            sheet
                .Rule(selector, s => s.Background(container).TextColor(content))
                .Rule(selector + ":hover", s => s.Background(MaterialParts.Hover(container, content)))
                .Rule(selector + ":active", s => s.Background(MaterialParts.Pressed(container, content)));
        }

        internal static void Disabled(StyleSheet sheet, string tag, MaterialColorScheme c)
        {
            Color container = MaterialParts.Alpha(c.OnSurface, StateLayer.DisabledContainer);
            Color content = MaterialParts.Alpha(c.OnSurface, StateLayer.DisabledContent);
            sheet.Rule(tag + ":disabled, " + tag + ".toggle:disabled, " + tag + ".selected:disabled",
                s => s.Background(container).TextColor(content).BoxShadow());
        }

        // ---- icon buttons -----------------------------------------------------------------

        internal static Box IconButton(MaterialDesign m3, string icon, IconButtonStyle style, ComponentSize size,
            IconButtonWidth width, ButtonShape shape, Signal<bool> selected)
        {
            m3.Ensure(MaterialStyles.IconButton);
            MenUIContext ui = m3.ui;
            Box button = ui.Div().Tag("m3-icon-button").Class(IconStyleClass(style)).Class(MaterialParts.SizeClass(size));
            if (width == IconButtonWidth.Narrow) button.Class("narrow");
            else if (width == IconButtonWidth.Wide) button.Class("wide");
            if (shape == ButtonShape.Square) button.Class("square");
            TextElement iconText = MaterialParts.Icon(m3, icon, 0f, false);
            button.Children(MaterialParts.Ripple(m3, button), iconText);
            if (selected != null)
            {
                button.Class("toggle").Class("selected", () => selected.Value)
                    .OnClick(() => selected.Value = !selected.Peek());
                iconText.Class("filled", () => selected.Value);
            }
            return button;
        }

        private static string IconStyleClass(IconButtonStyle style)
        {
            if (style == IconButtonStyle.Filled) return "filled";
            if (style == IconButtonStyle.Tonal) return "tonal";
            if (style == IconButtonStyle.Outlined) return "outlined";
            return "standard";
        }

        internal static void IconButtonRules(StyleSheet sheet, MaterialColorScheme c, MotionScheme motion)
        {
            sheet.Rule("m3-icon-button", s => s.Center().FlexShrink(0).Position(PositionMode.Relative)
                .Transition(TransitionGroup.Corners, motion.FastSpatial)
                .Transition(TransitionGroup.Colors, motion.DefaultEffects)
                .Transition(StyleProperty.Width, motion.FastSpatial));
            for (int i = 0; i < SizeCount; i++)
            {
                string size = "m3-icon-button." + MaterialParts.SizeClass((ComponentSize)i);
                ComponentSize component = (ComponentSize)i;
                float height = MaterialParts.BySize(component, 32f, 40f, 56f, 96f, 136f);
                float iconSize = MaterialParts.BySize(component, 20f, 24f, 24f, 32f, 40f);
                float narrow = MaterialParts.BySize(component, 28f, 32f, 48f, 64f, 104f);
                float wide = MaterialParts.BySize(component, 40f, 52f, 72f, 128f, 184f);
                float square = MaterialParts.BySize(component, 12f, 12f, 16f, 28f, 28f);
                float pressed = MaterialParts.BySize(component, 8f, 8f, 12f, 16f, 16f);
                float round = height * 0.5f;
                float grow = MaterialParts.BySize(component, 8f, 10f, 14f, 20f, 28f);
                sheet
                    .Rule(size, s => s.Size(height, height).BorderRadius(round).FontSize(iconSize))
                    .Rule(size + ".narrow", s => s.Width(narrow))
                    .Rule(size + ".wide", s => s.Width(wide))
                    .Rule(size + ".square", s => s.BorderRadius(square))
                    .Rule(size + ".toggle.selected", s => s.BorderRadius(square))
                    .Rule(size + ".square.toggle.selected", s => s.BorderRadius(round))
                    .Rule(size + ":active", s => s.BorderRadius(pressed))
                    .Rule(size + ".selected:active", s => s.BorderRadius(pressed))
                    .Rule(size + ".grow", s => s.Width(height + grow))
                    .Rule(size + ".shrink", s => s.Width(height - grow * 0.5f));
            }
            ColorRules(sheet, "m3-icon-button.standard", Colors.Transparent, c.OnSurfaceVariant);
            ColorRules(sheet, "m3-icon-button.standard.toggle.selected", Colors.Transparent, c.Primary);
            ColorRules(sheet, "m3-icon-button.filled", c.Primary, c.OnPrimary);
            ColorRules(sheet, "m3-icon-button.filled.toggle", c.SurfaceContainer, c.OnSurfaceVariant);
            ColorRules(sheet, "m3-icon-button.filled.toggle.selected", c.Primary, c.OnPrimary);
            ColorRules(sheet, "m3-icon-button.tonal", c.SecondaryContainer, c.OnSecondaryContainer);
            ColorRules(sheet, "m3-icon-button.tonal.toggle.selected", c.Secondary, c.OnSecondary);
            ColorRules(sheet, "m3-icon-button.outlined", Colors.Transparent, c.OnSurfaceVariant);
            ColorRules(sheet, "m3-icon-button.outlined.toggle.selected", c.InverseSurface, c.InverseOnSurface);
            sheet.Rule("m3-icon-button.outlined", s => s.Border(1, c.OutlineVariant))
                .Rule("m3-icon-button.outlined.toggle.selected", s => s.BorderColor(c.InverseSurface));
            Disabled(sheet, "m3-icon-button", c);
        }

        // ---- FABs ----------------------------------------------------------------------------

        internal static Box Fab(MaterialDesign m3, string icon, FabSize size, FabColor color)
        {
            m3.Ensure(MaterialStyles.Fab);
            Box fab = m3.ui.Div().Tag("m3-fab").Class(FabSizeClass(size)).Class(FabColorClass(color));
            fab.Children(MaterialParts.Ripple(m3, fab), MaterialParts.Icon(m3, icon, 0f, false));
            return fab;
        }

        internal static Box ExtendedFab(MaterialDesign m3, string label, string icon, FabSize size, FabColor color,
            Func<bool> expanded)
        {
            m3.Ensure(MaterialStyles.Fab);
            MenUIContext ui = m3.ui;
            Box fab = ui.Div().Tag("m3-fab").Class("extended").Class(FabSizeClass(size)).Class(FabColorClass(color));
            // The label slides open and shut (its maximum width animates).
            Box labelBox = ui.Div().Tag("m3-fab-label").Child(ui.Text(label));
            if (expanded != null)
            {
                fab.Class("collapsed", () => !expanded());
            }
            fab.Children(MaterialParts.Ripple(m3, fab), MaterialParts.Icon(m3, icon, 0f, false), labelBox);
            return fab;
        }

        private static string FabSizeClass(FabSize size)
        {
            if (size == FabSize.Medium) return "medium";
            if (size == FabSize.Large) return "large";
            return "regular";
        }

        private static string FabColorClass(FabColor color)
        {
            if (color == FabColor.SecondaryContainer) return "secondary-container";
            if (color == FabColor.TertiaryContainer) return "tertiary-container";
            if (color == FabColor.Primary) return "primary";
            if (color == FabColor.Secondary) return "secondary";
            if (color == FabColor.Tertiary) return "tertiary";
            return "primary-container";
        }

        internal static void FabRules(StyleSheet sheet, MaterialColorScheme c, MotionScheme motion)
        {
            sheet
                .Rule("m3-fab", s => s.Row().AlignItems(Align.Center).JustifyContent(Justify.Center).FlexShrink(0)
                    .Width(Length.FitContent).Position(PositionMode.Relative).BoxShadow(Elevation.Level(3))
                    .Transition(TransitionGroup.Corners, motion.FastSpatial)
                    .Transition(TransitionGroup.Colors, motion.DefaultEffects)
                    .Transition(TransitionGroup.Spacing, motion.DefaultSpatial))
                .Rule("m3-fab:hover", s => s.BoxShadow(Elevation.Level(4)))
                .Rule("m3-fab.regular", s => s.Size(56, 56).BorderRadius(16).FontSize(24))
                .Rule("m3-fab.medium", s => s.Size(80, 80).BorderRadius(20).FontSize(28))
                .Rule("m3-fab.large", s => s.Size(96, 96).BorderRadius(28).FontSize(36))
                .Rule("m3-fab.regular:active", s => s.BorderRadius(12))
                .Rule("m3-fab.medium:active", s => s.BorderRadius(16))
                .Rule("m3-fab.large:active", s => s.BorderRadius(20))
                // Extended: icon and label; the label's width animates when collapsing.
                .Rule("m3-fab.extended", s => s.Width(Length.FitContent))
                .Rule("m3-fab.extended.regular", s => s.Width(Length.FitContent).PaddingHorizontal(16).Gap(12))
                .Rule("m3-fab.extended.medium", s => s.Width(Length.FitContent).PaddingHorizontal(26).Gap(12))
                .Rule("m3-fab.extended.large", s => s.Width(Length.FitContent).PaddingHorizontal(28).Gap(16))
                .Rule("m3-fab-label", s => s.Row().MaxWidth(360).OverflowHidden().Opacity(1f).FlexShrink(0)
                    .Transition(StyleProperty.MaxWidth, motion.DefaultSpatial).Transition(StyleProperty.Opacity, motion.FastEffects))
                .Rule("m3-fab-label > text", s => s.FlexShrink(0))   // never wraps while it narrows
                .Rule("m3-fab.regular m3-fab-label", s => s.FontSize(16))
                .Rule("m3-fab.medium m3-fab-label", s => s.FontSize(22))
                .Rule("m3-fab.large m3-fab-label", s => s.FontSize(24))
                .Rule("m3-fab.collapsed", s => s.Gap(0))
                .Rule("m3-fab.collapsed m3-fab-label", s => s.MaxWidth(0).Opacity(0f))
                .Rule("m3-fab.collapsed.regular", s => s.PaddingHorizontal(16))
                // The FAB menu's button: open, it is a circle in the primary color.
                .Rule("m3-fab.menu-open", s => s.BorderRadius(28));
            ColorRules(sheet, "m3-fab.primary-container", c.PrimaryContainer, c.OnPrimaryContainer);
            ColorRules(sheet, "m3-fab.secondary-container", c.SecondaryContainer, c.OnSecondaryContainer);
            ColorRules(sheet, "m3-fab.tertiary-container", c.TertiaryContainer, c.OnTertiaryContainer);
            ColorRules(sheet, "m3-fab.primary", c.Primary, c.OnPrimary);
            ColorRules(sheet, "m3-fab.secondary", c.Secondary, c.OnSecondary);
            ColorRules(sheet, "m3-fab.tertiary", c.Tertiary, c.OnTertiary);
            ColorRules(sheet, "m3-fab.menu-open", c.Primary, c.OnPrimary);

            // FAB menu items: pills that spring in from the FAB.
            sheet
                .Rule("m3-fab-menu", s => s.Column().AlignItems(Align.End).Gap(16).Width(Length.FitContent))
                .Rule("m3-fab-menu-items", s => s.Column().AlignItems(Align.End).Gap(4).PointerEvents(PointerEventsMode.None))
                .Rule("m3-fab-menu.open > m3-fab-menu-items", s => s.PointerEvents(PointerEventsMode.Auto))
                .Rule("m3-fab-menu-item", s => s.Row().AlignItems(Align.Center).Gap(8).Height(56).PaddingHorizontal(24)
                    .Width(Length.FitContent).BorderRadius(28).FontSize(16).Position(PositionMode.Relative)
                    .Transition(StyleProperty.Opacity, motion.FastEffects).Transition(TransitionGroup.Transform, motion.FastSpatial)
                    .Transition(TransitionGroup.Colors, motion.DefaultEffects))
                .Rule("m3-fab-menu-item > m3-icon", s => s.FontSize(24))
                .Rule("m3-fab-menu-item", s => s.Opacity(0f).TranslateY(24).Scale(0.85f))
                .Rule("m3-fab-menu.open m3-fab-menu-item", s => s.Opacity(1f).TranslateY(0).Scale(1f));
            ColorRules(sheet, "m3-fab-menu-item", c.PrimaryContainer, c.OnPrimaryContainer);
        }

        internal static Box FabMenu(MaterialDesign m3, Signal<bool> open, string icon, FabMenuItem[] items)
        {
            m3.Ensure(MaterialStyles.Fab);
            MenUIContext ui = m3.ui;
            Box fab = ui.Div().Tag("m3-fab").Class("regular").Class("primary-container")
                .Class("menu-open", () => open.Value)
                .OnClick(() => open.Value = !open.Peek());
            TextElement fabIcon = ui.Text(() => open.Value ? MaterialSymbol.Close : icon).Tag("m3-icon");
            fab.Children(MaterialParts.Ripple(m3, fab), fabIcon);

            // The items always keep their place (so the FAB never moves, and
            // opening changes no layout): closed, they are transparent, shrunk
            // and out of reach; they spring in one after another.
            Box column = ui.Div().Tag("m3-fab-menu-items");
            for (int i = 0; i < items.Length; i++)
            {
                FabMenuItem item = items[i];
                // The item nearest the FAB comes first.
                float delay = (items.Length - 1 - i) * 0.03f;
                Box row = ui.Div().Tag("m3-fab-menu-item")
                    .Transition(StyleProperty.Opacity, m3.Motion.FastEffects.Delay(delay))
                    .Transition(TransitionGroup.Transform, m3.Motion.FastSpatial.Delay(delay));
                row.Children(MaterialParts.Ripple(m3, row), MaterialParts.Icon(m3, item.Icon, 0f, false), ui.Text(item.Label))
                    .OnClick(() =>
                    {
                        open.Value = false;
                        if (item.OnClick != null)
                        {
                            item.OnClick();
                        }
                    });
                column.Child(row);
            }
            return ui.Div().Tag("m3-fab-menu").Class("open", () => open.Value).Children(column, fab);
        }

        // ---- groups ----------------------------------------------------------------------------

        internal static Box ButtonGroup(MaterialDesign m3, ButtonGroupKind kind, Element[] buttons)
        {
            m3.Ensure(MaterialStyles.ButtonGroup);
            MenUIContext ui = m3.ui;
            Box group = ui.Div().Tag("m3-button-group").Class(kind == ButtonGroupKind.Connected ? "connected" : "standard");
            Signal<int> pressed = ui.Signal(-1);
            for (int i = 0; i < buttons.Length; i++)
            {
                Element button = buttons[i];
                int index = i;
                if (kind == ButtonGroupKind.Standard)
                {
                    button.Class("grow", () => pressed.Value == index)
                        .Class("shrink", () => pressed.Value >= 0 && Mathf.Abs(pressed.Value - index) == 1);
                    button.OnPointerDown(e => pressed.Value = index);
                    button.OnPointerUp(e => pressed.Value = -1);
                    button.OnPointerLeave(e => { if (pressed.Peek() == index) pressed.Value = -1; });
                }
                else
                {
                    button.Class(i == 0 ? "first" : i == buttons.Length - 1 ? "last" : "middle");
                    if (buttons.Length == 1) button.Class("last");
                }
                group.Child(button);
            }
            return group;
        }

        internal static Box SegmentedGroup(MaterialDesign m3, Signal<int> selected, string[] labels, string[] icons,
            ComponentSize size)
        {
            MenUIContext ui = m3.ui;
            Element[] buttons = new Element[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                Box button = Button(m3, labels[i], icons != null && i < icons.Length ? icons[i] : null,
                    ButtonStyle.Filled, size, ButtonShape.Round, null);
                button.Class("toggle").Class("selected", () => selected.Value == index)
                    .OnClick(() => selected.Value = index);
                buttons[i] = button;
            }
            return ButtonGroup(m3, ButtonGroupKind.Connected, buttons);
        }

        internal static void GroupRules(StyleSheet sheet, MaterialColorScheme c, MotionScheme motion)
        {
            sheet
                .Rule("m3-button-group", s => s.Row().AlignItems(Align.Center).Width(Length.FitContent))
                .Rule("m3-button-group.standard", s => s.Gap(8))
                .Rule("m3-button-group.connected", s => s.Gap(2));
            // Connected: inner corners small, outer ones round; the selected one fully round.
            for (int i = 0; i < SizeCount; i++)
            {
                ComponentSize component = (ComponentSize)i;
                string prefix = "m3-button-group.connected > m3-button." + MaterialParts.SizeClass((ComponentSize)i);
                float round = MaterialParts.BySize(component, 32f, 40f, 56f, 96f, 136f) * 0.5f;
                float inner = MaterialParts.BySize(component, 4f, 8f, 8f, 16f, 20f);
                float pressedInner = MaterialParts.BySize(component, 8f, 12f, 12f, 24f, 28f);
                sheet
                    .Rule(prefix + ".first", s => s.BorderRadius(round, inner, inner, round))
                    .Rule(prefix + ".middle", s => s.BorderRadius(inner))
                    .Rule(prefix + ".last", s => s.BorderRadius(inner, round, round, inner))
                    .Rule(prefix + ".first:active", s => s.BorderRadius(round, pressedInner, pressedInner, round))
                    .Rule(prefix + ".middle:active", s => s.BorderRadius(pressedInner))
                    .Rule(prefix + ".last:active", s => s.BorderRadius(pressedInner, round, round, pressedInner))
                    .Rule(prefix + ".first.selected, " + prefix + ".middle.selected, " + prefix + ".last.selected",
                        s => s.BorderRadius(round));
            }
            // The split button: two halves, the inner corners small.
            for (int i = 0; i < SizeCount; i++)
            {
                ComponentSize component = (ComponentSize)i;
                float round = MaterialParts.BySize(component, 32f, 40f, 56f, 96f, 136f) * 0.5f;
                float inner = MaterialParts.BySize(component, 4f, 4f, 4f, 8f, 12f);
                float pressedInner = MaterialParts.BySize(component, 8f, 8f, 12f, 16f, 20f);
                float trailing = MaterialParts.BySize(component, 34f, 42f, 56f, 88f, 116f);
                string size = MaterialParts.SizeClass((ComponentSize)i);
                sheet
                    .Rule("m3-button.split-leading." + size, s => s.BorderRadius(round, inner, inner, round))
                    .Rule("m3-button.split-leading." + size + ":active", s => s.BorderRadius(round, pressedInner, pressedInner, round))
                    .Rule("m3-button.split-trailing." + size, s => s.BorderRadius(inner, round, round, inner)
                        .Width(trailing).PaddingHorizontal(0))
                    .Rule("m3-button.split-trailing." + size + ":active", s => s.BorderRadius(pressedInner, round, round, pressedInner))
                    .Rule("m3-button.split-trailing." + size + ".selected", s => s.BorderRadius(round));
            }
        }

        internal static Box SplitButton(MaterialDesign m3, string label, string icon, Action onClick, Signal<bool> menuOpen,
            ButtonStyle style, ComponentSize size)
        {
            m3.Ensure(MaterialStyles.ButtonGroup);
            MenUIContext ui = m3.ui;
            Box leading = Button(m3, label, icon, style, size, ButtonShape.Round, null).Class("split-leading");
            if (onClick != null)
            {
                leading.OnClick(onClick);
            }
            Box trailing = Button(m3, null, null, style, size, ButtonShape.Round, null).Class("split-trailing")
                .Class("selected", () => menuOpen.Value)
                .OnClick(() => menuOpen.Value = !menuOpen.Peek());
            trailing.Child(ui.Text(() => menuOpen.Value ? MaterialSymbol.KeyboardArrowUp : MaterialSymbol.KeyboardArrowDown)
                .Tag("m3-icon"));
            return ui.Div().Tag("m3-button-group").Gap(2).Children(leading, trailing);
        }
    }
}
