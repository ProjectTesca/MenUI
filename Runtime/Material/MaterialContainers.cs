// Material 3 containment and small parts: cards, chips, list items,
// dividers, badges.

using System;
using UnityEngine;

namespace MenUI.Material3
{
    public enum CardStyle { Elevated, Filled, Outlined }

    internal static class MaterialContainers
    {
        internal static Box Card(MaterialDesign m3, CardStyle style)
        {
            m3.Ensure(MaterialStyles.Containers);
            string kind = style == CardStyle.Elevated ? "elevated" : style == CardStyle.Outlined ? "outlined" : "filled";
            return m3.ui.Div().Tag("m3-card").Class(kind);
        }

        internal static Box Chip(MaterialDesign m3, string label, string icon, Signal<bool> selected, Action onRemove)
        {
            m3.Ensure(MaterialStyles.Containers);
            MenUIContext ui = m3.ui;
            Box chip = ui.Div().Tag("m3-chip");
            chip.Child(MaterialParts.Ripple(m3, chip));
            if (selected != null)
            {
                // A filter chip: a check slides in before the label when selected.
                chip.Class("filter").Class("selected", () => selected.Value).OnClick(() => selected.Value = !selected.Peek());
                chip.Child(ui.Div().Tag("m3-chip-check").Child(MaterialParts.Icon(m3, MaterialSymbol.Check, 18f, false)));
            }
            if (!string.IsNullOrEmpty(icon))
            {
                chip.Class("with-icon").Child(MaterialParts.Icon(m3, icon, 18f, false).Tag("m3-chip-icon"));
            }
            chip.Child(ui.Text(label));
            if (onRemove != null)
            {
                Box remove = ui.Div().Tag("m3-chip-remove").Child(MaterialParts.Icon(m3, MaterialSymbol.Close, 18f, false));
                remove.OnClick(e =>
                {
                    e.StopPropagation();
                    onRemove();
                });
                chip.Class("input").Child(remove);
            }
            return chip;
        }

        internal static Box ListItem(MaterialDesign m3, string headline, string supporting, string leadingIcon, string trailing)
        {
            m3.Ensure(MaterialStyles.Containers);
            MenUIContext ui = m3.ui;
            Box item = ui.Div().Tag("m3-list-item");
            item.Child(MaterialParts.Ripple(m3, item));
            if (!string.IsNullOrEmpty(leadingIcon))
            {
                item.Child(MaterialParts.Icon(m3, leadingIcon, 24f, false).Tag("m3-list-icon"));
            }
            Box texts = ui.Div().Tag("m3-list-texts").Child(ui.Text(headline).Tag("m3-list-headline"));
            if (!string.IsNullOrEmpty(supporting))
            {
                item.Class("two-line");
                texts.Child(ui.Text(supporting).Tag("m3-list-supporting"));
            }
            item.Child(texts);
            if (!string.IsNullOrEmpty(trailing))
            {
                item.Child(ui.Text(trailing).Tag("m3-list-trailing"));
            }
            return item;
        }

        internal static Box Divider(MaterialDesign m3)
        {
            m3.Ensure(MaterialStyles.Containers);
            return m3.ui.Div().Tag("m3-divider");
        }

        internal static Box Badge(MaterialDesign m3, Func<string> text)
        {
            m3.Ensure(MaterialStyles.Containers);
            Box badge = m3.ui.Div().Tag("m3-badge");
            if (text != null)
            {
                badge.Class("large").Child(m3.ui.Text(text));
            }
            return badge;
        }

        internal static void Rules(StyleSheet sheet, MaterialColorScheme c, MotionScheme motion)
        {
            sheet
                // Cards.
                .Rule("m3-card", s => s.Column().Gap(8).Padding(16).BorderRadius(ShapeScale.Medium).TextColor(c.OnSurface)
                    .Transition(TransitionGroup.Colors, motion.DefaultEffects))
                .Rule("m3-card.elevated", s => s.Background(c.SurfaceContainerLow).BoxShadow(Elevation.Level(1)))
                .Rule("m3-card.filled", s => s.Background(c.SurfaceContainerHighest))
                .Rule("m3-card.outlined", s => s.Background(c.Surface).Border(1, c.OutlineVariant))
                // Chips.
                .Rule("m3-chip", s => s.Row().AlignItems(Align.Center).Gap(8).Height(32).PaddingHorizontal(16)
                    .Width(Length.FitContent).FlexShrink(0).BorderRadius(ShapeScale.Small).Border(1, c.OutlineVariant)
                    .Position(PositionMode.Relative).FontSize(TypeScale.Size(TypeRole.LabelLarge))
                    .Transition(TransitionGroup.Colors, motion.DefaultEffects).Transition(TransitionGroup.Spacing, motion.FastSpatial))
                .Rule("m3-chip.with-icon, m3-chip.filter.selected", s => s.PaddingLeft(8))
                .Rule("m3-chip.input", s => s.PaddingRight(8))
                .Rule("m3-chip-icon", s => s.TextColor(c.Primary))
                .Rule("m3-chip-check", s => s.Row().MaxWidth(0).OverflowHidden().Opacity(0f)
                    .Transition(StyleProperty.MaxWidth, motion.FastSpatial).Transition(StyleProperty.Opacity, motion.FastEffects))
                .Rule("m3-chip-check > m3-icon", s => s.FlexShrink(0))
                .Rule("m3-chip.selected > m3-chip-check", s => s.MaxWidth(24).Opacity(1f))
                .Rule("m3-chip.filter", s => s.Gap(0))
                .Rule("m3-chip.filter > text", s => s.MarginLeft(0))
                .Rule("m3-chip.filter.selected", s => s.Gap(8).BorderColor(MaterialParts.Alpha(c.OutlineVariant, 0f)))
                .Rule("m3-chip-remove", s => s.Center().Size(24, 24).BorderRadius(12))
                .Rule("m3-chip-remove:hover", s => s.Background(MaterialParts.Alpha(c.OnSurfaceVariant, StateLayer.Hover)))
                // Lists.
                .Rule("m3-list-item", s => s.Row().AlignItems(Align.Center).Gap(16).MinHeight(56).Padding(8, 24, 8, 16)
                    .Position(PositionMode.Relative).TextColor(c.OnSurface)
                    .Transition(TransitionGroup.Colors, motion.DefaultEffects))
                .Rule("m3-list-item.two-line", s => s.MinHeight(72))
                .Rule("m3-list-icon", s => s.TextColor(c.OnSurfaceVariant))
                .Rule("m3-list-texts", s => s.Column().FlexGrow(1).MinWidth(0))
                .Rule("m3-list-headline", s => s.FontSize(TypeScale.Size(TypeRole.BodyLarge)))
                .Rule("m3-list-supporting", s => s.FontSize(TypeScale.Size(TypeRole.BodyMedium)).TextColor(c.OnSurfaceVariant))
                .Rule("m3-list-trailing", s => s.FontSize(TypeScale.Size(TypeRole.LabelSmall)).TextColor(c.OnSurfaceVariant))
                // Dividers and badges.
                .Rule("m3-divider", s => s.Height(1).FlexShrink(0).Background(c.OutlineVariant))
                .Rule("m3-badge", s => s.Size(6, 6).FlexShrink(0).BorderRadius(3).Background(c.Error))
                .Rule("m3-badge.large", s => s.Center().Width(Length.FitContent).MinWidth(16).Height(16).PaddingHorizontal(4)
                    .BorderRadius(8).TextColor(c.OnError).FontSize(TypeScale.Size(TypeRole.LabelSmall)));
            MaterialButtons.ColorRules(sheet, "m3-chip", Colors.Transparent, c.OnSurfaceVariant);
            MaterialButtons.ColorRules(sheet, "m3-chip.selected", c.SecondaryContainer, c.OnSecondaryContainer);
            MaterialButtons.ColorRules(sheet, "m3-list-item", Colors.Transparent, c.OnSurface);
        }
    }
}
