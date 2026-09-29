// Transform groups: parts of the tree the GPU moves, scales and fades
// without layout or repaint — the content of a scroll view, an element with
// `.Translate(...)`, an element with a transform or a running fade (see
// Element.NeedsLayer).
//
// Layout works in one untransformed coordinate space. Each element belongs
// to a group; its records carry the group index, and the shader maps them
// with the group's accumulated transform, clips to the group's clip and
// multiplies in the group's opacity. Scrolling, moving, zooming or fading is
// then a change to two small arrays (_GroupOffsets, _GroupClips) instead of
// new records.
//
// A group's own transform, in its parent's coordinates: scale by `scale`
// around the pivot (the element's center), then move by the offset. The
// accumulated transform to the screen is a uniform scale S and a
// translation T: screen = layout * S + T.
//
// A group's clip is the region its owner allows (a scroll view's padding
// box, or whatever clipped a translated element), given in the parent
// group's coordinates; the screen-space clip is that, mapped by the parent's
// transform and intersected with the parent's own clip. Clips inside a group
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
        // Transform relative to the parent group (physical px), and opacity.
        private readonly float[] offsetX = new float[Capacity];
        private readonly float[] offsetY = new float[Capacity];
        // The owner's own style: translate (added to the offset above, which
        // scrolling, `.Translate(() => ...)` and dragging set), scale, opacity.
        private readonly float[] styleX = new float[Capacity];
        private readonly float[] styleY = new float[Capacity];
        private readonly float[] scale = new float[Capacity];
        private readonly float[] pivotX = new float[Capacity];
        private readonly float[] pivotY = new float[Capacity];
        private readonly float[] alpha = new float[Capacity];
        // The group's clip in the parent group's coordinates.
        private readonly float[] clipLeft = new float[Capacity];
        private readonly float[] clipTop = new float[Capacity];
        private readonly float[] clipRight = new float[Capacity];
        private readonly float[] clipBottom = new float[Capacity];

        // Results for the shader and hit testing: accumulated translation
        // (x, y), scale (z) and opacity (w); screen-space clip (left, top,
        // right, bottom).
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
            for (int i = 0; i < Capacity; i++)
            {
                scale[i] = 1f;
                alpha[i] = 1f;
                offsets[i] = new Vector4(0f, 0f, 1f, 1f);
            }
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
                    styleX[i] = 0f;
                    styleY[i] = 0f;
                    scale[i] = 1f;
                    alpha[i] = 1f;
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

        /// The owner's translate (physical px), scale around the pivot, and
        /// opacity (1: as is).
        internal void SetStyleTransform(int group, float x, float y, float newScale, float newAlpha)
        {
            if (group == Root || (styleX[group] == x && styleY[group] == y && scale[group] == newScale && alpha[group] == newAlpha))
            {
                return;
            }
            styleX[group] = x;
            styleY[group] = y;
            scale[group] = newScale;
            alpha[group] = newAlpha;
            dirty = true;
        }

        /// Placement from layout: the parent group, the clip (in the parent's
        /// coordinates) and the pivot for scaling (the owner's center).
        internal void SetFrame(int group, int parentGroup, int left, int top, int right, int bottom,
            float centerX = 0f, float centerY = 0f)
        {
            if (group == Root)
            {
                return;
            }
            if (parent[group] == parentGroup && clipLeft[group] == left && clipTop[group] == top
                && clipRight[group] == right && clipBottom[group] == bottom
                && pivotX[group] == centerX && pivotY[group] == centerY)
            {
                return;
            }
            pivotX[group] = centerX;
            pivotY[group] = centerY;
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
                offsets[Root] = new Vector4(0f, 0f, 1f, 1f);
                clips[Root] = new Vector4(clipLeft[Root], clipTop[Root], clipRight[Root], clipBottom[Root]);
                return;
            }
            int p = parent[group];
            ResolveGroup(p);
            Vector4 parentTransform = offsets[p];
            Vector4 parentClip = clips[p];
            float s = scale[group];
            float parentScale = parentTransform.z;
            // local(q) = q * s + pivot * (1 - s) + offset, then the parent's.
            float localX = pivotX[group] * (1f - s) + offsetX[group] + styleX[group];
            float localY = pivotY[group] * (1f - s) + offsetY[group] + styleY[group];
            offsets[group] = new Vector4(parentTransform.x + localX * parentScale, parentTransform.y + localY * parentScale,
                parentScale * s, parentTransform.w * alpha[group]);
            clips[group] = new Vector4(
                Mathf.Max(parentClip.x, clipLeft[group] * parentScale + parentTransform.x),
                Mathf.Max(parentClip.y, clipTop[group] * parentScale + parentTransform.y),
                Mathf.Min(parentClip.z, clipRight[group] * parentScale + parentTransform.x),
                Mathf.Min(parentClip.w, clipBottom[group] * parentScale + parentTransform.y));
        }

        /// A layout x (physical px) in `group` on the screen, and back.
        internal float ScreenX(int group, float x)
        {
            Vector4 t = offsets[group];
            return x * t.z + t.x;
        }

        internal float ScreenY(int group, float y)
        {
            Vector4 t = offsets[group];
            return y * t.z + t.y;
        }

        internal float LocalX(int group, float screenX)
        {
            Vector4 t = offsets[group];
            return t.z != 0f ? (screenX - t.x) / t.z : 0f;
        }

        internal float LocalY(int group, float screenY)
        {
            Vector4 t = offsets[group];
            return t.z != 0f ? (screenY - t.y) / t.z : 0f;
        }

        internal float ScaleOf(int group)
        {
            return offsets[group].z;
        }
    }
}
