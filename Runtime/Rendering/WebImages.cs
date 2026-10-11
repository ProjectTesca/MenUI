// Images downloaded from the web, through VRChat's image downloader: one
// source per URL (asking again gives the same one), one downloader per image
// (so each can give its texture back on its own). VRChat queues the
// downloads itself — one every 5 seconds for the whole world — so MenUI asks
// only for what is built, and once per URL.
//
// MenUIApp receives the SDK's OnImageLoadSuccess / OnImageLoadError and hands
// them here. A texture is let go when the image atlas drops the last copy of
// it (nothing has shown it for a while and room was needed); drawing it
// again downloads it again.

using System.Collections.Generic;
using System.Threading.Tasks;
using MenSharp;
using UnityEngine;
using VRC.SDK3.Image;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace MenUI
{
    public sealed class WebImages
    {
        private readonly MenUIContext context;
        private readonly IUdonEventReceiver receiver;
        // Per URL, in step: its source, URL and downloader (null while none).
        private readonly List<ImageSource> sources = new List<ImageSource>();
        private readonly List<VRCUrl> urls = new List<VRCUrl>();
        private readonly List<VRCImageDownloader> downloaders = new List<VRCImageDownloader>();
        // Awaiting a source's outcome, in step.
        private readonly List<ImageSource> awaited = new List<ImageSource>();
        private readonly List<TaskCompletionSource<Result<ImageSource, ImageError>>> awaiting =
            new List<TaskCompletionSource<Result<ImageSource, ImageError>>>();

        internal WebImages(MenUIContext context, IUdonEventReceiver receiver)
        {
            this.context = context;
            this.receiver = receiver;
            context.downloadImage = source => Download(IndexOf(source));
            context.unloadImage = source => Unload(IndexOf(source));
        }

        /// The image at `url`, downloading unless it is here or on its way
        /// (a failed one is tried again).
        internal ImageSource Load(VRCUrl url)
        {
            string key = url != null ? url.Get() : null;
            if (string.IsNullOrEmpty(key))
            {
                ImageSource invalid = new ImageSource(context.runtime, string.Empty);
                invalid.error = new ImageError(ImageErrorKind.InvalidUrl, "empty URL");
                invalid.SetState(ImageSource.StateFailed);
                return invalid;
            }
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].url == key)
                {
                    if (sources[i].state == ImageSource.StateFailed)
                    {
                        Download(i);
                    }
                    return sources[i];
                }
            }
            ImageSource source = new ImageSource(context.runtime, key);
            sources.Add(source);
            urls.Add(url);
            downloaders.Add(null);
            Download(sources.Count - 1);
            return source;
        }

        /// The image's outcome: the source once it is loaded, or why it is not.
        internal Task<Result<ImageSource, ImageError>> WhenLoaded(ImageSource source)
        {
            TaskCompletionSource<Result<ImageSource, ImageError>> completion =
                new TaskCompletionSource<Result<ImageSource, ImageError>>();
            if (source.state == ImageSource.StateLoaded || source.state == ImageSource.StateUnloaded)
            {
                completion.SetResult(new Result<ImageSource, ImageError>.Ok(source));
            }
            else if (source.state == ImageSource.StateFailed)
            {
                completion.SetResult(new Result<ImageSource, ImageError>.Err(source.error));
            }
            else
            {
                awaited.Add(source);
                awaiting.Add(completion);
            }
            return completion.Task;
        }

        private int IndexOf(ImageSource source)
        {
            return sources.IndexOf(source);
        }

        private void Download(int index)
        {
            if (index < 0)
            {
                return;
            }
            ImageSource source = sources[index];
            DisposeDownloader(index);
            VRCImageDownloader downloader = new VRCImageDownloader();
            downloaders[index] = downloader;
            TextureInfo info = new TextureInfo();
            info.GenerateMipMaps = true;   // the atlas copy box-filters from mips
            info.FilterMode = FilterMode.Trilinear;
            info.WrapModeU = TextureWrapMode.Clamp;
            info.WrapModeV = TextureWrapMode.Clamp;
            source.error = null;
            source.SetState(ImageSource.StateLoading);
            downloader.DownloadImage(urls[index], null, receiver, info);
        }

        private void Unload(int index)
        {
            if (index < 0)
            {
                return;
            }
            DisposeDownloader(index);
            sources[index].texture = null;
            sources[index].SetState(ImageSource.StateUnloaded);
        }

        private void DisposeDownloader(int index)
        {
            if (downloaders[index] != null)
            {
                downloaders[index].Dispose();   // frees its texture
                downloaders[index] = null;
            }
        }

        private int IndexOfUrl(IVRCImageDownload result)
        {
            VRCUrl url = result.Url;
            string key = url != null ? url.Get() : null;
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].url == key)
                {
                    return i;
                }
            }
            return -1;
        }

        internal void Succeeded(IVRCImageDownload result)
        {
            int index = IndexOfUrl(result);
            if (index < 0 || sources[index].state != ImageSource.StateLoading)
            {
                return;
            }
            ImageSource source = sources[index];
            Texture2D texture = result.Result;
            source.Arrived(texture, texture.width, texture.height);
            Complete(source, new Result<ImageSource, ImageError>.Ok(source));
        }

        internal void Failed(IVRCImageDownload result)
        {
            int index = IndexOfUrl(result);
            if (index < 0 || sources[index].state != ImageSource.StateLoading)
            {
                return;
            }
            ImageSource source = sources[index];
            DisposeDownloader(index);
            source.error = new ImageError(KindOf(result.Error), result.ErrorMessage);
            source.SetState(ImageSource.StateFailed);
            Debug.LogWarning("[MenUI] image " + source.url + " did not load: " + source.error);
            Complete(source, new Result<ImageSource, ImageError>.Err(source.error));
        }

        private void Complete(ImageSource source, Result<ImageSource, ImageError> outcome)
        {
            for (int i = awaited.Count - 1; i >= 0; i--)
            {
                if (awaited[i] == source)
                {
                    TaskCompletionSource<Result<ImageSource, ImageError>> completion = awaiting[i];
                    awaited.RemoveAt(i);
                    awaiting.RemoveAt(i);
                    completion.SetResult(outcome);
                }
            }
        }

        private static ImageErrorKind KindOf(VRCImageDownloadError error)
        {
            if (error == VRCImageDownloadError.InvalidURL) return ImageErrorKind.InvalidUrl;
            if (error == VRCImageDownloadError.AccessDenied) return ImageErrorKind.AccessDenied;
            if (error == VRCImageDownloadError.DownloadError) return ImageErrorKind.DownloadError;
            if (error == VRCImageDownloadError.InvalidImage) return ImageErrorKind.InvalidImage;
            if (error == VRCImageDownloadError.TooManyRequests) return ImageErrorKind.TooManyRequests;
            return ImageErrorKind.Unknown;
        }

        /// Frees every downloaded texture (ReleasePanel).
        internal void Release()
        {
            for (int i = 0; i < downloaders.Count; i++)
            {
                DisposeDownloader(i);
            }
        }
    }
}
