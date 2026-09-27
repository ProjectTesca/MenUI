namespace MenUI
{
    /// A run of text. It is a box of its own (like a `p` or `span` with
    /// `display: block`); mixing styles inside one run is not supported yet.
    public sealed class TextElement : Element
    {
        internal string content = string.Empty;

        // Unwrapped (max-content) shaping, and the shaping at the final width.
        private readonly ShapedText maxContent = new ShapedText();
        private readonly ShapedText arranged = new ShapedText();
        private readonly ShapedText longestRun = new ShapedText();
        private ShapedText current;

        internal TextElement(MenUIContext context, string content) : base(context)
        {
            this.content = content ?? string.Empty;
            tag = "text";
        }

        internal override bool IsLeaf { get { return true; } }

        internal void SetContent(string value)
        {
            value = value ?? string.Empty;
            if (value == content)
            {
                return;
            }
            content = value;
            ContentChanged();
        }

        internal override void MeasureContent(int availableWidth, out int contentWidth, out int contentHeight)
        {
            float fontSize = computed.FontSize;
            context.ShaperFor(computed.FontIndex).Shape(maxContent, content, fontSize, -1, TextAlignMode.Start);
            if (availableWidth < 0 || maxContent.width <= availableWidth)
            {
                contentWidth = maxContent.width;
                contentHeight = maxContent.height;
                return;
            }
            context.ShaperFor(computed.FontIndex).Shape(arranged, content, fontSize, availableWidth, TextAlignMode.Start);
            contentWidth = availableWidth;   // wrapping text fills the line, as fit-content does in CSS
            contentHeight = arranged.height;
        }

        /// The widest unbreakable run: text between break opportunities —
        /// spaces, and around CJK characters (each can start a new line).
        /// Approximated by shaping the run with the most characters.
        internal override int MinContentWidthOfContent()
        {
            string text = content;
            int bestStart = 0, bestLength = 0;
            int runStart = 0;
            int length = text.Length;
            for (int i = 0; i <= length; i++)
            {
                bool end = i == length;
                int code = end ? 0 : text[i];
                bool space = !end && (code == ' ' || code == '\t' || code == '\n' || code == 0x3000);
                bool breaksAround = !end && IsBreakableCharacter(code);
                if (end || space || breaksAround)
                {
                    if (i - runStart > bestLength)
                    {
                        bestStart = runStart;
                        bestLength = i - runStart;
                    }
                    if (breaksAround && bestLength < 1)
                    {
                        bestStart = i;
                        bestLength = 1;
                    }
                    runStart = i + 1;
                }
            }
            if (bestLength == 0)
            {
                return 0;
            }
            string run = text.Substring(bestStart, bestLength);
            context.ShaperFor(computed.FontIndex).Shape(longestRun, run, computed.FontSize, -1, TextAlignMode.Start);
            return longestRun.width;
        }

        /// Characters that may be broken before or after (CJK ideographs,
        /// kana, full-width forms, CJK punctuation).
        private static bool IsBreakableCharacter(int code)
        {
            return (code >= 0x3000 && code <= 0x9FFF) || (code >= 0xF900 && code <= 0xFAFF)
                || (code >= 0xFF00 && code <= 0xFFEF) || (code >= 0xAC00 && code <= 0xD7AF);
        }

        internal override void ArrangeContent(int contentWidth, int contentHeight)
        {
            float fontSize = computed.FontSize;
            TextAlignMode align = computed.TextAlign;
            context.ShaperFor(computed.FontIndex).Shape(maxContent, content, fontSize, -1, TextAlignMode.Start);
            if (align == TextAlignMode.Start && maxContent.width <= contentWidth)
            {
                current = maxContent;
                return;
            }
            context.ShaperFor(computed.FontIndex).Shape(arranged, content, fontSize, contentWidth, align);
            current = arranged;
        }

        internal override void PaintContent(Painter painter, float opacity)
        {
            if (current == null || current.glyphCount == 0)
            {
                return;
            }
            painter.Glyphs(this, current, contentX, contentY, computed.ColorOf(StyleProperty.TextColor), opacity);
        }
    }
}
