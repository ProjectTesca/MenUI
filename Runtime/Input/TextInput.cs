// The boundary between text entry and MenUI.
//
// MenUI draws text boxes itself, but typing needs something the player's
// client understands. In VRChat that is a real uGUI InputField: clicking one
// directly (not activating it from code) brings up VRChat's own input UI —
// the desktop input window or the VR keyboard, with IME.
//
// So MenUI tells a text input host, once a frame, which text boxes a pointer
// could click right now — each one hovered by a pointer, the one last
// pressed, and any being edited — with the part of each that is visible, in
// logical pixels on the panel. The host puts something clickable there (the
// default one: a transparent InputField, see InputFieldTextInputHost) and
// reports back what the player typed and when editing starts and ends.
// Because the list follows MenUI's own hit testing, nothing covering a text
// box (a menu, a dialog) is ever shadowed by an input field.
//
// Another host (an in-world keyboard, ...) plugs in with
// `MenUIApp.CreateTextInputHost` or `UI.SetTextInputHost(...)`.

using System.Collections.Generic;
using UnityEngine;

namespace MenUI
{
    /// One text box as a text input host sees it.
    public sealed class TextInputTarget
    {
        internal readonly TextBox box;
        internal bool focused;

        internal TextInputTarget(TextBox box)
        {
            this.box = box;
        }

        /// The visible part of the text box on the panel, in logical pixels
        /// from the top-left corner.
        public float Left { get; internal set; }
        public float Top { get; internal set; }
        public float Width { get; internal set; }
        public float Height { get; internal set; }
        /// The current value.
        public string Text { get { return box.value.Peek(); } }
        public bool Password { get { return box.password; } }
        public bool MultiLine { get { return box.multiLine; } }
        /// 0: no limit.
        public int MaxLength { get { return box.maxLength; } }
        /// Font size in logical pixels (for hosts that show the text themselves).
        public float FontSize { get { return box.computed.FontSize / box.context.scale; } }
        /// Whether this box is being edited (as last reported).
        public bool Focused { get { return focused; } }

        /// The player changed the text.
        public void ReportText(string text)
        {
            box.ApplyInput(text);
        }

        /// Editing started (true) or ended (false). `canceled`: ended by
        /// cancelling (the text is back to what it was).
        public void ReportFocus(bool isFocused, bool canceled)
        {
            if (isFocused == focused)
            {
                return;
            }
            focused = isFocused;
            box.ApplyFocus(isFocused, canceled);
        }
    }

    public interface ITextInputHost
    {
        /// Called once when the host is installed on a panel.
        void Attach(PanelSurface surface);

        /// The text boxes a pointer can reach now (see TextInput.cs). Called
        /// once a frame, before MenUI lays out and draws; the list is reused.
        void Update(List<TextInputTarget> targets);
    }

    /// Finds the text boxes to hand to the host each frame.
    internal sealed class TextInputCoordinator
    {
        private readonly MenUIContext context;
        private readonly List<TextInputTarget> targets = new List<TextInputTarget>();
        // Being edited, as the host reported: kept on the list until it ends.
        private readonly List<TextInputTarget> focused = new List<TextInputTarget>();
        // The text box the last press on the panel landed on (null: elsewhere):
        // kept on the list while the client's input UI may still be opening.
        private TextBox lastPressed;
        internal ITextInputHost host;

        internal TextInputCoordinator(MenUIContext context)
        {
            this.context = context;
        }

        internal void Pressed(Element element)
        {
            lastPressed = NearestTextBox(element);
        }

        internal void FocusChanged(TextInputTarget target, bool isFocused)
        {
            if (isFocused)
            {
                if (!focused.Contains(target))
                {
                    focused.Add(target);
                }
            }
            else
            {
                focused.Remove(target);
            }
        }

        internal void Update()
        {
            if (host == null)
            {
                return;
            }
            targets.Clear();
            for (int i = 0; i < focused.Count; i++)
            {
                Offer(focused[i].box, true);
            }
            if (lastPressed != null)
            {
                Offer(lastPressed, false);
            }
            for (int slot = 0; slot < InputRouter.MaxPointers; slot++)
            {
                TextBox hovered = NearestTextBox(context.router.HoverTarget(slot));
                if (hovered != null)
                {
                    Offer(hovered, false);
                }
            }
            host.Update(targets);
        }

        /// Adds `box` with its visible rectangle, unless it cannot be clicked
        /// (not drawn, disabled, fully clipped). A box being edited stays even
        /// if it scrolled out of view, so the edit can finish.
        private void Offer(TextBox box, bool editing)
        {
            TextInputTarget target = box.target;
            if (targets.Contains(target))
            {
                return;
            }
            bool clickable = box.paintGeneration == context.painter.Generation
                && box.computed.IsVisible
                && box.computed.Enum(StyleProperty.PointerEvents) == (int)PointerEventsMode.Auto
                && (box.pseudoStates & PseudoState.Disabled) == 0;
            if (!clickable && !editing)
            {
                return;
            }
            // The element's rectangle within its clip (both in its group's
            // space), moved by the group's offset and cut by the group's clip.
            float left = Mathf.Max(box.x, box.clipLeft);
            float top = Mathf.Max(box.y, box.clipTop);
            float right = Mathf.Min(box.x + box.width, box.clipRight);
            float bottom = Mathf.Min(box.y + box.height, box.clipBottom);
            int group = box.group;
            if (group != TransformGroups.Root)
            {
                Vector4 offset = context.groups.offsets[group];
                Vector4 clip = context.groups.clips[group];
                left = Mathf.Max(left + offset.x, clip.x);
                top = Mathf.Max(top + offset.y, clip.y);
                right = Mathf.Min(right + offset.x, clip.z);
                bottom = Mathf.Min(bottom + offset.y, clip.w);
            }
            if ((right <= left || bottom <= top) && !editing)
            {
                return;
            }
            float scale = context.scale;
            target.Left = left / scale;
            target.Top = top / scale;
            target.Width = Mathf.Max(0f, right - left) / scale;
            target.Height = Mathf.Max(0f, bottom - top) / scale;
            targets.Add(target);
        }

        private static TextBox NearestTextBox(Element element)
        {
            for (Element e = element; e != null; e = e.parent)
            {
                TextBox box = e as TextBox;
                if (box != null)
                {
                    return box;
                }
            }
            return null;
        }
    }
}
