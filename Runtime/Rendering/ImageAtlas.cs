// Images share the panel's single draw: each is copied, at about the size it
// is shown, into an atlas page (a RenderTexture the panel shader samples as
// _Image0.._Image3), and its record samples that region.
//
// A copy is made per image and level of detail: level k is the image at
// 1/2^k of its pixels, the largest level that still covers the size it is
// drawn at (a 2048 px photo shown at 300 px is copied at 512 px). Elements
// hold the copies they draw; a copy nobody holds stays cached (showing it
// again costs nothing) until room is needed. Pages are shelf-packed; when
// one does not fit, unheld copies are dropped and the rest packed again
// (compaction, before the next layout), and only then is a page added.
//
// The copy itself is one Blit of a small shader (MenUI/ImageCopy) that
// draws the source into the region only, premultiplied, box-filtered when
// shrinking.

using System.Collections.Generic;
using UnityEngine;
#if !MENUI_HEADLESS
using VRC.SDKBase;
#endif

namespace MenUI
{
    /// One copy of an image in an atlas page.
    internal sealed class ImageCopy
    {
        internal ImageSource source;
        internal int level;
        internal int page;              // -1: not placed
        internal int x, y;              // the region's bottom-left texel (v up)
        internal int width, height;     // texels
        internal readonly List<Element> holders = new List<Element>();
    }

    /// One atlas page: a square RenderTexture, shelf-packed.
    internal sealed class ImagePage
    {
        internal RenderTexture texture;   // null in headless tests
        internal int size;
        internal readonly List<int> shelfY = new List<int>();
        internal readonly List<int> shelfHeight = new List<int>();
        internal readonly List<int> shelfX = new List<int>();
        internal int top;                 // where the next shelf starts
        internal int copies;              // copies placed on it

        internal void Clear()
        {
            shelfY.Clear();
            shelfHeight.Clear();
            shelfX.Clear();
            top = 0;
            copies = 0;
        }

        /// A free spot for a `width` x `height` slot, or false.
        internal bool Place(int width, int height, out int x, out int y)
        {
            int best = -1;
            int bestHeight = int.MaxValue;
            for (int i = 0; i < shelfY.Count; i++)
            {
                int h = shelfHeight[i];
                // A shelf that fits without wasting much of its height.
                if (h >= height && h <= height + height / 4 + 4 && shelfX[i] + width <= size && h < bestHeight)
                {
                    best = i;
                    bestHeight = h;
                }
            }
            if (best < 0)
            {
                if (top + height > size || width > size)
                {
                    x = 0;
                    y = 0;
                    return false;
                }
                shelfY.Add(top);
                shelfHeight.Add(height);
                shelfX.Add(0);
                best = shelfY.Count - 1;
                top += height;
            }
            x = shelfX[best];
            y = shelfY[best];
            shelfX[best] = x + width;
            copies++;
            return true;
        }
    }

    public sealed class ImageAtlas
    {
        internal const int MaxPages = 4;
        internal const int SmallPage = 1024;
        internal const int LargePage = 2048;
        // Empty texels around each copy: bilinear sampling at its edge never
        // reaches a neighbour.
        private const int Gutter = 1;

        private readonly MenUIContext context;
        private readonly PanelRenderer renderer;
        /// MenUI/ImageCopy (set by MenUIApp; null: images are not drawn).
        internal Material copyMaterial;

        internal readonly ImagePage[] pages = new ImagePage[MaxPages];
        internal int pageCount;
        // Every copy made and not dropped, held or not.
        internal readonly List<ImageCopy> copies = new List<ImageCopy>();
        // The sources made from textures (Image(texture)), reused.
        private readonly List<ImageSource> sources = new List<ImageSource>();
        /// A copy did not fit: compact before the next layout.
        internal bool compactionPending;
        // Elements that drew nothing for want of room: they repaint after it.
        private readonly List<Element> waiting = new List<Element>();
        private bool warnedFull, warnedMaterial;
        /// Copies made (diagnostics and tests).
        internal int copiesMade;

        internal ImageAtlas(MenUIContext context, PanelRenderer renderer)
        {
            this.context = context;
            this.renderer = renderer;
        }

        /// The source for `texture` (or its region `pixels`, x and y from the
        /// top-left; an empty rect: all of it), the same one each time.
        internal ImageSource SourceFor(Texture texture, Rect pixels)
        {
            if (texture == null)
            {
                return null;
            }
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].Is(texture, pixels))
                {
                    return sources[i];
                }
            }
            ImageSource source;
            if (pixels.width <= 0f || pixels.height <= 0f)
            {
                source = new ImageSource(texture, 0f, 0f, 1f, 1f, texture.width, texture.height);
            }
            else
            {
                float textureWidth = texture.width;
                float textureHeight = texture.height;
                source = new ImageSource(texture, pixels.x / textureWidth, 1f - (pixels.y + pixels.height) / textureHeight,
                    (pixels.x + pixels.width) / textureWidth, 1f - pixels.y / textureHeight,
                    (int)pixels.width, (int)pixels.height);
            }
            sources.Add(source);
            return source;
        }

        /// The level of detail that covers `width` x `height` physical px:
        /// the most halvings that keep the copy at least that large, and the
        /// copy no larger than a page.
        internal static int LevelFor(ImageSource source, float width, float height)
        {
            int level = 0;
            while (level < ImageSource.Levels - 1
                && ((source.width >> (level + 1)) >= width && (source.height >> (level + 1)) >= height
                    || CopySize(source.width, level) + Gutter * 2 > LargePage
                    || CopySize(source.height, level) + Gutter * 2 > LargePage))
            {
                level++;
            }
            return level;
        }

        private static int CopySize(int pixels, int level)
        {
            return Mathf.Max(1, (pixels + (1 << level) - 1) >> level);
        }

        /// The copy of `source` for drawing it `width` x `height` physical px,
        /// held by `holder`; null while there is no room (it is made after
        /// the next compaction) or no copy material.
        internal ImageCopy Acquire(Element holder, ImageSource source, float width, float height)
        {
            int level = LevelFor(source, width, height);
            ImageCopy copy = source.copies[level];
            if (copy == null)
            {
                copy = Make(source, level);
                if (copy == null)
                {
                    if (compactionPending && !waiting.Contains(holder))
                    {
                        waiting.Add(holder);
                    }
                    return null;
                }
            }
            if (!copy.holders.Contains(holder))
            {
                copy.holders.Add(holder);
            }
            return copy;
        }

        /// `holder` no longer draws `copy` (it stays cached until room is needed).
        internal void Release(Element holder, ImageCopy copy)
        {
            if (copy != null)
            {
                copy.holders.Remove(holder);
            }
        }

        private ImageCopy Make(ImageSource source, int level)
        {
            if (renderer != null && copyMaterial == null)
            {
                if (!warnedMaterial)
                {
                    warnedMaterial = true;
                    Debug.LogWarning("[MenUI] images need the panel set up again: select it and run MenUI > Set Up Selected Panel.");
                }
                return null;
            }
            ImageCopy copy = new ImageCopy();
            copy.source = source;
            copy.level = level;
            copy.width = CopySize(source.width, level);
            copy.height = CopySize(source.height, level);
            if (!PlaceNew(copy))
            {
                return null;
            }
            source.copies[level] = copy;
            copies.Add(copy);
            Draw(copy);
            return copy;
        }

        /// Places a new copy: on a page with room; else — when the copies
        /// nobody holds would make the room — not now, compacting first;
        /// else on a new page.
        private bool PlaceNew(ImageCopy copy)
        {
            if (PlaceOnPages(copy))
            {
                return true;
            }
            int slot = (copy.width + Gutter * 2) * (copy.height + Gutter * 2);
            int unheld = 0;
            for (int i = 0; i < copies.Count; i++)
            {
                ImageCopy other = copies[i];
                if (other.holders.Count == 0)
                {
                    unheld += (other.width + Gutter * 2) * (other.height + Gutter * 2);
                }
            }
            if (unheld >= slot || pageCount == MaxPages)
            {
                if (unheld > 0)
                {
                    compactionPending = true;
                    context.needsPaint = true;
                }
                else if (!warnedFull)
                {
                    warnedFull = true;
                    Debug.LogWarning("[MenUI] the image atlas is full (" + MaxPages + " pages): some images are not drawn.");
                }
                return false;
            }
            int needed = Mathf.Max(copy.width, copy.height) + Gutter * 2;
            AddPage(needed > SmallPage || pageCount > 0 ? LargePage : SmallPage);
            return PlaceOnPages(copy);
        }

        private bool PlaceOnPages(ImageCopy copy)
        {
            for (int p = 0; p < pageCount; p++)
            {
                int x, y;
                if (pages[p].Place(copy.width + Gutter * 2, copy.height + Gutter * 2, out x, out y))
                {
                    copy.page = p;
                    copy.x = x + Gutter;
                    copy.y = y + Gutter;
                    return true;
                }
            }
            copy.page = -1;
            return false;
        }

        private void AddPage(int size)
        {
            ImagePage page = new ImagePage();
            page.size = size;
#if !MENUI_HEADLESS
            if (renderer != null)
            {
                page.texture = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                page.texture.filterMode = FilterMode.Bilinear;
                page.texture.wrapMode = TextureWrapMode.Clamp;
                renderer.SetImagePage(pageCount, page.texture);
            }
#endif
            pages[pageCount] = page;
            pageCount++;
        }

        /// Drops the copies nobody holds and packs the rest again (largest
        /// first), drawing the moved ones anew; their holders repaint.
        internal void Compact()
        {
            compactionPending = false;
            List<ImageCopy> kept = new List<ImageCopy>();
            List<ImageSource> dropped = new List<ImageSource>();
            for (int i = 0; i < copies.Count; i++)
            {
                ImageCopy copy = copies[i];
                if (copy.holders.Count > 0)
                {
                    kept.Add(copy);
                }
                else
                {
                    copy.source.copies[copy.level] = null;
                    copy.page = -1;
                    if (!dropped.Contains(copy.source))
                    {
                        dropped.Add(copy.source);
                    }
                }
            }
            // Tallest first packs shelves tightest (an insertion sort: few copies).
            for (int i = 1; i < kept.Count; i++)
            {
                ImageCopy moving = kept[i];
                int j = i - 1;
                while (j >= 0 && (kept[j].height < moving.height
                    || (kept[j].height == moving.height && kept[j].width < moving.width)))
                {
                    kept[j + 1] = kept[j];
                    j--;
                }
                kept[j + 1] = moving;
            }
            copies.Clear();
            for (int p = 0; p < pageCount; p++)
            {
                pages[p].Clear();
            }
            for (int i = 0; i < kept.Count; i++)
            {
                ImageCopy copy = kept[i];
                if (PlaceOnPages(copy))
                {
                    copies.Add(copy);
                    Draw(copy);
                }
                else
                {
                    copy.source.copies[copy.level] = null;
                }
                for (int h = 0; h < copy.holders.Count; h++)
                {
                    copy.holders[h].MarkPaintDirty();
                }
                if (copy.page < 0)
                {
                    copy.holders.Clear();
                }
            }
            for (int i = 0; i < waiting.Count; i++)
            {
                waiting[i].MarkPaintDirty();
            }
            waiting.Clear();
            // A downloaded image no copy is left of gives its texture back
            // (it is downloaded again if it is drawn again).
            for (int i = 0; i < dropped.Count; i++)
            {
                ImageSource source = dropped[i];
                if (source.url != null && source.state == ImageSource.StateLoaded && !HasCopies(source)
                    && context.unloadImage != null)
                {
                    context.unloadImage(source);
                }
            }
            // Pages left empty at the end give their memory back.
            while (pageCount > 0 && pages[pageCount - 1].copies == 0)
            {
                pageCount--;
                FreePage(pages[pageCount]);
                pages[pageCount] = null;
#if !MENUI_HEADLESS
                if (renderer != null)
                {
                    renderer.SetImagePage(pageCount, null);
                }
#endif
            }
        }

        private static bool HasCopies(ImageSource source)
        {
            for (int level = 0; level < ImageSource.Levels; level++)
            {
                if (source.copies[level] != null)
                {
                    return true;
                }
            }
            return false;
        }

        /// The copy's region in its page's uv, inset by half a texel (v up):
        /// left, bottom, right, top.
        internal Vector4 RegionOf(ImageCopy copy)
        {
            float size = pages[copy.page].size;
            return new Vector4((copy.x + 0.5f) / size, (copy.y + 0.5f) / size,
                (copy.x + copy.width - 0.5f) / size, (copy.y + copy.height - 0.5f) / size);
        }

        private void Draw(ImageCopy copy)
        {
            copiesMade++;
#if !MENUI_HEADLESS
            ImagePage page = pages[copy.page];
            if (page.texture == null)
            {
                return;
            }
            ImageSource source = copy.source;
            float size = page.size;
            copyMaterial.SetVector("_DestinationRect", new Vector4(copy.x / size, copy.y / size,
                (copy.x + copy.width) / size, (copy.y + copy.height) / size));
            copyMaterial.SetVector("_SourceRect", new Vector4(source.u0, source.v0, source.u1, source.v1));
            // Source texels per copied texel, and the mip level that box
            // filters half of that (four taps do the rest).
            float ratio = Mathf.Max((float)source.width / copy.width, (float)source.height / copy.height);
            copyMaterial.SetFloat("_LevelOfDetail", Mathf.Max(0f, Mathf.Log(ratio, 2f) - 1f));
            copyMaterial.SetVector("_Footprint", new Vector4((source.u1 - source.u0) / copy.width,
                (source.v1 - source.v0) / copy.height, 0f, 0f));
            VRCGraphics.Blit(source.texture, page.texture, copyMaterial);
#endif
        }

        private static void FreePage(ImagePage page)
        {
#if !MENUI_HEADLESS
            if (page.texture != null)
            {
                page.texture.Release();
                Object.Destroy(page.texture);
                page.texture = null;
            }
#endif
        }

        /// Frees every page (ReleasePanel).
        internal void Release()
        {
            for (int p = 0; p < pageCount; p++)
            {
                FreePage(pages[p]);
                pages[p] = null;
            }
            pageCount = 0;
            copies.Clear();
            waiting.Clear();
        }
    }
}
