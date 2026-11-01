//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// A picture a <see cref="TextToImageEngine"/> drew: interleaved RGB888 pixels, row by row.
    /// </summary>
    public class TextToImageResult
    {
        /// <summary>The pixels: Height rows of Width x Channels bytes (red, green, blue).</summary>
        public byte[] Rgb { get; set; }

        /// <summary>The width in pixels.</summary>
        public int Width { get; set; }

        /// <summary>The height in pixels.</summary>
        public int Height { get; set; }

        /// <summary>The bytes per pixel (3).</summary>
        public int Channels { get; set; }
    }

    /// <summary>
    /// LiteRT-LM's text to image (its OmniEngine, omni/text2image) for one model, such as Bonsai-Image-ternary-4B or
    /// FLUX.2-klein-4B, loaded from its local .litertlm. It draws square pictures of one size, chosen when it is
    /// created. Use one thread at a time: a picture blocks until it is done and can't be cancelled.
    /// </summary>
    public class TextToImageEngine : Emgu.LiteRT.Util.UnmanagedObject
    {
        /// <summary>
        /// Load a text-to-image model.
        /// </summary>
        /// <param name="modelName">The model's name in LiteRT-LM's registry, "bonsai-flux2" or "flux2-klein"</param>
        /// <param name="modelPath">The model file (.litertlm)</param>
        /// <param name="backend">"cpu", "gpu" or "npu"</param>
        /// <param name="numThreads">The CPU threads, or 0 for the default (4)</param>
        /// <param name="size">The side of the square pictures in pixels (a multiple of 16), or 0 for the model's own
        /// (256 for Bonsai Image and FLUX.2-klein, which refuse any other)</param>
        /// <param name="cacheDir">The folder for the compiled-model caches (about 3.5 GB, named after the graphs, so
        /// each model wants a folder of its own), or null for the model file's folder</param>
        public TextToImageEngine(String modelName, String modelPath, String backend = "cpu", int numThreads = 0, int size = 0,
            String cacheDir = null)
        {
            if (String.IsNullOrEmpty(modelName))
                throw new ArgumentNullException("modelName");
            if (String.IsNullOrEmpty(modelPath))
                throw new ArgumentNullException("modelPath");
            if (String.Equals(backend, "gpu", StringComparison.OrdinalIgnoreCase))
                Emgu.LiteRT.Util.AcceleratorPlugins.Load();
            OmniInvoke.CheckError(OmniInvoke.omniextern_image_engine_create(LiteRtLmInvoke.ToUtf8(modelName),
                LiteRtLmInvoke.ToUtf8(modelPath), LiteRtLmInvoke.ToUtf8(backend ?? "cpu"), numThreads, size,
                LiteRtLmInvoke.ToUtf8(cacheDir), out _ptr), "omniextern_image_engine_create");
        }

        /// <summary>
        /// Draw a picture. Blocks until it is done (seconds to minutes).
        /// </summary>
        /// <param name="prompt">What to draw</param>
        /// <param name="steps">The denoising steps, or 0 for the model's default (4)</param>
        /// <param name="seed">The starting noise: the same prompt and seed give the same picture; 0 for the model's
        /// default (42)</param>
        /// <returns>The picture</returns>
        public TextToImageResult Generate(String prompt, int steps = 0, ulong seed = 0)
        {
            if (String.IsNullOrEmpty(prompt))
                throw new ArgumentNullException("prompt");
            IntPtr rgb;
            int width, height, channels;
            LiteRtLmStatusCode status = OmniInvoke.omniextern_image_generate(_ptr, LiteRtLmInvoke.ToUtf8(prompt), steps,
                seed, out rgb, out width, out height, out channels);
            OmniInvoke.CheckError(status, "omniextern_image_generate");
            try
            {
                byte[] pixels = new byte[(long)width * height * channels];
                Marshal.Copy(rgb, pixels, 0, pixels.Length);
                return new TextToImageResult { Rgb = pixels, Width = width, Height = height, Channels = channels };
            }
            finally
            {
                OmniInvoke.omniextern_free_buffer(rgb);
            }
        }

        /// <summary>Release the engine.</summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                OmniInvoke.omniextern_image_engine_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }
}
