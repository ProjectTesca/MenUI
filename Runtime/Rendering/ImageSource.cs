// What an image element draws: a texture embedded in the world (ready at
// once), or one downloaded from the web (see WebImages) that becomes ready
// later — or fails. Its state is reactive: `IsLoaded`, `HasFailed` and
// `Error` can drive a placeholder or a message.

using System.Collections.Generic;
using UnityEngine;

namespace MenUI
{
    /// Why an image did not load (VRChat's image download errors).
    public enum ImageErrorKind
    {
        Unknown,
        InvalidUrl,
        /// Not on the trusted list, and the player does not allow untrusted URLs.
        AccessDenied,
        DownloadError,
        /// Not a PNG or JPEG, or larger than 2048 x 2048.
        InvalidImage,
        TooManyRequests,
    }

    public sealed class ImageError
    {
        public readonly ImageErrorKind Kind;
        public readonly string Message;

        public ImageError(ImageErrorKind kind, string message)
        {
            Kind = kind;
            Message = message;
        }

        public override string ToString()
        {
            return Kind + ": " + Message;
        }
    }

    /// An image MenUI can draw: a texture, or a region of one (a sprite
    /// sheet), embedded or downloaded. Sizes are in the texture's pixels,
    /// shown as logical pixels.
    public sealed class ImageSource
    {
        internal const int Levels = 12;
        internal const int StateLoaded = 0;
        internal const int StateLoading = 1;
        internal const int StateFailed = 2;
        // Downloaded once, its texture since let go (nothing showed it):
        // drawing it downloads it again.
        internal const int StateUnloaded = 3;

        internal Texture texture;
        // The region in the texture's uv (v up): left, bottom, right, top.
        internal readonly float u0, v0, u1, v1;
        // 0 until a downloaded image first arrives.
        internal int width, height;
        internal readonly ImageCopy[] copies = new ImageCopy[Levels];

        internal int state;
        // Null for an embedded image (always loaded).
        private readonly Signal<int> stateSignal;
        internal ImageError error;
        /// The URL of a downloaded image (null: embedded).
        internal readonly string url;
        // The image elements showing it: they size and draw again when it
        // arrives.
        internal readonly List<ImageElement> elements = new List<ImageElement>();

        /// An embedded texture (or a region of it).
        internal ImageSource(Texture texture, float u0, float v0, float u1, float v1, int width, int height)
        {
            this.texture = texture;
            this.u0 = u0;
            this.v0 = v0;
            this.u1 = u1;
            this.v1 = v1;
            this.width = Mathf.Max(1, width);
            this.height = Mathf.Max(1, height);
            state = StateLoaded;
        }

        /// A downloaded image, not there yet.
        internal ImageSource(ReactiveRuntime runtime, string url)
        {
            this.url = url;
            u1 = 1f;
            v1 = 1f;
            state = StateLoading;
            stateSignal = new Signal<int>(runtime, StateLoading);
        }

        /// Its natural size: the texture's pixels (the region's, for a part);
        /// 0 until a downloaded image arrives.
        public int Width { get { return width; } }
        public int Height { get { return height; } }

        /// Whether it can be drawn (reactive: a binding that reads it reruns
        /// when the download completes).
        public bool IsLoaded
        {
            get
            {
                int now = stateSignal != null ? stateSignal.Value : state;
                return now == StateLoaded || now == StateUnloaded;
            }
        }

        /// Whether the download failed (reactive); `Error` says why.
        public bool HasFailed
        {
            get
            {
                int now = stateSignal != null ? stateSignal.Value : state;
                return now == StateFailed;
            }
        }

        public ImageError Error { get { return error; } }

        /// Whether its size is known (a downloaded image that arrived once).
        internal bool HasSize { get { return width > 0 && height > 0; } }

        internal bool Is(Texture texture, Rect pixels)
        {
            if (url != null || this.texture != texture)
            {
                return false;
            }
            if (pixels.width <= 0f)
            {
                return u0 == 0f && v0 == 0f && u1 == 1f && v1 == 1f;
            }
            return width == (int)pixels.width && height == (int)pixels.height
                && u0 == pixels.x / texture.width && v1 == 1f - pixels.y / texture.height;
        }

        /// A download arrived: its elements size and draw again.
        internal void Arrived(Texture arrived, int arrivedWidth, int arrivedHeight)
        {
            bool resized = arrivedWidth != width || arrivedHeight != height;
            texture = arrived;
            width = Mathf.Max(1, arrivedWidth);
            height = Mathf.Max(1, arrivedHeight);
            SetState(StateLoaded);
            for (int i = 0; i < elements.Count; i++)
            {
                if (resized)
                {
                    elements[i].ContentChanged();
                }
                elements[i].MarkPaintDirty();
            }
        }

        internal void SetState(int value)
        {
            state = value;
            if (stateSignal != null)
            {
                stateSignal.Value = value;
            }
        }
    }
}
