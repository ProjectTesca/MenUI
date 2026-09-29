// Material 3 overlays: menus, dialogs, snackbars, and the plain tooltip's
// look. They open on the overlay layer and animate in and out.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MenUI.Material3
{
    /// An entry of a Menu.
    public sealed class MenuItem
    {
        public readonly string Label;
        public readonly string Icon;
        public readonly string Trailing;
        public readonly Action OnClick;

        public MenuItem(string label, Action onClick, string icon = null, string trailing = null)
        {
            Label = label;
            OnClick = onClick;
            Icon = icon;
            Trailing = trailing;
        }
    }

    internal sealed class SnackbarMessage
    {
        internal readonly int id;
        internal readonly string text;
        internal readonly string action;
        internal readonly Action onAction;

        internal SnackbarMessage(int id, string text, string action, Action onAction)
        {
            this.id = id;
            this.text = text;
            this.action = action;
            this.onAction = onAction;
        }
    }

    internal static class MaterialOverlays
    {
        internal static Region Menu(MaterialDesign m3, Signal<bool> open, Element anchor, MenuItem[] items)
        {
            m3.Ensure(MaterialStyles.Overlays);
            MenUIContext ui = m3.ui;
            return ui.Show(() => open.Value, () => ui.Overlay(() =>
            {
                float left, top, width, height;
                ui.LogicalRect(anchor, out left, out top, out width, out height);
                Box menu = ui.Div().Tag("m3-menu").Left(left).Top(top + height + 4f);
                for (int i = 0; i < items.Length; i++)
                {
                    MenuItem item = items[i];
                    Box row = ui.Div().Tag("m3-menu-item");
                    row.Child(MaterialParts.Ripple(m3, row));
                    if (!string.IsNullOrEmpty(item.Icon))
                    {
                        row.Child(MaterialParts.Icon(m3, item.Icon, 24f, false));
                    }
                    row.Child(ui.Text(item.Label).FlexGrow(1));
                    if (!string.IsNullOrEmpty(item.Trailing))
                    {
                        row.Child(ui.Text(item.Trailing).Tag("m3-menu-trailing"));
                    }
                    row.OnClick(() =>
                    {
                        open.Value = false;
                        if (item.OnClick != null)
                        {
                            item.OnClick();
                        }
                    });
                    menu.Child(row);
                }
                // A press anywhere else closes it.
                Box layer = ui.Div().Tag("m3-menu-layer").Child(menu);
                layer.OnPointerDown(e =>
                {
                    if (e.Target == layer)
                    {
                        open.Value = false;
                    }
                });
                return layer;
            }));
        }

        internal static Region Dialog(MaterialDesign m3, Signal<bool> open, string headline, string text, string icon,
            Element[] actions)
        {
            m3.Ensure(MaterialStyles.Overlays);
            MenUIContext ui = m3.ui;
            return ui.Show(() => open.Value, () => ui.Overlay(() =>
            {
                Box dialog = ui.Div().Tag("m3-dialog");
                if (!string.IsNullOrEmpty(icon))
                {
                    dialog.Class("with-icon").Child(MaterialParts.Icon(m3, icon, 24f, false).Tag("m3-dialog-icon"));
                }
                dialog.Child(ui.Text(headline).Tag("m3-dialog-headline"));
                if (!string.IsNullOrEmpty(text))
                {
                    dialog.Child(ui.Text(text).Tag("m3-dialog-text"));
                }
                if (actions != null && actions.Length > 0)
                {
                    dialog.Child(ui.Div().Tag("m3-dialog-actions").Children(actions));
                }
                Box scrim = ui.Div().Tag("m3-scrim").Child(dialog);
                scrim.OnClick(e =>
                {
                    if (e.Target == scrim)
                    {
                        open.Value = false;
                    }
                });
                return scrim;
            }));
        }

        internal static void Snackbar(MaterialDesign m3, string message, string action, Action onAction, float seconds)
        {
            m3.Ensure(MaterialStyles.Overlays);
            MenUIContext ui = m3.ui;
            if (m3.snackbars == null)
            {
                m3.snackbars = ui.Signal(new List<SnackbarMessage>());
                Signal<List<SnackbarMessage>> list = m3.snackbars;
                ui.runtime.RunWithOwner(ui.runtime.root, () =>
                {
                    ui.Overlay(() => ui.Div().Tag("m3-snackbar-stack").Child(
                        ui.For(() => list.Value, item => item.id, (item, index) => Build(m3, list, item.Peek()))));
                });
            }
            SnackbarMessage entry = new SnackbarMessage(++m3.lastSnackbar, message, action, onAction);
            List<SnackbarMessage> shown = new List<SnackbarMessage>(m3.snackbars.Peek());
            shown.Add(entry);
            m3.snackbars.Value = shown;
            Signal<List<SnackbarMessage>> snackbars = m3.snackbars;
            ui.After(seconds, () => Dismiss(snackbars, entry));
        }

        private static Element Build(MaterialDesign m3, Signal<List<SnackbarMessage>> list, SnackbarMessage item)
        {
            MenUIContext ui = m3.ui;
            Box bar = ui.Div().Tag("m3-snackbar").Child(ui.Text(item.text).FlexGrow(1));
            if (!string.IsNullOrEmpty(item.action))
            {
                Box button = ui.Div().Tag("m3-snackbar-action").Child(ui.Text(item.action));
                button.Child(MaterialParts.Ripple(m3, button));
                button.OnClick(() =>
                {
                    Dismiss(list, item);
                    if (item.onAction != null)
                    {
                        item.onAction();
                    }
                });
                bar.Child(button);
            }
            return bar;
        }

        private static void Dismiss(Signal<List<SnackbarMessage>> list, SnackbarMessage item)
        {
            List<SnackbarMessage> remaining = new List<SnackbarMessage>(list.Peek());
            if (remaining.Remove(item))
            {
                list.Value = remaining;
            }
        }

        internal static void Rules(StyleSheet sheet, MaterialColorScheme c, MotionScheme motion)
        {
            AnimationSpec spatial = motion.FastSpatial;
            AnimationSpec effects = motion.FastEffects;
            sheet
                // Menus.
                .Rule("m3-menu-layer", s => s.Absolute().Inset(0).PointerEvents(PointerEventsMode.Auto))
                .Rule("m3-menu-layer:exit-to", s => s.PointerEvents(PointerEventsMode.None))
                .Rule("m3-menu", s => s.Absolute().Column().PaddingVertical(8).MinWidth(112).MaxWidth(280)
                    .BorderRadius(ShapeScale.Large).Background(c.SurfaceContainer).TextColor(c.OnSurface)
                    .BoxShadow(Elevation.Level(2)).FontSize(TypeScale.Size(TypeRole.BodyLarge))
                    .Transition(StyleProperty.Opacity, effects).Transition(TransitionGroup.Transform, spatial))
                .Rule("m3-menu:enter-from", s => s.Opacity(0f).Scale(0.9f).TranslateY(-8))
                .Rule("m3-menu:exit-to", s => s.Opacity(0f))
                .Rule("m3-menu-item", s => s.Row().AlignItems(Align.Center).Gap(12).Height(48).PaddingHorizontal(12)
                    .Position(PositionMode.Relative).Transition(TransitionGroup.Colors, motion.DefaultEffects))
                .Rule("m3-menu-item > m3-icon", s => s.TextColor(c.OnSurfaceVariant))
                .Rule("m3-menu-trailing", s => s.TextColor(c.OnSurfaceVariant).FontSize(TypeScale.Size(TypeRole.LabelLarge)))
                // Dialogs: a scrim, and the dialog scaling up into place.
                .Rule("m3-scrim", s => s.Absolute().Inset(0).Center().Background(MaterialParts.Alpha(c.Scrim, 0.32f))
                    .PointerEvents(PointerEventsMode.Auto).Transition(StyleProperty.Opacity, effects))
                .Rule("m3-scrim:enter-from", s => s.Opacity(0f))
                .Rule("m3-scrim:exit-to", s => s.Opacity(0f).PointerEvents(PointerEventsMode.None))
                .Rule("m3-dialog", s => s.Column().Gap(16).Padding(24).MinWidth(280).MaxWidth(560)
                    .BorderRadius(ShapeScale.ExtraLarge).Background(c.SurfaceContainerHigh).TextColor(c.OnSurface)
                    .BoxShadow(Elevation.Level(3)).Transition(TransitionGroup.Transform, motion.DefaultSpatial))
                .Rule("m3-dialog:enter-from", s => s.Scale(0.85f).TranslateY(24))
                .Rule("m3-dialog.with-icon", s => s.AlignItems(Align.Center))
                .Rule("m3-dialog-icon", s => s.TextColor(c.Secondary))
                .Rule("m3-dialog-headline", s => s.FontSize(TypeScale.Size(TypeRole.HeadlineSmall)))
                .Rule("m3-dialog-text", s => s.FontSize(TypeScale.Size(TypeRole.BodyMedium)).TextColor(c.OnSurfaceVariant))
                .Rule("m3-dialog-actions", s => s.Row().JustifyContent(Justify.End).Gap(8).MarginTop(8).FullWidth())
                // Snackbars.
                .Rule("m3-snackbar-stack", s => s.Absolute().Left(0).Right(0).Bottom(16).Column().AlignItems(Align.Center)
                    .Gap(8).PointerEvents(PointerEventsMode.None))
                .Rule("m3-snackbar", s => s.Row().AlignItems(Align.Center).Gap(8).MinHeight(48).MinWidth(288).MaxWidth(560)
                    .Padding(4, 8, 4, 16).BorderRadius(ShapeScale.ExtraSmall).Background(c.InverseSurface)
                    .TextColor(c.InverseOnSurface).FontSize(TypeScale.Size(TypeRole.BodyMedium)).BoxShadow(Elevation.Level(3))
                    .PointerEvents(PointerEventsMode.Auto)
                    .Transition(StyleProperty.Opacity, effects).Transition(TransitionGroup.Transform, spatial))
                .Rule("m3-snackbar:enter-from", s => s.Opacity(0f).TranslateY(24))
                .Rule("m3-snackbar:exit-to", s => s.Opacity(0f).Scale(0.95f))
                .Rule("m3-snackbar-action", s => s.Center().Height(36).PaddingHorizontal(12).BorderRadius(18)
                    .Position(PositionMode.Relative).TextColor(c.InversePrimary).FontSize(TypeScale.Size(TypeRole.LabelLarge)));
            MaterialButtons.ColorRules(sheet, "m3-menu-item", Colors.Transparent, c.OnSurface);
            MaterialButtons.ColorRules(sheet, "m3-snackbar-action", Colors.Transparent, c.InversePrimary);
        }

        /// The built-in tooltip, in Material's plain-tooltip look.
        internal static void TooltipRules(StyleSheet sheet, MaterialColorScheme c)
        {
            sheet.Rule("tooltip", s => s.Padding(4, 8).BorderRadius(ShapeScale.ExtraSmall).Background(c.InverseSurface)
                .TextColor(c.InverseOnSurface).FontSize(TypeScale.Size(TypeRole.BodySmall)));
        }
    }
}
