// Material Design 3 (with M3 Expressive's components, shapes and motion)
// for MenUI. One MaterialDesign per panel holds the color scheme and the
// motion scheme, and builds the components:
//
//   public class MyMenu : MaterialApp            // or MenUIApp + MaterialDesign.Of(UI)
//   {
//       protected override Element Build()
//       {
//           return M3.Surface().Padding(24).Children(
//               M3.Button("Save", MaterialSymbol.Check).OnClick(Save),
//               M3.Fab(MaterialSymbol.Add));
//       }
//   }
//
// Components are ordinary elements with `m3-...` tags; their look lives in a
// style sheet below your own sheets (so your rules override it), rebuilt
// when the color or motion scheme changes (colors then change with a
// transition). Icons are Material Symbols (MaterialSymbol.*).

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MenUI.Material3
{
    public sealed class MaterialDesign
    {
        /// The icon fonts (bundled with MenUI; always registered).
        public const string IconFont = "Material Symbols Rounded";
        public const string FilledIconFont = "Material Symbols Rounded Filled";
        private const string ExtensionKey = "MenUI.Material3";

        internal readonly MenUIContext ui;
        private readonly Signal<MaterialColorScheme> scheme;
        private MotionScheme motion = MotionScheme.Expressive;
        internal readonly StyleSheet sheet = new StyleSheet();
        private int installed;

        /// The panel's MaterialDesign (made on first use).
        public static MaterialDesign Of(MenUIContext ui)
        {
            object found;
            if (ui.extensions.TryGetValue(ExtensionKey, out found))
            {
                return (MaterialDesign)found;
            }
            MaterialDesign created = new MaterialDesign(ui);
            ui.extensions[ExtensionKey] = created;
            return created;
        }

        private MaterialDesign(MenUIContext ui)
        {
            this.ui = ui;
            scheme = ui.Signal(MaterialColorScheme.BaselineLight);
            ui.AddDefaultStyleSheet(sheet);
        }

        public MenUIContext UI { get { return ui; } }

        /// The color scheme (a dependency when read in a binding).
        public MaterialColorScheme Scheme { get { return scheme.Value; } }

        internal MaterialColorScheme CurrentScheme { get { return scheme.Peek(); } }

        /// The motion scheme: Expressive (the default) or Standard.
        public MotionScheme Motion { get { return motion; } }

        public void SetColorScheme(MaterialColorScheme value)
        {
            if (value == null)
            {
                return;
            }
            scheme.Value = value;
            RebuildRules();
        }

        public void SetMotionScheme(MotionScheme value)
        {
            if (value == null)
            {
                return;
            }
            motion = value;
            RebuildRules();
        }

        private void RebuildRules()
        {
            sheet.Clear();
            for (int kind = 1; kind <= MaterialStyles.Last; kind <<= 1)
            {
                if ((installed & kind) != 0)
                {
                    MaterialStyles.Add(sheet, scheme.Peek(), motion, kind);
                }
            }
            ui.RematchStyles();
        }

        /// Adds a component's rules the first time one is built.
        internal void Ensure(int kind)
        {
            if ((installed & kind) != 0)
            {
                return;
            }
            installed |= kind;
            MaterialStyles.Add(sheet, scheme.Peek(), motion, kind);
            ui.RematchStyles();
        }

        // ---- basics -------------------------------------------------------------------

        /// A page: the surface color, text on it, body type.
        public Box Surface()
        {
            Ensure(MaterialStyles.Base);
            return ui.Div().Tag("m3-surface");
        }

        /// Text in a role of the type scale.
        public TextElement Text(string text, TypeRole role = TypeRole.BodyLarge)
        {
            return ui.Text(text).FontSize(TypeScale.Size(role));
        }

        public TextElement Text(Func<string> text, TypeRole role = TypeRole.BodyLarge)
        {
            return ui.Text(text).FontSize(TypeScale.Size(role));
        }

        /// A Material Symbol (MaterialSymbol.*), `size` logical px.
        public TextElement Icon(string symbol, float size = 24f, bool filled = false)
        {
            return MaterialParts.Icon(this, symbol, size, filled);
        }

        // ---- buttons --------------------------------------------------------------------

        /// A common button: label, optional icon (MaterialSymbol.*).
        public Box Button(string label, string icon = null, ButtonStyle style = ButtonStyle.Filled,
            ComponentSize size = ComponentSize.Small, ButtonShape shape = ButtonShape.Round)
        {
            return MaterialButtons.Button(this, label, icon, style, size, shape, null);
        }

        /// A toggle button: `selected` flips on click; the shape morphs
        /// (round ↔ square) and the colors change when selected.
        public Box ToggleButton(Signal<bool> selected, string label, string icon = null, ButtonStyle style = ButtonStyle.Filled,
            ComponentSize size = ComponentSize.Small, ButtonShape shape = ButtonShape.Round)
        {
            return MaterialButtons.Button(this, label, icon, style, size, shape, selected);
        }

        /// An icon button.
        public Box IconButton(string icon, IconButtonStyle style = IconButtonStyle.Standard,
            ComponentSize size = ComponentSize.Small, IconButtonWidth width = IconButtonWidth.Default,
            ButtonShape shape = ButtonShape.Round)
        {
            return MaterialButtons.IconButton(this, icon, style, size, width, shape, null);
        }

        public Box ToggleIconButton(Signal<bool> selected, string icon, IconButtonStyle style = IconButtonStyle.Standard,
            ComponentSize size = ComponentSize.Small, IconButtonWidth width = IconButtonWidth.Default,
            ButtonShape shape = ButtonShape.Round)
        {
            return MaterialButtons.IconButton(this, icon, style, size, width, shape, selected);
        }

        /// A floating action button.
        public Box Fab(string icon, FabSize size = FabSize.Regular, FabColor color = FabColor.PrimaryContainer)
        {
            return MaterialButtons.Fab(this, icon, size, color);
        }

        /// An extended FAB (icon and label); while `expanded` is false (if
        /// given) it collapses to its icon.
        public Box ExtendedFab(string label, string icon, FabSize size = FabSize.Regular,
            FabColor color = FabColor.PrimaryContainer, Func<bool> expanded = null)
        {
            return MaterialButtons.ExtendedFab(this, label, icon, size, color, expanded);
        }

        /// A FAB that opens a menu of actions above it (M3 Expressive). The
        /// FAB becomes a close button; the items spring in.
        public Box FabMenu(Signal<bool> open, string icon, params FabMenuItem[] items)
        {
            return MaterialButtons.FabMenu(this, open, icon, items);
        }

        /// Buttons in a row that react together: a pressed one widens and
        /// its neighbors make room (standard), or joined into one shape with
        /// the selected one fully rounded (connected).
        public Box ButtonGroup(ButtonGroupKind kind, params Element[] buttons)
        {
            return MaterialButtons.ButtonGroup(this, kind, buttons);
        }

        /// A connected, single-select button group bound to `selected` (the
        /// index): e.g. Going / Not going / Maybe.
        public Box SegmentedGroup(Signal<int> selected, string[] labels, string[] icons = null,
            ComponentSize size = ComponentSize.Small)
        {
            return MaterialButtons.SegmentedGroup(this, selected, labels, icons, size);
        }

        /// A split button: an action, and a menu arrow beside it.
        public Box SplitButton(string label, string icon, Action onClick, Signal<bool> menuOpen,
            ButtonStyle style = ButtonStyle.Filled, ComponentSize size = ComponentSize.Small)
        {
            return MaterialButtons.SplitButton(this, label, icon, onClick, menuOpen, style, size);
        }

        // ---- toolbars ------------------------------------------------------------------

        /// A floating toolbar: a pill of controls (icon buttons, ...).
        public Box FloatingToolbar(bool vibrant, params Element[] items)
        {
            return MaterialBars.FloatingToolbar(this, vibrant, items);
        }

        /// A toolbar docked to an edge (full width).
        public Box DockedToolbar(params Element[] items)
        {
            return MaterialBars.DockedToolbar(this, items);
        }

        /// An icon button for toolbars that shows `selected` as a wide filled pill.
        public Box ToolbarItem(string icon, Func<bool> selected, Action onClick, bool vibrant = false)
        {
            return MaterialBars.ToolbarItem(this, icon, selected, onClick, vibrant);
        }

        // ---- progress ---------------------------------------------------------------------

        /// A linear progress indicator: `progress` 0..1, or null for an
        /// indeterminate one. Wavy (Expressive) or flat.
        public Graphic LinearProgress(Func<float> progress = null, bool wavy = false, float thickness = 4f)
        {
            return MaterialProgress.Linear(this, progress, wavy, thickness);
        }

        /// A circular progress indicator (null progress: indeterminate).
        public Graphic CircularProgress(Func<float> progress = null, bool wavy = false, float size = 40f)
        {
            return MaterialProgress.Circular(this, progress, wavy, size);
        }

        /// The loading indicator (M3 Expressive): a shape morphing through
        /// the shape library while it turns; `contained` puts it on a circle.
        public Element LoadingIndicator(bool contained = false, float size = 48f)
        {
            return MaterialProgress.Loading(this, contained, size);
        }

        // ---- selection ------------------------------------------------------------------

        public Box Checkbox(Signal<bool> isChecked, string label = null)
        {
            return MaterialSelection.Checkbox(this, isChecked, label);
        }

        public Box Radio<T>(Signal<T> selected, T value, string label = null)
        {
            return MaterialSelection.Radio(this, selected, value, label);
        }

        /// A switch; with `icons`, the thumb shows a check (on) or a cross (off).
        public Box Switch(Signal<bool> isOn, string label = null, bool icons = false)
        {
            return MaterialSelection.Switch(this, isOn, label, icons);
        }

        /// A slider (M3 Expressive: a thick track and a bar handle).
        public Box Slider(Signal<float> value, float minimum = 0f, float maximum = 1f, float step = 0f)
        {
            return MaterialSelection.Slider(this, value, minimum, maximum, step);
        }

        // ---- text fields ----------------------------------------------------------------

        /// A text field with a floating label (filled or outlined).
        public Box TextField(Signal<string> value, string label, bool outlined = false, string supportingText = null,
            string leadingIcon = null)
        {
            return MaterialFields.TextField(this, value, label, outlined, supportingText, leadingIcon);
        }

        // ---- containment -------------------------------------------------------------------

        public Box Card(CardStyle style = CardStyle.Filled)
        {
            return MaterialContainers.Card(this, style);
        }

        /// A chip: assist (an action), filter (with `selected`), input (with
        /// `onRemove`) or suggestion.
        public Box Chip(string label, string icon = null, Signal<bool> selected = null, Action onRemove = null)
        {
            return MaterialContainers.Chip(this, label, icon, selected, onRemove);
        }

        /// A list item: a headline, optional supporting text, leading icon and trailing text.
        public Box ListItem(string headline, string supporting = null, string leadingIcon = null, string trailing = null)
        {
            return MaterialContainers.ListItem(this, headline, supporting, leadingIcon, trailing);
        }

        public Box Divider()
        {
            return MaterialContainers.Divider(this);
        }

        /// A badge: a small dot (null text) or a count.
        public Box Badge(Func<string> text = null)
        {
            return MaterialContainers.Badge(this, text);
        }

        // ---- navigation ---------------------------------------------------------------------

        /// Primary tabs with a sliding indicator (secondary: a thinner one under the whole tab).
        public Box Tabs(Signal<int> selected, string[] labels, string[] icons = null, bool secondary = false)
        {
            return MaterialNavigation.Tabs(this, selected, labels, icons, secondary);
        }

        public Box NavigationBar(Signal<int> selected, string[] labels, string[] icons)
        {
            return MaterialNavigation.NavigationBar(this, selected, labels, icons);
        }

        /// A navigation rail; `expanded` switches between the narrow rail
        /// (labels under icons) and the wide one (M3 Expressive). `fab` (an
        /// extended FAB, say) goes above the items.
        public Box NavigationRail(Signal<int> selected, Signal<bool> expanded, string[] labels, string[] icons, Element fab = null)
        {
            return MaterialNavigation.NavigationRail(this, selected, expanded, labels, icons, fab);
        }

        /// A top app bar: a navigation icon, a title, actions at the end.
        public Box TopAppBar(string title, string navigationIcon = null, Action onNavigation = null,
            TopAppBarStyle style = TopAppBarStyle.Small, params Element[] actions)
        {
            return MaterialNavigation.TopAppBar(this, title, navigationIcon, onNavigation, style, actions);
        }

        // ---- overlays ---------------------------------------------------------------------

        /// A menu anchored under `anchor` while `open` is true.
        public Region Menu(Signal<bool> open, Element anchor, params MenuItem[] items)
        {
            return MaterialOverlays.Menu(this, open, anchor, items);
        }

        /// A basic dialog: optional icon, headline, text, and actions (buttons).
        public Region Dialog(Signal<bool> open, string headline, string text, string icon = null, params Element[] actions)
        {
            return MaterialOverlays.Dialog(this, open, headline, text, icon, actions);
        }

        /// A snackbar at the bottom of the panel for `seconds`; with an
        /// action button when `action` is given.
        public void Snackbar(string message, string action = null, Action onAction = null, float seconds = 4f)
        {
            MaterialOverlays.Snackbar(this, message, action, onAction, seconds);
        }

        /// A plain tooltip (M3 look) after resting on `element`.
        public T Tooltip<T>(T element, string text) where T : Element
        {
            Ensure(MaterialStyles.Tooltip);
            return element.Tooltip(text);
        }

        // ---- shared state for overlays ------------------------------------------------------

        internal Signal<List<SnackbarMessage>> snackbars;
        internal int lastSnackbar;
    }

    /// A reusable piece of Material UI (see Component).
    public abstract class MaterialComponent : Component
    {
        protected MaterialDesign M3 { get { return MaterialDesign.Of(UI); } }
    }
}
