// Material 3 navigation: tabs (a sliding indicator), the navigation bar,
// the M3 Expressive navigation rail (narrow ↔ wide, animated) and top app
// bars.

using System;
using UnityEngine;

namespace MenUI.Material3
{
    public enum TopAppBarStyle { Small, CenterAligned, Medium, Large }

    internal static class MaterialNavigation
    {
        // ---- tabs ---------------------------------------------------------------------------

        internal static Box Tabs(MaterialDesign m3, Signal<int> selected, string[] labels, string[] icons, bool secondary)
        {
            m3.Ensure(MaterialStyles.Navigation);
            MenUIContext ui = m3.ui;
            Box bar = ui.Div().Tag("m3-tabs").Class(secondary ? "secondary" : "primary");
            if (icons != null)
            {
                bar.Class("with-icons");
            }
            Box[] tabs = new Box[labels.Length];
            TextElement[] texts = new TextElement[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                Box tab = ui.Div().Tag("m3-tab").Class("selected", () => selected.Value == index)
                    .OnClick(() => selected.Value = index);
                tab.Child(MaterialParts.Ripple(m3, tab));
                if (icons != null && i < icons.Length)
                {
                    tab.Child(MaterialParts.Icon(m3, icons[i], 24f, false).Class("filled", () => selected.Value == index));
                }
                texts[i] = ui.Text(labels[i]);
                tab.Child(texts[i]);
                tabs[i] = tab;
                bar.Child(tab);
            }
            // The indicator follows the selected tab (its label, for primary
            // tabs), sliding with a spring once it has a place.
            Box indicator = ui.Div().Tag("m3-tab-indicator");
            bool placed = false;
            indicator.Bind(e =>
            {
                if (ui.layoutTick.Value < 0)
                {
                    return;
                }
                int index = Mathf.Clamp(selected.Value, 0, tabs.Length - 1);
                Element target = secondary ? (Element)tabs[index] : texts[index];
                if (target.width <= 0 || bar.width <= 0)
                {
                    return;
                }
                float scale = ui.scale;
                float width = Mathf.Max(target.width / scale, secondary ? 0f : 24f);
                float left = (target.x - bar.x) / scale + (target.width / scale - width) * 0.5f;
                e.Left(left).Width(width);
                if (!placed)
                {
                    placed = true;
                    e.SetClass("placed", true);
                }
            });
            bar.Child(indicator);
            return bar;
        }

        // ---- navigation bar ---------------------------------------------------------------

        internal static Box NavigationBar(MaterialDesign m3, Signal<int> selected, string[] labels, string[] icons)
        {
            m3.Ensure(MaterialStyles.Navigation);
            MenUIContext ui = m3.ui;
            Box bar = ui.Div().Tag("m3-navigation-bar");
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                Func<bool> isSelected = () => selected.Value == index;
                Box pill = ui.Div().Tag("m3-nav-indicator").Class("selected", isSelected);
                pill.Children(MaterialParts.Ripple(m3, pill),
                    MaterialParts.Icon(m3, icons[i], 24f, false).Class("filled", isSelected));
                Box item = ui.Div().Tag("m3-nav-item").Class("selected", isSelected)
                    .OnClick(() => selected.Value = index)
                    .Children(pill, ui.Text(labels[i]).Tag("m3-nav-label"));
                bar.Child(item);
            }
            return bar;
        }

        // ---- navigation rail -------------------------------------------------------------------

        internal static Box NavigationRail(MaterialDesign m3, Signal<int> selected, Signal<bool> expanded, string[] labels,
            string[] icons, Element fab)
        {
            m3.Ensure(MaterialStyles.Navigation);
            MenUIContext ui = m3.ui;
            Box rail = ui.Div().Tag("m3-rail").Class("expanded", () => expanded.Value);
            Box menu = MaterialButtons.IconButton(m3, MaterialSymbol.Menu, IconButtonStyle.Standard, ComponentSize.Small,
                IconButtonWidth.Default, ButtonShape.Round, null).Class("rail-menu");
            menu.OnClick(() => expanded.Value = !expanded.Peek());
            // The menu icon turns into "menu open" when expanded.
            TextElement menuIcon = (TextElement)menu.children[1];
            ui.Bind(() => menuIcon.SetContent(expanded.Value ? MaterialSymbol.MenuOpen : MaterialSymbol.Menu));
            rail.Child(menu);
            if (fab != null)
            {
                rail.Child(ui.Div().Tag("m3-rail-fab").Child(fab));
            }
            Box items = ui.Div().Tag("m3-rail-items");
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                Func<bool> isSelected = () => selected.Value == index;
                // The pill holds the icon (and, wide, the label); narrow, the
                // label sits under it. Both labels are there; the unused one
                // is folded away, so switching animates.
                Box pill = ui.Div().Tag("m3-rail-indicator").Class("selected", isSelected);
                pill.Children(MaterialParts.Ripple(m3, pill),
                    MaterialParts.Icon(m3, icons[i], 24f, false).Class("filled", isSelected),
                    ui.Div().Tag("m3-rail-inline-label").Child(ui.Text(labels[i])));
                Box item = ui.Div().Tag("m3-rail-item").Class("selected", isSelected)
                    .OnClick(() => selected.Value = index)
                    .Children(pill, ui.Div().Tag("m3-rail-label").Child(ui.Text(labels[i])));
                items.Child(item);
            }
            return rail.Child(items);
        }

        // ---- top app bar ---------------------------------------------------------------------

        internal static Box TopAppBar(MaterialDesign m3, string title, string navigationIcon, Action onNavigation,
            TopAppBarStyle style, Element[] actions)
        {
            m3.Ensure(MaterialStyles.Navigation);
            m3.Ensure(MaterialStyles.IconButton);
            MenUIContext ui = m3.ui;
            Box row = ui.Div().Tag("m3-app-bar-row");
            if (!string.IsNullOrEmpty(navigationIcon))
            {
                Box navigation = MaterialButtons.IconButton(m3, navigationIcon, IconButtonStyle.Standard, ComponentSize.Small,
                    IconButtonWidth.Default, ButtonShape.Round, null);
                if (onNavigation != null)
                {
                    navigation.OnClick(onNavigation);
                }
                row.Child(navigation);
            }
            TextElement heading = ui.Text(title).Tag("m3-app-bar-title");
            bool inRow = style == TopAppBarStyle.Small || style == TopAppBarStyle.CenterAligned;
            if (inRow)
            {
                row.Child(heading);
            }
            else
            {
                row.Child(ui.Div().FlexGrow(1));
            }
            row.Child(ui.Div().Tag("m3-app-bar-actions").Children(actions));
            Box bar = ui.Div().Tag("m3-app-bar").Class(style == TopAppBarStyle.CenterAligned ? "center"
                : style == TopAppBarStyle.Medium ? "medium" : style == TopAppBarStyle.Large ? "large" : "small").Child(row);
            if (!inRow)
            {
                bar.Child(heading);
            }
            return bar;
        }

        internal static void Rules(StyleSheet sheet, MaterialColorScheme c, MotionScheme motion)
        {
            AnimationSpec spatial = motion.FastSpatial;
            AnimationSpec effects = motion.DefaultEffects;
            sheet
                // Tabs.
                .Rule("m3-tabs", s => s.Row().Position(PositionMode.Relative).BorderWidth(0, 0, 1, 0).BorderColor(c.OutlineVariant))
                .Rule("m3-tab", s => s.Column().AlignItems(Align.Center).JustifyContent(Justify.Center).Gap(2)
                    .FlexGrow(1).FlexBasis(0).Height(48).PaddingHorizontal(16).Position(PositionMode.Relative)
                    .FontSize(TypeScale.Size(TypeRole.TitleSmall)).Transition(TransitionGroup.Colors, effects))
                .Rule("m3-tabs.with-icons > m3-tab", s => s.Height(64))
                .Rule("m3-tab-indicator", s => s.Absolute().Bottom(0).Height(3).BorderRadius(3, 3, 0, 0).Background(c.Primary)
                    .PointerEvents(PointerEventsMode.None))
                .Rule("m3-tab-indicator.placed", s => s.Transition(TransitionGroup.Position, spatial)
                    .Transition(StyleProperty.Width, spatial))
                .Rule("m3-tabs.secondary > m3-tab-indicator", s => s.Height(2).BorderRadius(0));
            MaterialButtons.ColorRules(sheet, "m3-tab", Colors.Transparent, c.OnSurfaceVariant);
            MaterialButtons.ColorRules(sheet, "m3-tab.selected", Colors.Transparent, c.Primary);
            MaterialButtons.ColorRules(sheet, "m3-tabs.secondary > m3-tab.selected", Colors.Transparent, c.OnSurface);

            sheet
                // Navigation bar: icons in pills that open out when selected.
                .Rule("m3-navigation-bar", s => s.Row().Height(80).Background(c.SurfaceContainer).PaddingVertical(12))
                .Rule("m3-nav-item", s => s.Column().AlignItems(Align.Center).Gap(4).FlexGrow(1).FlexBasis(0)
                    .TextColor(c.OnSurfaceVariant).Transition(TransitionGroup.Colors, effects))
                .Rule("m3-nav-item.selected", s => s.TextColor(c.OnSurface))
                .Rule("m3-nav-indicator", s => s.Center().Width(32).Height(32).BorderRadius(16).Position(PositionMode.Relative)
                    .TextColor(c.OnSurfaceVariant)
                    .Transition(StyleProperty.Width, spatial).Transition(TransitionGroup.Colors, effects))
                .Rule("m3-nav-indicator.selected", s => s.Width(56).Background(c.SecondaryContainer).TextColor(c.OnSecondaryContainer))
                .Rule("m3-nav-label", s => s.FontSize(TypeScale.Size(TypeRole.LabelMedium)))
                // Navigation rail (M3 Expressive): 96 px narrow, 220 px wide.
                .Rule("m3-rail", s => s.Column().Gap(4).Width(96).FlexShrink(0).PaddingVertical(16).OverflowHidden()
                    .Background(c.Surface).TextColor(c.OnSurfaceVariant).Transition(StyleProperty.Width, motion.DefaultSpatial))
                .Rule("m3-rail.expanded", s => s.Width(220))
                .Rule("m3-icon-button.rail-menu", s => s.MarginLeft(28))
                .Rule("m3-rail-fab", s => s.MarginLeft(20).MarginVertical(8).Width(Length.FitContent))
                .Rule("m3-rail-items", s => s.Column().Gap(4).MarginTop(24))
                .Rule("m3-rail-item", s => s.Column().AlignItems(Align.Center).Gap(4).MarginLeft(20).Width(Length.FitContent)
                    .MinWidth(56).PaddingVertical(4).Transition(TransitionGroup.Colors, effects))
                .Rule("m3-rail-item.selected", s => s.TextColor(c.OnSurface))
                .Rule("m3-rail-indicator", s => s.Row().AlignItems(Align.Center).Height(32).PaddingHorizontal(16)
                    .BorderRadius(16).Position(PositionMode.Relative).Width(Length.FitContent)
                    .Transition(StyleProperty.Height, spatial).Transition(TransitionGroup.Colors, effects)
                    .Transition(TransitionGroup.Corners, spatial))
                .Rule("m3-rail.expanded m3-rail-indicator", s => s.Height(56).BorderRadius(28))
                .Rule("m3-rail-indicator.selected", s => s.Background(c.SecondaryContainer).TextColor(c.OnSecondaryContainer))
                .Rule("m3-rail-inline-label", s => s.Row().MaxWidth(0).OverflowHidden().Opacity(0f).MarginLeft(0)
                    .FontSize(TypeScale.Size(TypeRole.LabelLarge))
                    .Transition(StyleProperty.MaxWidth, spatial).Transition(StyleProperty.Opacity, effects)
                    .Transition(StyleProperty.MarginLeft, spatial))
                .Rule("m3-rail-inline-label > text", s => s.FlexShrink(0))
                .Rule("m3-rail.expanded m3-rail-inline-label", s => s.MaxWidth(160).Opacity(1f).MarginLeft(8))
                .Rule("m3-rail-label", s => s.Row().MaxHeight(24).OverflowHidden().Opacity(1f)
                    .FontSize(TypeScale.Size(TypeRole.LabelMedium))
                    .Transition(StyleProperty.MaxHeight, spatial).Transition(StyleProperty.Opacity, effects))
                .Rule("m3-rail.expanded m3-rail-label", s => s.MaxHeight(0).Opacity(0f))
                // Top app bars.
                .Rule("m3-app-bar", s => s.Column().Background(c.Surface).TextColor(c.OnSurface).PaddingHorizontal(4))
                .Rule("m3-app-bar-row", s => s.Row().AlignItems(Align.Center).Gap(4).Height(64))
                .Rule("m3-app-bar-title", s => s.FlexGrow(1).MinWidth(0).MarginLeft(12).FontSize(TypeScale.Size(TypeRole.TitleLarge)))
                .Rule("m3-app-bar.center m3-app-bar-title", s => s.TextAlign(TextAlignMode.Center))
                .Rule("m3-app-bar.medium > m3-app-bar-title", s => s.Padding(0, 12, 20, 12).FontSize(TypeScale.Size(TypeRole.HeadlineSmall)))
                .Rule("m3-app-bar.large > m3-app-bar-title", s => s.Padding(24, 12, 24, 12).FontSize(TypeScale.Size(TypeRole.HeadlineMedium)))
                .Rule("m3-app-bar-actions", s => s.Row().AlignItems(Align.Center).Gap(0));
            MaterialButtons.ColorRules(sheet, "m3-rail-indicator", Colors.Transparent, c.OnSurfaceVariant);
            MaterialButtons.ColorRules(sheet, "m3-rail-indicator.selected", c.SecondaryContainer, c.OnSecondaryContainer);
        }
    }
}
