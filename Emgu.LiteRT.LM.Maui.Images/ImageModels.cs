//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Globalization;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.LM.Maui.Images;

/// <summary>A picture an <see cref="IImageModel"/> drew.</summary>
/// <param name="Rgb">Interleaved RGB888 pixels, row by row</param>
/// <param name="Width">The width in pixels</param>
/// <param name="Height">The height in pixels</param>
/// <param name="Elapsed">How long it took</param>
public sealed record ImageModelPicture(byte[] Rgb, int Width, int Height, TimeSpan Elapsed);

/// <summary>
/// An image model <see cref="ImageFeature"/> draws with: downloads its files, loads, and draws a picture for a prompt.
/// <see cref="ZImageTurboImageModel"/> and <see cref="OmniImageModel"/> (LiteRT-LM's Bonsai Image and FLUX.2-klein)
/// are the ones this package has.
/// </summary>
public interface IImageModel : IDisposable
{
    /// <summary>The name to show, e.g. "Bonsai Image 4B".</summary>
    string Name { get; }

    /// <summary>The picture size to show, e.g. "512 x 512".</summary>
    string SizeLabel { get; }

    /// <summary>The denoising steps a picture takes.</summary>
    int Steps { get; }

    /// <summary>The backend it runs on, "cpu" or "gpu".</summary>
    string Backend { get; }

    /// <summary>The download's size in bytes.</summary>
    long DownloadSize { get; }

    /// <summary>About how many bytes the first picture prepares beside the download (caches).</summary>
    long CacheSize { get; }

    /// <summary>The bytes downloaded so far, partial files included.</summary>
    long DownloadedBytes { get; }

    /// <summary>Whether everything is downloaded (cheap: it doesn't read the files).</summary>
    bool IsDownloaded { get; }

    /// <summary>The folder its files are in, kept out of iCloud backups on iOS.</summary>
    string Folder { get; }

    /// <summary>Whether a picture reports its steps through <see cref="GenerateAsync"/>'s stage callback (else only its start).</summary>
    bool ReportsSteps { get; }

    /// <summary>Download what is missing, reporting (bytes done, total bytes).</summary>
    Task DownloadAsync(Action<long, long> progress, CancellationToken cancel);

    /// <summary>Load the model with this many CPU threads.</summary>
    Task InitAsync(int threads);

    /// <summary>
    /// About how long the first load takes on this device, in words ("up to about 10 minutes"), or null when it isn't
    /// long: LiteRT-LM compiles the model's graphs for the backend on the first load and keeps them in the caches, so
    /// later loads take seconds (a Windows PC's GPU: about 10 minutes for Bonsai Image 4B on an RTX 3070).
    /// </summary>
    string FirstLoadEstimate { get; }

    /// <summary>Whether the caches the first load makes exist (they are gone when the model's files are deleted).</summary>
    bool CachesExist { get; }

    /// <summary>Why a prompt is too long for the model (a sentence for the chat model), or null when it fits.</summary>
    string CheckPrompt(string prompt);

    /// <summary>Draw a picture, telling stage the step it is at (e.g. "Step 2 of 8"), on a worker thread.</summary>
    Task<ImageModelPicture> GenerateAsync(string prompt, long seed, Action<string> stage, CancellationToken cancel);

    /// <summary>Forget cached prompt encodings (a private chat began or ended).</summary>
    void ClearPromptCache();
}

/// <summary>Z-Image-Turbo (Emgu.LiteRT.Models' <see cref="ZImageTurbo"/>): 256 x 256, 8 steps, on the CPU.</summary>
public sealed class ZImageTurboImageModel : IImageModel
{
    private const int StepCount = 8;
    private readonly ZImageTurbo _model;

    /// <summary>Create it over the model in its default folder.</summary>
    public ZImageTurboImageModel()
    {
        _model = new ZImageTurbo();
    }

    /// <inheritdoc/>
    public string Name => "Z-Image-Turbo";
    /// <inheritdoc/>
    public string SizeLabel => string.Format(CultureInfo.InvariantCulture, "{0} x {0}", ZImageTurbo.ImageSize);
    /// <inheritdoc/>
    public int Steps => StepCount;
    /// <inheritdoc/>
    public string Backend => "cpu";
    /// <inheritdoc/>
    public long DownloadSize => ZImageTurbo.DownloadSize;
    /// <inheritdoc/>
    public long CacheSize => 9_000_000_000L;
    /// <inheritdoc/>
    public long DownloadedBytes => _model.DownloadedBytes;
    /// <inheritdoc/>
    public bool IsDownloaded => _model.IsDownloaded;
    /// <inheritdoc/>
    public string Folder => _model.Folder;
    /// <inheritdoc/>
    public bool ReportsSteps => true;

    /// <inheritdoc/>
    public Task DownloadAsync(Action<long, long> progress, CancellationToken cancel) =>
        _model.Download(new InlineProgress<ZImageDownloadProgress>(p => progress(p.BytesDone, p.TotalBytes)), cancel);

    /// <inheritdoc/>
    public Task InitAsync(int threads)
    {
        _model.NumThreads = threads;
        return _model.Init();
    }

    /// <inheritdoc/>
    public string FirstLoadEstimate => null;

    /// <inheritdoc/>
    public bool CachesExist => true;

    /// <inheritdoc/>
    public string CheckPrompt(string prompt)
    {
        int tokens = _model.CountPromptTokens(prompt);
        return tokens <= ZImageTurbo.MaxPromptTokens ? null : string.Format(CultureInfo.InvariantCulture,
            "The description is too long for the image model ({0} tokens, at most {1} fit). Call generate_image again with a shorter English description of about 15 words, keeping the subject and the style.",
            tokens, ZImageTurbo.MaxPromptTokens);
    }

    /// <inheritdoc/>
    public async Task<ImageModelPicture> GenerateAsync(string prompt, long seed, Action<string> stage, CancellationToken cancel)
    {
        ZImageResult result = await _model.Generate(prompt, seed, StepCount,
            new InlineProgress<ZImageProgress>(p => { if (p.Stage != "Done") stage(p.Stage); }), cancel);
        return new ImageModelPicture(result.Rgb, result.Width, result.Height, result.Elapsed);
    }

    /// <inheritdoc/>
    public void ClearPromptCache() => _model.ClearPromptCache();

    /// <inheritdoc/>
    public void Dispose() => _model.Dispose();
}

/// <summary>
/// A text-to-image model on LiteRT-LM's OmniEngine (Emgu.LiteRT.LM.Models' <see cref="TextToImageModel"/>: Bonsai
/// Image 4B, FLUX.2-klein 4B). A picture is drawn in one call: no steps to report, and stopping it lets it finish in
/// the background.
/// </summary>
public sealed class OmniImageModel : IImageModel
{
    private readonly TextToImageModel _model;
    private readonly string _backend;
    private readonly long _cacheSize;

    /// <summary>Create it over a model.</summary>
    /// <param name="model">The model (owned: disposed with this)</param>
    /// <param name="backend">"cpu" or "gpu"</param>
    /// <param name="cacheSize">About how many bytes its compiled-model caches take, for asking before the download</param>
    public OmniImageModel(TextToImageModel model, string backend = "cpu", long cacheSize = 0)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _backend = backend ?? "cpu";
        _cacheSize = cacheSize;
    }

    /// <summary>The model.</summary>
    public TextToImageModel Model => _model;

    /// <inheritdoc/>
    public string Name => _model.DisplayName;
    /// <inheritdoc/>
    public string SizeLabel => string.Format(CultureInfo.InvariantCulture, "{0} x {0}", _model.ImageSize);
    /// <inheritdoc/>
    public int Steps => _model.DefaultSteps;
    /// <inheritdoc/>
    public string Backend => _model.Backend ?? _backend;
    /// <inheritdoc/>
    public long DownloadSize => _model.DownloadSize;
    /// <inheritdoc/>
    public long CacheSize => _cacheSize;
    /// <inheritdoc/>
    public long DownloadedBytes => _model.ModelFile.IsLocalFileVerified ? _model.DownloadSize : _model.ModelFile.PartialDownloadBytes;
    /// <inheritdoc/>
    public bool IsDownloaded => _model.ModelFile.IsLocalFileVerified;
    /// <inheritdoc/>
    public string Folder => _model.ModelFile.LocalFolder;
    /// <inheritdoc/>
    public bool ReportsSteps => false;

    /// <inheritdoc/>
    public async Task DownloadAsync(Action<long, long> progress, CancellationToken cancel)
    {
        long total = _model.DownloadSize;
        Emgu.LiteRT.Util.FileDownloadManager.DownloadProgressChangedEventHandler handler =
            (t, received, percent) => progress(received, t > 0 ? t.Value : total);
        _model.OnDownloadProgressChanged += handler;
        try
        {
            await _model.Download(cancel);
        }
        finally
        {
            _model.OnDownloadProgressChanged -= handler;
        }
    }

    /// <inheritdoc/>
    public string FirstLoadEstimate
    {
        get
        {
            if (!string.Equals(_backend, "gpu", StringComparison.OrdinalIgnoreCase))
                return "a few minutes";
            // Measured: Bonsai Image 4B on an RTX 3070 (WebGPU) 9-10 minutes; a Mac's Metal about 30 s, a phone's OpenCL about a minute.
            return Microsoft.Maui.Devices.DeviceInfo.Current.Platform == Microsoft.Maui.Devices.DevicePlatform.WinUI
                ? "up to about 10 minutes" : "about a minute";
        }
    }

    /// <inheritdoc/>
    public bool CachesExist
    {
        get
        {
            string folder = _model.CacheFolder;
            return System.IO.Directory.Exists(folder) && System.Linq.Enumerable.Any(System.IO.Directory.EnumerateFileSystemEntries(folder));
        }
    }

    /// <summary>Why the GPU couldn't load the model, when it fell back to the CPU; null otherwise.</summary>
    public string GpuFailure { get; private set; }

    // A GPU load that fails (e.g. an 8 GB phone's GPU runs out of memory for the FP32 graphs: a Galaxy S10's Adreno 640
    // did) falls back to the CPU, which draws the same pictures, slower.
    /// <inheritdoc/>
    public async Task InitAsync(int threads)
    {
        if (!string.Equals(_backend, "gpu", StringComparison.OrdinalIgnoreCase))
        {
            await _model.Init(_backend, threads);
            return;
        }
        try
        {
            await _model.Init("gpu", threads);
        }
        catch (LiteRtLmException ex)
        {
            GpuFailure = ex.ErrorMessage ?? ex.Message;
            await _model.Init("cpu", threads);
        }
    }

    // Both models encode the prompt with Qwen3 over 256-512 tokens: a chat model's description always fits.
    /// <inheritdoc/>
    public string CheckPrompt(string prompt) => null;

    /// <inheritdoc/>
    public async Task<ImageModelPicture> GenerateAsync(string prompt, long seed, Action<string> stage, CancellationToken cancel)
    {
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        TextToImageResult result = await _model.GenerateAsync(prompt, (ulong)Math.Max(1, seed), 0, cancel);
        return new ImageModelPicture(result.Rgb, result.Width, result.Height, watch.Elapsed);
    }

    /// <inheritdoc/>
    public void ClearPromptCache()
    {
    }

    /// <inheritdoc/>
    public void Dispose() => _model.Dispose();
}

// IProgress without Progress<T>'s capture of the current SynchronizationContext (none on a worker thread): the handler
// runs where Report is called.
internal sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public InlineProgress(Action<T> handler)
    {
        _handler = handler;
    }

    public void Report(T value) => _handler(value);
}
