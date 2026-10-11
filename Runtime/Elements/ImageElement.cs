// An image (CSS `img`): a texture, or a region of one, drawn in the panel's
// single draw through the image atlas (see ImageAtlas).
//
//   public Texture2D logo;                                   // set in the inspector
//   Image(logo).Width(120)                                   // height follows the aspect ratio
//   Image(icons, new Rect(0, 0, 32, 32)).Size(24, 24)        // one sprite of a sheet
//   Image(photo).Size(64, 64).Fit(ImageFit.Cover).BorderRadius(32) // a round avatar
//   Image(() => playing.Value ? pause : play)                // follows a signal
//
// Sizing is CSS's for a replaced element: with no size, its natural size
// (one texture pixel per logical pixel); with one side set, the other keeps
// the aspect ratio; with both, `Fit` places the image in the box.

using UnityEngine;

namespace MenUI
{
    /// How an image fills its box (CSS `object-fit`).
    public enum ImageFit
    {
        /// Stretched to the box (the default, as in CSS).
        Fill,
        /// As large as fits, whole; the rest of the box stays empty.
        Contain,
        /// Covers the box, cropped to it.
        Cover,
        /// Its natural size, centered, cropped to the box.
        None,
        /// `None`, or `Contain` when that is smaller.
        ScaleDown,
    }

    public sealed class ImageElement : Element
    {
        internal ImageSource source;
        internal ImageFit fit = ImageFit.Fill;
        internal Color tint = Color.white;
        // The atlas copy drawn last (held until the source changes or the
        // element's owner is disposed).
        internal ImageCopy held;
        private int arrangedWidth, arrangedHeight;

        internal ImageElement(MenUIContext context, ImageSource source) : base(context)
        {
            tag = "img";
            this.source = source;
            if (source != null)
            {
                source.elements.Add(this);
            }
            Owner owner = context.runtime.currentOwner;
            if (owner != null)
            {
                owner.OnCleanup(() =>
                {
                    ReleaseCopy();
                    if (this.source != null)
                    {
                        this.source.elements.Remove(this);
                    }
                });
            }
        }

        internal override bool IsLeaf { get { return true; } }

        /// The image's natural size in physical px.
        private float NaturalWidth { get { return source.width * context.scale; } }
        private float NaturalHeight { get { return source.height * context.scale; } }

        internal void SetSource(ImageSource value)
        {
            if (value == source)
            {
                return;
            }
            bool sameSize = value != null && source != null && value.width == source.width && value.height == source.height;
            ReleaseCopy();
            if (source != null)
            {
                source.elements.Remove(this);
            }
            source = value;
            if (source != null)
            {
                source.elements.Add(this);
            }
            if (!sameSize)
            {
                ContentChanged();
            }
            MarkPaintDirty();
        }

        private void ReleaseCopy()
        {
            if (held != null)
            {
                context.images.Release(this, held);
                held = null;
            }
        }

        internal override void MeasureContent(int availableWidth, bool widthDefinite, int innerHeight,
            out int contentWidth, out int contentHeight)
        {
            // Nothing to size by (yet: a download not arrived): the set sizes, or 0.
            if (source == null || !source.HasSize)
            {
                contentWidth = widthDefinite ? availableWidth : 0;
                contentHeight = innerHeight >= 0 ? innerHeight : 0;
                return;
            }
            float naturalWidth = NaturalWidth;
            float naturalHeight = NaturalHeight;
            if (widthDefinite)
            {
                contentWidth = availableWidth;
                contentHeight = innerHeight >= 0 ? innerHeight : Mathf.RoundToInt(availableWidth * naturalHeight / naturalWidth);
            }
            else if (innerHeight >= 0)
            {
                contentHeight = innerHeight;
                contentWidth = Mathf.RoundToInt(innerHeight * naturalWidth / naturalHeight);
            }
            else
            {
                contentWidth = Mathf.CeilToInt(naturalWidth);
                contentHeight = Mathf.CeilToInt(naturalHeight);
            }
        }

        /// An image does not shrink below its size (CSS): its natural width,
        /// or the width a set height gives it.
        internal override int MinContentWidthOfContent()
        {
            if (source == null || !source.HasSize)
            {
                return 0;
            }
            int height = computed.Resolve(StyleProperty.Height, -1, -1);
            if (height >= 0)
            {
                int edges = (int)computed.numbers[StyleProperty.BorderTopWidth] + (int)computed.numbers[StyleProperty.BorderBottomWidth]
                    + computed.Resolve(StyleProperty.PaddingTop, 0, 0) + computed.Resolve(StyleProperty.PaddingBottom, 0, 0);
                return Mathf.RoundToInt(Mathf.Max(0, height - edges) * NaturalWidth / NaturalHeight);
            }
            return Mathf.CeilToInt(NaturalWidth);
        }

        internal override void ArrangeContent(int contentWidth, int contentHeight)
        {
            arrangedWidth = contentWidth;
            arrangedHeight = contentHeight;
        }

        internal override void PaintContent(Painter painter, float opacity)
        {
            if (source == null || !computed.IsVisible || arrangedWidth <= 0 || arrangedHeight <= 0)
            {
                return;
            }
            if (source.state != ImageSource.StateLoaded)
            {
                if (source.state == ImageSource.StateUnloaded)
                {
                    context.RequestImage(source);   // let go meanwhile: download it again
                }
                return;   // drawn when it arrives (ImageSource.Arrived)
            }
            // The image's rectangle in the content box, by `fit` (physical px).
            float boxWidth = arrangedWidth, boxHeight = arrangedHeight;
            float naturalWidth = NaturalWidth, naturalHeight = NaturalHeight;
            float width = boxWidth, height = boxHeight;
            ImageFit mode = fit;
            if (mode == ImageFit.ScaleDown)
            {
                mode = naturalWidth <= boxWidth && naturalHeight <= boxHeight ? ImageFit.None : ImageFit.Contain;
            }
            if (mode == ImageFit.Contain || mode == ImageFit.Cover)
            {
                float scaleX = boxWidth / naturalWidth, scaleY = boxHeight / naturalHeight;
                float scale = mode == ImageFit.Contain ? Mathf.Min(scaleX, scaleY) : Mathf.Max(scaleX, scaleY);
                width = naturalWidth * scale;
                height = naturalHeight * scale;
            }
            else if (mode == ImageFit.None)
            {
                width = naturalWidth;
                height = naturalHeight;
            }
            float left = contentX + (boxWidth - width) * 0.5f;
            float top = contentY + (boxHeight - height) * 0.5f;

            ImageCopy copy = context.images.Acquire(this, source, width, height);
            if (copy != held)
            {
                if (held != null)
                {
                    context.images.Release(this, held);
                }
                held = copy;
            }
            if (copy == null)
            {
                return;   // drawn once there is room (see ImageAtlas.Compact)
            }
            painter.Image(this, new Vector4(contentX, contentY, boxWidth, boxHeight), left, top, width, height, copy, tint, opacity);
        }
    }

    public static class ImageExtensions
    {
        /// How the image fills its box when both sides are set (CSS `object-fit`).
        public static ImageElement Fit(this ImageElement self, ImageFit fit)
        {
            if (self.fit != fit)
            {
                self.fit = fit;
                self.MarkPaintDirty();
            }
            return self;
        }

        /// Multiplies the image's colors (white: as it is). A white icon
        /// tinted takes the tint's color.
        public static ImageElement Tint(this ImageElement self, Color tint)
        {
            if (self.tint != tint)
            {
                self.tint = tint;
                self.MarkPaintDirty();
            }
            return self;
        }
    }
}
