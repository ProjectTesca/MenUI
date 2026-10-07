// A panel's draw records (see PanelRenderer for the layout): the painter
// writes them here; the renderer uploads them. Plain data, so the painter
// runs (and is tested) without a GPU.

using UnityEngine;

namespace MenUI
{
    internal sealed class RecordBuffer
    {
        internal const int TexelsPerRecord = 8;

        internal readonly Color[] pixels;
        // The records of the previous full paint, for elements that did not
        // change to copy theirs from (see Painter.PaintAll).
        private readonly Color[] previous;
        private bool overflowed;
        internal readonly int capacity;
        private int count;
        private bool warnedFull;
        /// Something was written since the renderer last uploaded.
        internal bool changed = true;

        // All zero: bulk-clears records (empty records draw nothing).
        private readonly Color[] zeros;
        // Range mode: a range of records is painted again from its start
        // (the records after it stay); what is left of it is emptied.
        private bool rangeMode;
        private int rangeCursor, rangeEnd;
        private bool rangeOverflowed;

        // Overwrite mode: records are rewritten in place in [cursor, end).
        private bool overwriting;
        private int overwriteCursor;
        private int overwriteEnd;
        private bool overwriteOverflowed;

        internal RecordBuffer(int capacity)
        {
            this.capacity = capacity;
            pixels = new Color[capacity * TexelsPerRecord];
            previous = new Color[capacity * TexelsPerRecord];
            zeros = new Color[capacity * TexelsPerRecord];
        }

        /// Where the next record goes (the count, or the cursor of a range).
        internal int RecordCount { get { return rangeMode ? rangeCursor : count; } }

        /// Appends `length` empty records (room kept for later).
        internal void AppendEmpty(int length)
        {
            length = Mathf.Min(length, capacity - count);
            if (length <= 0)
            {
                return;
            }
            System.Array.Copy(zeros, 0, pixels, count * TexelsPerRecord, length * TexelsPerRecord);
            count += length;
            changed = true;
        }

        /// Paints `length` records from `start` again (see Painter.RepaintRange):
        /// the old ones are kept aside for CopyPrevious.
        internal void BeginRange(int start, int length)
        {
            System.Array.Copy(pixels, start * TexelsPerRecord, previous, start * TexelsPerRecord, length * TexelsPerRecord);
            rangeMode = true;
            rangeCursor = start;
            rangeEnd = start + length;
            rangeOverflowed = false;
        }

        /// Steps over `length` records of the range left exactly as they are
        /// (they are already in place).
        internal void SkipInRange(int length)
        {
            if (rangeCursor + length > rangeEnd)
            {
                rangeOverflowed = true;
                return;
            }
            rangeCursor += length;
        }

        /// Empties what the range did not use; false when it did not fit.
        internal bool EndRange()
        {
            rangeMode = false;
            if (rangeCursor < rangeEnd)
            {
                System.Array.Copy(zeros, 0, pixels, rangeCursor * TexelsPerRecord, (rangeEnd - rangeCursor) * TexelsPerRecord);
            }
            changed = true;
            return !rangeOverflowed;
        }

        /// Starts a full paint. The current records are kept aside first, so
        /// unchanged elements can copy theirs over (CopyPrevious).
        internal void BeginRecords()
        {
            System.Array.Copy(pixels, previous, count * TexelsPerRecord);
            count = 0;
            overflowed = false;
        }

        /// Some record of this full paint did not fit (the capacity is full).
        internal bool Overflowed { get { return rangeMode ? rangeOverflowed : overflowed; } }

        /// Appends `length` records of the previous full paint from `start`,
        /// unchanged: one bulk copy instead of computing them again.
        internal void CopyPrevious(int start, int length)
        {
            if (length <= 0)
            {
                return;
            }
            if (rangeMode)
            {
                if (rangeCursor + length > rangeEnd)
                {
                    rangeOverflowed = true;
                    return;
                }
                System.Array.Copy(previous, start * TexelsPerRecord, pixels, rangeCursor * TexelsPerRecord, length * TexelsPerRecord);
                rangeCursor += length;
                changed = true;
                return;
            }
            if (count + length > capacity)
            {
                Full();
                return;
            }
            System.Array.Copy(previous, start * TexelsPerRecord, pixels, count * TexelsPerRecord, length * TexelsPerRecord);
            count += length;
            changed = true;
        }

        private void Full()
        {
            overflowed = true;
            if (!warnedFull)
            {
                warnedFull = true;
                Debug.LogWarning("[MenUI] the panel needs more than " + capacity + " draw records; the rest are not drawn. "
                    + "Show fewer elements at once (a VirtualList for long lists, Pages or Near for the rest).");
            }
        }

        /// Rewrites `length` existing records from `start` instead of
        /// appending. EndOverwrite says whether exactly that many were written.
        internal void BeginOverwrite(int start, int length)
        {
            overwriting = true;
            overwriteCursor = start;
            overwriteEnd = start + length;
            overwriteOverflowed = false;
        }

        internal bool EndOverwrite()
        {
            overwriting = false;
            return !overwriteOverflowed && overwriteCursor == overwriteEnd;
        }

        internal int AllocateRecord()
        {
            if (overwriting)
            {
                if (overwriteCursor >= overwriteEnd)
                {
                    overwriteOverflowed = true;
                    return -1;
                }
                return ClearRecord(overwriteCursor++);
            }
            if (rangeMode)
            {
                if (rangeCursor >= rangeEnd)
                {
                    rangeOverflowed = true;
                    return -1;
                }
                return ClearRecord(rangeCursor++);
            }
            if (count >= capacity)
            {
                Full();
                return -1;
            }
            return ClearRecord(count++);
        }

        /// Clears the texels a primitive may leave unwritten.
        private int ClearRecord(int record)
        {
            int b = record * TexelsPerRecord;
            Color zero = new Color(0f, 0f, 0f, 0f);
            pixels[b + 5] = zero;
            pixels[b + 6] = zero;
            pixels[b + 7] = zero;
            return record;
        }

        internal void Texel(int record, int texel, float r, float g, float b, float a)
        {
            pixels[record * TexelsPerRecord + texel] = new Color(r, g, b, a);
            changed = true;
        }

        internal void Texel(int record, int texel, Color color)
        {
            pixels[record * TexelsPerRecord + texel] = color;
            changed = true;
        }
    }
}
