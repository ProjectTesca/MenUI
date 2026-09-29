using System;
using System.Collections.Generic;
using MenUI;
using MenUI.Material3;
using UnityEngine;

// Material Design 3 Expressive: the showcase tiles from the M3 Expressive
// announcement (FAB menu, toolbars, connected button group, wavy progress,
// loading indicator, navigation rail), then every component by page. The
// header switches light/dark, the seed color, and Expressive/Standard motion.
public class MenUIMaterialDemo : MaterialApp
{
    private Signal<bool> dark;
    private Signal<int> seed;

    protected override Element Build()
    {
        ResizePanel(1800, 1200, 1.5f);
        dark = Signal(false);
        seed = Signal(0);
        Signal<int> motion = Signal(0);
        Effect(() => M3.SetColorScheme(Scheme(seed.Value, dark.Value)));
        Effect(() => M3.SetMotionScheme(motion.Value == 0 ? MotionScheme.Expressive : MotionScheme.Standard));

        Signal<int> page = Signal(0);
        Box header = Div().Row().AlignItems(Align.Center).Gap(16).Padding(16, 24).Children(
            M3.Text("Material 3 Expressive", TypeRole.HeadlineSmall).FlexGrow(1),
            SeedChips(),
            M3.SegmentedGroup(motion, new[] { "Expressive", "Standard" }, null, ComponentSize.ExtraSmall),
            M3.Switch(dark, "Dark", true));

        // Pages are kept once built (switching back only hides and shows),
        // and built ahead one per frame, so switching tabs stays quick.
        Region content = Pages(() => page.Value, true,
            () => Page(Showcase()), () => Page(ButtonsPage()), () => Page(InputsPage()), () => Page(MorePage()));

        return M3.Surface().FullSize().Column().Children(
            header,
            M3.Tabs(page, new[] { "Showcase", "Buttons", "Inputs", "More" },
                new[] { MaterialSymbol.AutoAwesome, MaterialSymbol.SmartButton, MaterialSymbol.ToggleOn, MaterialSymbol.Widgets }),
            content);
    }

    private Element Page(Element body)
    {
        return ScrollArea(ScrollState(), Div().Padding(24).Child(body)).FlexGrow(1).MinHeight(0);
    }

    private static MaterialColorScheme Scheme(int seedIndex, bool isDark)
    {
        if (seedIndex == 0)
        {
            return isDark ? MaterialColorScheme.BaselineDark : MaterialColorScheme.BaselineLight;
        }
        return MaterialColorScheme.FromSeed(SeedColor(seedIndex), isDark);
    }

    private static Color SeedColor(int index)
    {
        if (index == 1) return Colors.Hex(0x3B82F6);
        if (index == 2) return Colors.Hex(0x16A34A);
        if (index == 3) return Colors.Hex(0xEA580C);
        return Colors.Hex(0xDB2777);
    }

    private Element SeedChips()
    {
        Box row = Div().Row().Gap(8);
        for (int i = 0; i < 5; i++)
        {
            int index = i;
            Color swatch = i == 0 ? Colors.Hex(0x6750A4) : SeedColor(i);
            row.Child(Div().Size(28, 28).BorderRadius(14).Background(swatch)
                .Bind(e => e.BorderColor(seed.Value == index ? M3.Scheme.OnSurface : Colors.Transparent))
                .BorderWidth(3).Transition(TransitionGroup.Corners, M3.Motion.FastSpatial)
                .Hover(s => s.BorderRadius(8))
                .OnClick(() => seed.Value = index));
        }
        return row;
    }

    // ---- showcase: the tiles of the M3 Expressive announcement ------------------------------

    private Box Tile(float width, float height, Element content)
    {
        return Div().Size(width, height).FlexShrink(0).Center().BorderRadius(28).Padding(16)
            .Background(() => M3.Scheme.SurfaceContainerHigh).Child(content);
    }

    private Element Showcase()
    {
        Signal<bool> menuOpen = Signal(true);
        Signal<int> going = Signal(0);
        Signal<int> tool = Signal(3);
        Signal<int> destination = Signal(1);
        Signal<bool> railExpanded = Signal(true);
        AnimatedFloat progress = Animated(0.3f);
        progress.Play(new Keyframes(0.05f).To(0.95f, 4f, Easing.EaseInOut).To(0.05f, 4f, Easing.EaseInOut), true);

        Box fabMenu = M3.FabMenu(menuOpen, MaterialSymbol.Add,
            new FabMenuItem("Select", MaterialSymbol.CheckCircle, null),
            new FabMenuItem("Add photos", MaterialSymbol.AddAPhoto, null),
            new FabMenuItem("Share album", MaterialSymbol.Share, null),
            new FabMenuItem("Search", MaterialSymbol.Search, null));

        Box toolbar = Div().Row().AlignItems(Align.Center).Gap(12).Children(
            M3.FloatingToolbar(false,
                M3.IconButton(MaterialSymbol.CheckCircle), M3.IconButton(MaterialSymbol.Favorite),
                M3.IconButton(MaterialSymbol.Description), M3.IconButton(MaterialSymbol.CalendarMonth)),
            M3.Fab(MaterialSymbol.Close, FabSize.Regular, FabColor.TertiaryContainer));

        Box group = M3.SegmentedGroup(going, new[] { "Going", "Not going", "Maybe" },
            new[] { MaterialSymbol.CheckCircle, MaterialSymbol.Block, MaterialSymbol.Help });

        Box bars = Div().Column().Gap(20).Width(300).Children(
            M3.LinearProgress(() => progress.Value),
            M3.LinearProgress(() => progress.Value, false, 8f),
            M3.LinearProgress(() => progress.Value, true));

        string[] tools = { MaterialSymbol.Edit, MaterialSymbol.Crop, MaterialSymbol.Tune, MaterialSymbol.Settings, MaterialSymbol.Draw };
        Box toolRow = M3.FloatingToolbar(false);
        for (int i = 0; i < tools.Length; i++)
        {
            int index = i;
            toolRow.Child(M3.ToolbarItem(tools[i], () => tool.Value == index, () => tool.Value = index));
        }

        Box rail = M3.NavigationRail(destination, railExpanded,
            new[] { "Friends", "Alarms", "Map", "Settings" },
            new[] { MaterialSymbol.Favorite, MaterialSymbol.Alarm, MaterialSymbol.Map, MaterialSymbol.Settings },
            M3.ExtendedFab("Add friend", MaterialSymbol.PersonAdd, FabSize.Regular, FabColor.TertiaryContainer,
                () => railExpanded.Value));

        Signal<bool> closed = Signal(false);
        return Div().Row().FlexWrap(MenUI.WrapMode.Wrap).Gap(24).Children(
            Tile(360, 380, fabMenu),
            Tile(420, 180, toolbar),
            Tile(420, 180, group),
            Tile(420, 180, bars),
            Tile(360, 180, toolRow),
            Tile(200, 180, M3.LoadingIndicator(true, 96f)),
            Tile(200, 180, M3.ToggleIconButton(closed, MaterialSymbol.Close, IconButtonStyle.Filled, ComponentSize.Large)),
            Tile(360, 420, rail).AlignItems(Align.Start).JustifyContent(Justify.Start));
    }

    // ---- buttons ---------------------------------------------------------------------------

    private Element ButtonsPage()
    {
        Box page = Div().Column().Gap(28);
        ComponentSize[] sizes = { ComponentSize.ExtraSmall, ComponentSize.Small, ComponentSize.Medium, ComponentSize.Large, ComponentSize.ExtraLarge };
        page.Child(M3.Text("Common buttons (press them: the corners morph)", TypeRole.TitleLarge));
        ButtonStyle[] styles = { ButtonStyle.Filled, ButtonStyle.Tonal, ButtonStyle.Elevated, ButtonStyle.Outlined, ButtonStyle.Text };
        string[] names = { "Filled", "Tonal", "Elevated", "Outlined", "Text" };
        Box styleRow = Div().Row().Gap(12).AlignItems(Align.Center).FlexWrap(MenUI.WrapMode.Wrap);
        for (int i = 0; i < styles.Length; i++)
        {
            styleRow.Child(M3.Button(names[i], i == 0 ? MaterialSymbol.Add : null, styles[i]));
        }
        page.Child(styleRow);

        Box sizeRow = Div().Row().Gap(12).AlignItems(Align.Center).FlexWrap(MenUI.WrapMode.Wrap);
        string[] sizeNames = { "XS", "S", "M", "L", "XL" };
        for (int i = 0; i < sizes.Length; i++)
        {
            sizeRow.Child(M3.Button(sizeNames[i], MaterialSymbol.Star, ButtonStyle.Filled, sizes[i],
                i % 2 == 0 ? ButtonShape.Round : ButtonShape.Square));
        }
        page.Child(sizeRow);

        page.Child(M3.Text("Toggle buttons (round ↔ square when selected)", TypeRole.TitleLarge));
        Box toggles = Div().Row().Gap(12).AlignItems(Align.Center);
        for (int i = 0; i < 4; i++)
        {
            toggles.Child(M3.ToggleButton(Signal(i % 2 == 0), names[i], MaterialSymbol.Favorite, styles[i], ComponentSize.Medium));
        }
        page.Child(toggles);

        page.Child(M3.Text("Icon buttons", TypeRole.TitleLarge));
        IconButtonStyle[] iconStyles = { IconButtonStyle.Standard, IconButtonStyle.Filled, IconButtonStyle.Tonal, IconButtonStyle.Outlined };
        Box icons = Div().Row().Gap(12).AlignItems(Align.Center).FlexWrap(MenUI.WrapMode.Wrap);
        for (int i = 0; i < iconStyles.Length; i++)
        {
            icons.Child(M3.ToggleIconButton(Signal(false), MaterialSymbol.Bookmark, iconStyles[i], ComponentSize.Medium));
        }
        icons.Child(M3.IconButton(MaterialSymbol.Mic, IconButtonStyle.Filled, ComponentSize.Medium, IconButtonWidth.Narrow));
        icons.Child(M3.IconButton(MaterialSymbol.Mic, IconButtonStyle.Filled, ComponentSize.Medium, IconButtonWidth.Wide, ButtonShape.Square));
        page.Child(icons);

        page.Child(M3.Text("FABs", TypeRole.TitleLarge));
        Signal<bool> extended = Signal(true);
        page.Child(Div().Row().Gap(16).AlignItems(Align.Center).Children(
            M3.Fab(MaterialSymbol.Edit),
            M3.Fab(MaterialSymbol.Edit, FabSize.Medium, FabColor.SecondaryContainer),
            M3.Fab(MaterialSymbol.Edit, FabSize.Large, FabColor.Tertiary),
            M3.ExtendedFab("Compose", MaterialSymbol.Edit, FabSize.Regular, FabColor.PrimaryContainer, () => extended.Value)
                .OnClick(() => extended.Value = !extended.Peek()),
            M3.Text("← click to fold", TypeRole.BodySmall)));

        page.Child(M3.Text("Button groups (press one: it widens)", TypeRole.TitleLarge));
        Signal<bool> splitOpen = Signal(false);
        Box split = M3.SplitButton("Send", MaterialSymbol.Send, () => M3.Snackbar("Sent"), splitOpen);
        page.Child(Div().Row().Gap(32).AlignItems(Align.Center).FlexWrap(MenUI.WrapMode.Wrap).Children(
            M3.ButtonGroup(ButtonGroupKind.Standard,
                M3.Button("One", null, ButtonStyle.Tonal, ComponentSize.Medium),
                M3.Button("Two", null, ButtonStyle.Tonal, ComponentSize.Medium),
                M3.Button("Three", null, ButtonStyle.Tonal, ComponentSize.Medium)),
            M3.SegmentedGroup(Signal(1), new[] { "Day", "Week", "Month" }, null, ComponentSize.Medium),
            split,
            M3.Menu(splitOpen, split,
                new MenuItem("Schedule send", () => M3.Snackbar("Scheduled"), MaterialSymbol.Schedule),
                new MenuItem("Save draft", () => M3.Snackbar("Draft saved"), MaterialSymbol.Draft))));
        return page;
    }

    // ---- inputs ----------------------------------------------------------------------------

    private Element InputsPage()
    {
        Signal<bool> a = Signal(true), b = Signal(false), s1 = Signal(true), s2 = Signal(false);
        Signal<int> choice = Signal(1);
        Signal<float> volume = Signal(0.4f), steps = Signal(3f);
        Signal<string> name = Signal(""), email = Signal("hello@example.com");
        Signal<bool> f1 = Signal(true), f2 = Signal(false), f3 = Signal(false);
        List<string> tags = new List<string> { "Unity", "VRChat", "Udon" };
        Signal<List<string>> tagList = Signal(tags);

        return Div().Column().Gap(28).Children(
            M3.Text("Selection", TypeRole.TitleLarge),
            Div().Row().Gap(24).FlexWrap(MenUI.WrapMode.Wrap).Children(
                M3.Checkbox(a, "Checked"), M3.Checkbox(b, "Unchecked"),
                M3.Radio(choice, 0, "One"), M3.Radio(choice, 1, "Two"), M3.Radio(choice, 2, "Three"),
                M3.Switch(s1, "Switch"), M3.Switch(s2, "With icons", true)),
            M3.Text("Sliders", TypeRole.TitleLarge),
            M3.Slider(volume).Width(420),
            M3.Slider(steps, 0f, 10f, 1f).Width(420),
            M3.Text("Text fields", TypeRole.TitleLarge),
            Div().Row().Gap(24).FlexWrap(MenUI.WrapMode.Wrap).Children(
                M3.TextField(name, "Name", false, "As it appears on your profile"),
                M3.TextField(email, "Email", true, null, MaterialSymbol.Mail)),
            M3.Text("Chips", TypeRole.TitleLarge),
            Div().Row().Gap(8).FlexWrap(MenUI.WrapMode.Wrap).Children(
                M3.Chip("Assist", MaterialSymbol.Event),
                M3.Chip("Filter", null, f1), M3.Chip("Another", null, f2), M3.Chip("Third", null, f3),
                For(() => tagList.Value, tag => tag, (tag, index) => M3.Chip(tag.Peek(), null, null, () =>
                {
                    List<string> next = new List<string>(tagList.Peek());
                    next.Remove(tag.Peek());
                    tagList.Value = next;
                }))));
    }

    // ---- more: containers, navigation, overlays, progress --------------------------------------

    private Element MorePage()
    {
        Signal<int> tab = Signal(0), nav = Signal(0);
        Signal<bool> dialogOpen = Signal(false), menuOpen = Signal(false);
        Box menuButton = M3.Button("Menu", MaterialSymbol.ArrowDropDown, ButtonStyle.Outlined);
        menuButton.OnClick(() => menuOpen.Value = !menuOpen.Peek());

        Box card = M3.Card(CardStyle.Elevated).Width(320).Children(
            M3.Text("Elevated card", TypeRole.TitleMedium),
            M3.Text("Cards hold related content and actions.", TypeRole.BodyMedium),
            Div().Row().JustifyContent(Justify.End).Gap(8).Children(
                M3.Button("Share", null, ButtonStyle.Text), M3.Button("Open")));

        Box list = M3.Card(CardStyle.Outlined).Width(360).Padding(0).Gap(0).Children(
            M3.ListItem("Inbox", "3 new messages", MaterialSymbol.Inbox, "12:03"),
            M3.Divider(),
            M3.ListItem("Starred", null, MaterialSymbol.Star, "8"),
            M3.ListItem("Sent", "Yesterday", MaterialSymbol.Send));

        return Div().Column().Gap(28).Children(
            M3.TopAppBar("Top app bar", MaterialSymbol.Menu, null, TopAppBarStyle.Small,
                M3.IconButton(MaterialSymbol.Search), M3.IconButton(MaterialSymbol.MoreVert)),
            Div().Row().Gap(24).FlexWrap(MenUI.WrapMode.Wrap).AlignItems(Align.Start).Children(
                card, list, M3.Card(CardStyle.Filled).Width(240).Children(
                    M3.Text("Filled card", TypeRole.TitleMedium),
                    Div().Row().Gap(8).AlignItems(Align.Center).Children(M3.Text("Badges"), M3.Badge(), M3.Badge(() => "12")))),
            M3.Tabs(tab, new[] { "Flights", "Trips", "Explore" }).Width(480),
            M3.Tabs(tab, new[] { "Flights", "Trips", "Explore" }, null, true).Width(480),
            M3.NavigationBar(nav, new[] { "Home", "Search", "Profile" },
                new[] { MaterialSymbol.Home, MaterialSymbol.Search, MaterialSymbol.Person }).Width(480),
            Div().Row().Gap(12).AlignItems(Align.Center).Children(
                M3.Button("Dialog", MaterialSymbol.OpenInNew, ButtonStyle.Tonal).OnClick(() => dialogOpen.Value = true),
                M3.Button("Snackbar", null, ButtonStyle.Tonal).OnClick(() => M3.Snackbar("Message archived", "Undo", () => M3.Snackbar("Restored"))),
                menuButton,
                M3.Tooltip(M3.IconButton(MaterialSymbol.Info), "A plain tooltip"),
                M3.Menu(menuOpen, menuButton,
                    new MenuItem("Cut", null, MaterialSymbol.ContentCut, "Ctrl+X"),
                    new MenuItem("Copy", null, MaterialSymbol.ContentCopy, "Ctrl+C"),
                    new MenuItem("Paste", null, MaterialSymbol.ContentPaste, "Ctrl+V"))),
            M3.Dialog(dialogOpen, "Reset settings?", "This will reset your app preferences back to their default settings.",
                MaterialSymbol.RestartAlt,
                M3.Button("Cancel", null, ButtonStyle.Text).OnClick(() => dialogOpen.Value = false),
                M3.Button("Accept", null, ButtonStyle.Text).OnClick(() => dialogOpen.Value = false)),
            M3.Text("Progress", TypeRole.TitleLarge),
            Div().Row().Gap(32).AlignItems(Align.Center).Children(
                M3.CircularProgress(() => 0.7f), M3.CircularProgress(() => 0.7f, true, 48f),
                M3.CircularProgress(), M3.CircularProgress(null, true, 48f),
                M3.LoadingIndicator(), M3.LoadingIndicator(true)),
            M3.LinearProgress().Width(420),
            M3.LinearProgress(null, true).Width(420));
    }
}
