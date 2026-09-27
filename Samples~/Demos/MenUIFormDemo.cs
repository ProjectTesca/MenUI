using MenUI;
using UnityEngine;

// Text boxes. Click one: VRChat opens its input UI (in the editor, ClientSim
// types straight into it). The values are signals, so the preview follows
// every edit; "Commit" lines appear when editing finishes. The dark look is
// a style sheet rule for `input, textarea` over MenUI's built-in one.
public class MenUIFormDemo : MenUIApp
{
    protected override Element Build()
    {
        Color surface = Colors.Hex(0x1E293B);
        Color raised = Colors.Hex(0x334155);
        Color text = Colors.Hex(0xE2E8F0);
        Color muted = Colors.Hex(0x94A3B8);
        Color accent = Colors.Hex(0x3B82F6);

        AddStyleSheet(new StyleSheet()
            .Rule("input, textarea", s => s.Background(Colors.Hex(0x0F172A)).TextColor(text).Border(1, Colors.Hex(0x475569)))
            .Rule("input:hover, textarea:hover", s => s.BorderColor(Colors.Hex(0x64748B)))
            .Rule("input:focus, textarea:focus", s => s.BorderColor(accent))
            .Rule(".label", s => s.FontSize(13).TextColor(muted))
            .Rule(".button", s => s.Center().Padding(8, 16).BorderRadius(8).Background(raised))
            .Rule(".button:hover", s => s.Background(Colors.Hex(0x475569)))
            .Rule(".button:disabled", s => s.Opacity(0.4f)));

        Signal<string> name = Signal("");
        Signal<string> password = Signal("");
        Signal<string> notes = Signal("");
        Signal<string> log = Signal("");
        Signal<bool> locked = Signal(false);

        Box form = Div().Flex().Column().Gap(12).FlexGrow(1).Children(
            Text("Text boxes").FontSize(24),
            Field("Name", TextBox(name).Placeholder("山田 太郎").MaxLength(40)
                .Disabled(() => locked.Value)
                .OnCommit(value => log.Value = "Commit name: " + value + "\n" + log.Peek())),
            Field("Password", TextBox(password).Password().Placeholder("8 characters or more")
                .Disabled(() => locked.Value)),
            Field("Notes", TextArea(notes).Rows(4).Placeholder("複数行のメモ")
                .Disabled(() => locked.Value)
                .OnCommit(value => log.Value = "Commit notes (" + value.Length + " characters)\n" + log.Peek())),
            Div().Row().Gap(8).Children(
                Div().Class("button").OnClick(() => locked.Value = !locked.Peek())
                    .Children(Text(() => locked.Value ? "Unlock" : "Lock (disable)")),
                Div().Class("button").Disabled(() => locked.Value)
                    .OnClick(() => Batch(() => { name.Value = ""; password.Value = ""; notes.Value = ""; }))
                    .Children(Text("Clear")),
                Div().Class("button").Disabled(() => locked.Value)
                    .OnClick(() => name.Value = "Set from code")
                    .Children(Text("Set name from code"))));

        Box preview = Div().Flex().Column().Gap(8).Width(280).Padding(16).BorderRadius(12).Background(raised).Children(
            Text("Preview").FontSize(18),
            Text(() => "Hello, " + (name.Value.Length > 0 ? name.Value : "nobody") + "!"),
            Text(() => "Password: " + password.Value.Length + " characters").Class("label"),
            Text(() => notes.Value.Length > 0 ? notes.Value : "(no notes)"),
            Text(() => log.Value).Class("label"));

        Box page = Div().Flex().Row().Gap(24).Padding(24).AlignItems(Align.Start).Children(form, preview);
        return ScrollArea(ScrollState(), page).FullSize().Background(surface).TextColor(text).FontSize(16);
    }

    private Box Field(string label, TextBox box)
    {
        return Div().Flex().Column().Gap(4).Children(Text(label).Class("label"), box);
    }
}
