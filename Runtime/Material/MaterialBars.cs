// Material 3 Expressive toolbars: the floating toolbar (a pill of controls,
// standard or vibrant) and the docked toolbar, with items whose selection
// widens into a filled pill.

using System;
using UnityEngine;

namespace MenUI.Material3
{
    internal static class MaterialBars
    {
        internal static Box FloatingToolbar(MaterialDesign m3, bool vibrant, Element[] items)
        {
            m3.Ensure(MaterialStyles.Toolbar);
            Box bar = m3.ui.Div().Tag("m3-toolbar").Class("floating");
            if (vibrant)
            {
                bar.Class("vibrant");
            }
            return bar.Children(items);
        }

        internal static Box DockedToolbar(MaterialDesign m3, Element[] items)
        {
            m3.Ensure(MaterialStyles.Toolbar);
            return m3.ui.Div().Tag("m3-toolbar").Class("docked").Children(items);
        }

        internal static Box ToolbarItem(MaterialDesign m3, string icon, Func<bool> selected, Action onClick, bool vibrant)
        {
            m3.Ensure(MaterialStyles.Toolbar);
            Box item = m3.ui.Div().Tag("m3-toolbar-item");
            TextElement iconText = MaterialParts.Icon(m3, icon, 24f, false);
            item.Children(MaterialParts.Ripple(m3, item), iconText);
            if (selected != null)
            {
                item.Class("selected", selected);
                iconText.Class("filled", selected);
            }
            if (onClick != null)
            {
                item.OnClick(onClick);
            }
            return item;
        }

        internal static void Rules(StyleSheet sheet, MaterialColorScheme c, MotionScheme motion)
        {
            sheet
                .Rule("m3-toolbar", s => s.Row().AlignItems(Align.Center).Gap(4)
                    .Transition(TransitionGroup.Colors, motion.DefaultEffects))
                .Rule("m3-toolbar.floating", s => s.Width(Length.FitContent).Padding(8).BorderRadius(32)
                    .Background(c.SurfaceContainer).TextColor(c.OnSurfaceVariant).BoxShadow(Elevation.Level(3)))
                .Rule("m3-toolbar.floating.vibrant", s => s.Background(c.PrimaryContainer).TextColor(c.OnPrimaryContainer))
                .Rule("m3-toolbar.docked", s => s.Height(64).PaddingHorizontal(16).JustifyContent(Justify.SpaceAround)
                    .Background(c.SurfaceContainer).TextColor(c.OnSurfaceVariant))
                // Icon buttons follow the toolbar's content color.
                .Rule("m3-toolbar m3-icon-button.standard", s => s.TextColor(c.OnSurfaceVariant))
                .Rule("m3-toolbar.vibrant m3-icon-button.standard", s => s.TextColor(c.OnPrimaryContainer))
                .Rule("m3-toolbar-item", s => s.Center().Size(48, 48).FlexShrink(0).BorderRadius(24).Position(PositionMode.Relative)
                    .Transition(StyleProperty.Width, motion.FastSpatial).Transition(TransitionGroup.Colors, motion.DefaultEffects)
                    .Transition(TransitionGroup.Corners, motion.FastSpatial))
                .Rule("m3-toolbar-item:active", s => s.BorderRadius(12))
                .Rule("m3-toolbar-item.selected", s => s.Width(72));
            MaterialButtons.ColorRules(sheet, "m3-toolbar-item", Colors.Transparent, c.OnSurfaceVariant);
            MaterialButtons.ColorRules(sheet, "m3-toolbar-item.selected", c.Primary, c.OnPrimary);
            MaterialButtons.ColorRules(sheet, "m3-toolbar.vibrant m3-toolbar-item", Colors.Transparent, c.OnPrimaryContainer);
            MaterialButtons.ColorRules(sheet, "m3-toolbar.vibrant m3-toolbar-item.selected", c.SurfaceContainer, c.OnSurface);
        }
    }
}
