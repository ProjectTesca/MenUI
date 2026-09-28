// A text box: CSS `input` (one line) and `textarea` (several lines).
//
//   var name = Signal("");
//   TextBox(name).Placeholder("Your name").OnCommit(value => Save(value));
//   TextArea(notes).Rows(4).FullWidth();
//   TextBox(password).Password().MaxLength(32);
//
// The value is a signal, kept in both directions: what the player types is
// written to it, and setting it changes what the box shows. MenUI draws the
// box; typing goes through the panel's text input host (see TextInput.cs),
// which in VRChat brings up the client's own input UI when the box is
// clicked. There is no caret: editing happens in that UI.
//
// Like a browser's input, its size does not follow its text: about 20
// characters wide and one line (or `Rows`) high unless styled otherwise,
// and it never grows past what its container gives it. The default look is
// the `input` / `textarea` rules of MenUI's built-in style sheet; any rule
// or chained style of your own overrides it.

using System;
using UnityEngine;

namespace MenUI
{
    public sealed class TextBox : Element
    {
        internal readonly Signal<string> value;
        internal readonly bool multiLine;
        internal readonly TextInputTarget target;
        internal string placeholder = string.Empty;
        internal bool password;
        internal int maxLength;
        internal int rows = 2;
        internal Color placeholderColor;
        internal bool hasPlaceholderColor;
        private Action<string> committed;

        // What is drawn: the value (as bullets for a password) or the placeholder.
        private string shown = string.Empty;
        private bool showingPlaceholder = true;
        private readonly ShapedText shaped = new ShapedText();
        private int contentWidth, contentHeight;
        private int offsetX, offsetY;

        internal TextBox(MenUIContext context, Signal<string> value, bool multiLine) : base(context)
        {
            this.value = value;
            this.multiLine = multiLine;
            tag = multiLine ? "textarea" : "input";
            target = new TextInputTarget(this);
            context.EnsureStyles(ComponentStyles.TextBox);
            context.Bind(() => Show(value.Value));
        }

        internal override bool IsLeaf { get { return true; } }

        // ---- options (chained) -------------------------------------------------------

        /// Shown, dimmed, while the value is empty.
        public TextBox Placeholder(string text)
        {
            placeholder = text ?? string.Empty;
            Show(value.Peek());
            return this;
        }

        /// The placeholder's color (default: the text color, dimmed).
        public TextBox PlaceholderColor(Color color)
        {
            placeholderColor = color;
            hasPlaceholderColor = true;
            MarkPaintDirty();
            return this;
        }

        /// Shows bullets instead of the characters.
        public TextBox Password()
        {
            password = true;
            Show(value.Peek());
            return this;
        }

        /// At most `length` characters can be typed (0: no limit).
        public TextBox MaxLength(int length)
        {
            maxLength = Mathf.Max(0, length);
            return this;
        }

        /// Lines of text the box is high by default (TextArea; default 2).
        public TextBox Rows(int count)
        {
            rows = Mathf.Max(1, count);
            MarkLayoutDirty(true);
            return this;
        }

        /// Runs when the player finishes editing without cancelling (what
        /// browsers call `change`); the signal follows every edit already.
        public TextBox OnCommit(Action<string> handler)
        {
            committed += handler;
            return this;
        }

        /// Whether the box is being edited now.
        public bool IsEditing { get { return target.focused; } }

        // ---- input (from the host) -----------------------------------------------------

        internal void ApplyInput(string text)
        {
            text = text ?? string.Empty;
            if (!multiLine && text.IndexOf('\n') >= 0)
            {
                text = text.Replace("\r", string.Empty).Replace('\n', ' ');
            }
            if (maxLength > 0 && text.Length > maxLength)
            {
                text = text.Substring(0, maxLength);
            }
            if (text != value.Peek())
            {
                context.runtime.Batch(() => { value.Value = text; });
            }
        }

        internal void ApplyFocus(bool isFocused, bool canceled)
        {
            context.textInput.FocusChanged(target, isFocused);
            SetPseudoState(PseudoState.Focus, isFocused);
            if (!isFocused && !canceled && committed != null)
            {
                string text = value.Peek();
                Action<string> handler = committed;
                context.runtime.Batch(() => handler(text));
            }
        }

        // ---- layout and paint -------------------------------------------------------------

        private void Show(string text)
        {
            text = text ?? string.Empty;
            bool empty = text.Length == 0;
            string next = empty ? placeholder : password ? new string('•', text.Length) : text;
            if (next == shown && empty == showingPlaceholder)
            {
                return;
            }
            bool colorOnly = next == shown;
            shown = next;
            showingPlaceholder = empty;
            if (colorOnly)
            {
                MarkPaintDirty();
            }
            else
            {
                // The box's size never depends on its text: only it is laid out again.
                ContentChanged();
            }
        }

        private int LineBoxHeight(int lines)
        {
            float fontSize = computed.FontSize;
            if (context.shapers == null)
            {
                return Mathf.CeilToInt(fontSize * 1.45f * lines);   // headless tests: no fonts
            }
            return context.ShaperFor(computed.FontIndex).LineBoxHeight(fontSize, lines);
        }

        internal override void MeasureContent(int availableWidth, out int width, out int height)
        {
            // About 20 characters, as a browser's input and textarea.
            width = Mathf.RoundToInt(computed.FontSize * 10f);
            height = LineBoxHeight(multiLine ? rows : 1);
        }

        internal override int MinContentWidthOfContent()
        {
            // It can shrink to nothing (a browser's input would not; that
            // takes `min-width: 0` there).
            return 0;
        }

        internal override void ArrangeContent(int width, int height)
        {
            contentWidth = width;
            contentHeight = height;
            offsetX = 0;
            offsetY = 0;
            if (context.shapers == null)
            {
                return;
            }
            TextShaper shaper = context.ShaperFor(computed.FontIndex);
            float fontSize = computed.FontSize;
            TextAlignMode align = computed.TextAlign;
            if (multiLine)
            {
                shaper.Shape(shaped, shown, fontSize, width, align);
                return;
            }
            // One line, never wrapped; aligned while it fits, from the start
            // once it does not. Centered vertically, as browsers do.
            shaper.Shape(shaped, shown, fontSize, -1, TextAlignMode.Start);
            int free = width - shaped.width;
            if (free > 0)
            {
                offsetX = align == TextAlignMode.Center ? free / 2 : align == TextAlignMode.End ? free : 0;
            }
            offsetY = (height - LineBoxHeight(1)) / 2;
        }

        internal override void PaintContent(Painter painter, float opacity)
        {
            if (shaped.glyphCount == 0)
            {
                return;
            }
            Color color = computed.ColorOf(StyleProperty.TextColor);
            if (showingPlaceholder)
            {
                if (hasPlaceholderColor)
                {
                    color = placeholderColor;
                }
                else
                {
                    color.a *= 0.45f;
                }
            }
            // Clipped to the content box (vertically to the border box for
            // one line, so descenders of a tight box are not cut).
            int left = Mathf.Max(contentX, clipLeft);
            int right = Mathf.Min(contentX + contentWidth, clipRight);
            int top = Mathf.Max(multiLine ? contentY : y, clipTop);
            int bottom = Mathf.Min(multiLine ? contentY + contentHeight : y + height, clipBottom);
            painter.Glyphs(this, shaped, contentX + offsetX, contentY + offsetY, color, opacity, left, top, right, bottom);
        }
    }
}
