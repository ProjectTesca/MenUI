// Transform groups: parts of the tree the GPU moves without layout or
// repaint — the content of a scroll view, an element with `.Translate(...)`.
//
// Layout works in one untranslated coordinate space. Each element belongs to
// a group; its records carry the group index, and the shader adds the
// group's accumulated offset and clips to the group's clip. Scrolling or
// translating is then a change to two small arrays (_GroupOffsets,
// _GroupClips) instead of new records.
//
// A group's clip is the region its owner allows (a scroll view's padding
// box, or whatever clipped a translated element), given in the parent
// group's coordinates; the screen-space clip is that, moved by the parent's
// offset and intersected with the parent's own clip. Clips inside a group
// (overflow: hidden within the content) stay on the records themselves.

using UnityEngine;

namespace MenUI
{
    internal sealed class TransformGroups
    {
        internal const int Capacity = 128;
        internal const int Root = 0;
        internal const int Unbounded = 10000000;

        private readonly bool[] used = new bool[Capacity];
        private readonly int[] parent = new int[Capacity];
        // Offset relative to the parent group, physical px.
        private readonly float[] offsetX = new float[Capacity];
        private readonly float[] offsetY = new float[Capacity];
        // The group's clip in the parent group's coordinates.
        private readonly float[] clipLeft = new float[Capacity];
        private readonly float[] clipTop = new float[Capacity];
        private readonly float[] clipRight = new float[Capacity];
        private readonly float[] clipBottom = new float[Capacity];

        // Results for the shader and hit testing: accumulated offset (xy) and
        // screen-space clip (left, top, right, bottom).
        internal readonly Vector4[] offsets = new Vector4[Capacity];
        internal readonly Vector4[] clips = new Vector4[Capacity];
        private readonly int[] resolvedStamp = new int[Capacity];
        private int stamp;
        private bool warnedFull;

        /// Something changed since the last Resolve.
        internal bool dirty = true;
        /// Resolved values the GPU has not received yet. Separate from
        /// `dirty`: hit testing resolves too, and must not swallow the upload.
        internal bool uploadPending = true;

        internal TransformGroups(int panelWidth, int panelHeight)
        {
            used[Root] = true;
            clipRight[Root] = panelWidth;
            clipBottom[Root] = panelHeight;
        }

        /// The root group's clip: the whole panel.
        internal void SetPanelSize(int panelWidth, int panelHeight)
        {
            clipRight[Root] = panelWidth;
            clipBottom[Root] = panelHeight;
            dirty = true;
            uploadPending = true;
        }

        internal int Allocate()
        {
            for (int i = 1; i < Capacity; i++)
            {
                if (!used[i])
                {
                    used[i] = true;
                    parent[i] = Root;
                    offsetX[i] = 0f;
                    offsetY[i] = 0f;
                    clipLeft[i] = -Unbounded;
                    clipTop[i] = -Unbounded;
                    clipRight[i] = Unbounded;
                    clipBottom[i] = Unbounded;
                    dirty = true;
                    return i;
                }
            }
            if (!warnedFull)
            {
                warnedFull = true;
                Debug.LogWarning("[MenUI] more than " + (Capacity - 1) + " scroll views / translated elements; the rest do not move.");
            }
            return Root;
        }

        internal void Free(int group)
        {
            if (group > Root)
            {
                used[group] = false;
                dirty = true;
            }
        }

        internal void SetOffset(int group, float x, float y)
        {
            if (group == Root || (offsetX[group] == x && offsetY[group] == y))
            {
                return;
            }
            offsetX[group] = x;
            offsetY[group] = y;
            dirty = true;
        }

        /// Placement from layout: the parent group and the clip (in the
        /// parent's coordinates).
        internal void SetFrame(int group, int parentGroup, int left, int top, int right, int bottom)
        {
            if (group == Root)
            {
                return;
            }
            if (parent[group] == parentGroup && clipLeft[group] == left && clipTop[group] == top
                && clipRight[group] == right && clipBottom[group] == bottom)
            {
                return;
            }
            parent[group] = parentGroup;
            clipLeft[group] = left;
            clipTop[group] = top;
            clipRight[group] = right;
            clipBottom[group] = bottom;
            dirty = true;
        }

        internal void Resolve()
        {
            stamp++;
            for (int i = 0; i < Capacity; i++)
            {
                if (used[i])
                {
                    ResolveGroup(i);
                }
            }
            dirty = false;
            uploadPending = true;
        }

        private void ResolveGroup(int group)
        {
            if (resolvedStamp[group] == stamp)
            {
                return;
            }
            resolvedStamp[group] = stamp;
            if (group == Root)
            {
                offsets[Root] = new Vector4(0f, 0f, 0f, 0f);
                clips[Root] = new Vector4(clipLeft[Root], clipTop[Root], clipRight[Root], clipBottom[Root]);
                return;
            }
            int p = parent[group];
            ResolveGroup(p);
            Vector4 parentOffset = offsets[p];
            Vector4 parentClip = clips[p];
            offsets[group] = new Vector4(parentOffset.x + offsetX[group], parentOffset.y + offsetY[group], 0f, 0f);
            clips[group] = new Vector4(
                Mathf.Max(parentClip.x, clipLeft[group] + parentOffset.x),
                Mathf.Max(parentClip.y, clipTop[group] + parentOffset.y),
                Mathf.Min(parentClip.z, clipRight[group] + parentOffset.x),
                Mathf.Min(parentClip.w, clipBottom[group] + parentOffset.y));
        }
    }
}
