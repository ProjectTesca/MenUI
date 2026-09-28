using System.Collections.Generic;
using MenUI;
using UnityEngine;

// The built-in components, all bound to signals. The switch at the top flips
// the whole panel between the light and the dark theme.
public class MenUIComponentsDemo : MenUIApp
{
    private enum Quality { Low, Medium, High }

    protected override Element Build()
    {
        Signal<bool> dark = Signal(false);
        Effect(() => SetTheme(dark.Value ? Theme.Dark : Theme.Light));

        Signal<int> clicks = Signal(0);
        Signal<bool> agreed = Signal(false);
        Signal<bool> notifications = Signal(true);
        Signal<Quality> quality = Signal(Quality.Medium);
        Signal<float> volume = Signal(0.6f);
        Signal<float> steps = Signal(3f);
        Signal<string> fruit = Signal("Banana");
        Signal<int> number = Signal(12);
        Signal<int> tab = Signal(0);
        Signal<bool> dialogOpen = Signal(false);
        Signal<bool> busy = Signal(false);

        List<int> numbers = new List<int>();
        for (int i = 1; i <= 30; i++)
        {
            numbers.Add(i);
        }

        Box page = Div().Flex().Column().Gap(20).Padding(24).Children(
            Div().Row().JustifyContent(Justify.SpaceBetween).AlignItems(Align.Center).Children(
                Text("Components").FontSize(26),
                Switch(dark, "Dark theme")),

            Section("Buttons",
                Div().Row().Gap(8).FlexWrap(MenUI.WrapMode.Wrap).Children(
                    Button("Primary", ButtonVariant.Primary).OnClick(() => clicks.Value++),
                    Button("Secondary").OnClick(() => clicks.Value++),
                    Button("Danger", ButtonVariant.Danger).OnClick(() => clicks.Value = 0),
                    Button("Ghost", ButtonVariant.Ghost).OnClick(() => clicks.Value++),
                    Button("Disabled").Disabled(() => true),
                    Button("?").Tooltip("Tooltips appear after half a second")),
                Text(() => "Clicked " + clicks.Value + " times")),

            Section("Choices",
                Checkbox(agreed, "I agree to the terms"),
                Switch(notifications, "Notifications"),
                Div().Row().Gap(16).Children(
                    Radio(quality, Quality.Low, "Low"),
                    Radio(quality, Quality.Medium, "Medium"),
                    Radio(quality, Quality.High, "High")),
                Text(() => (agreed.Value ? "Agreed" : "Not agreed") + ", notifications "
                    + (notifications.Value ? "on" : "off") + ", quality " + quality.Value)),

            Section("Sliders and progress",
                Div().Row().Gap(12).AlignItems(Align.Center).Children(
                    Slider(volume).FlexGrow(1),
                    Text(() => Mathf.RoundToInt(volume.Value * 100f) + "%").Width(48)),
                Div().Row().Gap(12).AlignItems(Align.Center).Children(
                    Slider(steps, 0f, 10f, 1f).FlexGrow(1),
                    Text(() => steps.Value.ToString("0")).Width(48)),
                ProgressBar(() => volume.Value)),

            Section("Selects",
                Div().Row().Gap(12).Children(
                    Select(fruit, "Apple", "Banana", "Cherry", "Grape"),
                    Select(number, numbers, n => "Number " + n)),
                Text(() => fruit.Value + ", " + number.Value)),

            Section("Tabs",
                Tabs(tab, "General", "Audio", "About"),
                Dynamic(() => Text(tab.Value == 0 ? "General settings go here."
                    : tab.Value == 1 ? "Audio settings go here." : "MenUI components demo."))),

            Section("Dialog, toast, spinner",
                Div().Row().Gap(8).AlignItems(Align.Center).Children(
                    Button("Open dialog").OnClick(() => dialogOpen.Value = true),
                    Button("Show toast").OnClick(() => Toast("Saved at " + clicks.Peek() + " clicks")),
                    Button(() => busy.Value ? "Stop" : "Start work").OnClick(() => busy.Value = !busy.Peek()),
                    Show(() => busy.Value, () => Spinner()))),

            Dialog(dialogOpen, () => Div().Flex().Column().Gap(12).Children(
                Text("Delete everything?").FontSize(20),
                Text("This cannot be undone."),
                Divider(),
                Div().Row().Gap(8).JustifyContent(Justify.End).Children(
                    Button("Cancel").OnClick(() => dialogOpen.Value = false),
                    Button("Delete", ButtonVariant.Danger).OnClick(() =>
                    {
                        dialogOpen.Value = false;
                        Toast("Deleted");
                    })))));

        return ScrollArea(ScrollState(), page).FullSize().FontSize(16)
            .Background(() => UI.Theme.Background).TextColor(() => UI.Theme.Text);
    }

    private Box Section(string title, params Element[] content)
    {
        return Div().Flex().Column().Gap(10).Children(Text(title).FontSize(18), Divider())
            .Children(content);
    }

    private Box Button(System.Func<string> label)
    {
        return Button(Text(label));
    }
}
