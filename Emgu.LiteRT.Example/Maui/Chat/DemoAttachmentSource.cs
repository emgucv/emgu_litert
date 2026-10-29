// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui;
using Microsoft.Extensions.AI;

namespace Maui.Demo.Lite;

// The demo's "+" menu for Emgu.LiteRT.LM.Maui's ChatView (IChatAttachmentSource): a document (PDF, Word, text - attached
// to the chat, not the message: DocumentsFeature), a photo from the library or the camera, the sample images, an audio
// file or the sample recording. Images are decoded here (any format the platform's picker returns that OpenCV reads),
// downscaled and re-encoded as JPEG, which LiteRT-LM's image decoder (stb) reads. In a private chat the picker's
// temporary copies are deleted after reading.
internal sealed class DemoAttachmentSource : IChatAttachmentSource
{
    // The files "+" > Document offers: what Emgu.LiteRT.LM.Agents.Documents' DocumentReader reads (PDF, Word, text), by each platform's names.
    private static readonly FilePickerFileType DocumentFileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        { DevicePlatform.iOS, new[] { "com.adobe.pdf", "org.openxmlformats.wordprocessingml.document", "public.plain-text" } },
        { DevicePlatform.MacCatalyst, new[] { "com.adobe.pdf", "org.openxmlformats.wordprocessingml.document", "public.plain-text" } },
        { DevicePlatform.Android, new[] { "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "text/plain", "text/markdown" } },
        { DevicePlatform.WinUI, new[] { ".pdf", ".docx", ".txt", ".md" } },
    });

    // LiteRT-LM decodes audio with miniaudio, which reads WAV, MP3 and FLAC (not AAC/M4A).
    private static readonly FilePickerFileType AudioFileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        { DevicePlatform.iOS, new[] { "com.microsoft.waveform-audio", "public.mp3", "org.xiph.flac" } },
        { DevicePlatform.MacCatalyst, new[] { "com.microsoft.waveform-audio", "public.mp3", "org.xiph.flac" } },
        { DevicePlatform.Android, new[] { "audio/wav", "audio/x-wav", "audio/mpeg", "audio/flac", "audio/x-flac" } },
        { DevicePlatform.WinUI, new[] { ".wav", ".mp3", ".flac" } },
    });

    // Sample inputs bundled with the app (Resources/Raw, plus LiteRT-LM's audio test sample).
    private static readonly string[] SampleImages = { "surfers.jpg", "tulips.jpg", "space_shuttle.jpg" };
    private const string SampleAudio = "audio_sample.wav";

    // Images are downscaled to this size before sending: the vision encoder resizes them anyway, and smaller images
    // keep the base64-encoded message small.
    private const int MaxImageSize = 1024;

    private readonly Func<string, IReadOnlyList<(string Section, string Glyph, string Text, string Value)>, Task<string>> _showMenu;
    private readonly Func<bool> _isPrivate;
    private readonly Func<int> _sessionVersion;
    private readonly Action<byte[], string> _attachDocument;
    private readonly Action<string> _setStatus;

    public DemoAttachmentSource(Func<string, IReadOnlyList<(string Section, string Glyph, string Text, string Value)>, Task<string>> showMenu,
        Func<bool> isPrivate, Func<int> sessionVersion, Action<byte[], string> attachDocument, Action<string> setStatus)
    {
        _showMenu = showMenu;
        _isPrivate = isPrivate;
        _sessionVersion = sessionVersion;
        _attachDocument = attachDocument;
        _setStatus = setStatus;
    }

    public async Task<IReadOnlyList<DataContent>> PickAsync(ChatAttachmentKinds kinds, View anchor)
    {
        int sessionVersion = _sessionVersion();
        bool privateRead = _isPrivate();
        var picked = new List<DataContent>();

        var rows = new List<(string Section, string Glyph, string Text, string Value)>();
        rows.Add(("DOCUMENT (PDF, WORD, TEXT)", Theme.GlyphText, "Document", "document"));
        if (kinds.HasFlag(ChatAttachmentKinds.Image))
        {
            rows.Add(("IMAGE", Theme.GlyphImage, "Photo Library", "library"));
            // A picture that isn't in the photo library - a scan or a download on a desktop.
            rows.Add((null, Theme.GlyphImage, "Image File", "imagefile"));
            if (MediaPicker.Default.IsCaptureSupported)
                rows.Add((null, Theme.GlyphCamera, "Take Photo", "camera"));
            foreach (string sample in SampleImages)
                rows.Add((null, Theme.GlyphImage, "Sample: " + System.IO.Path.GetFileNameWithoutExtension(sample).Replace('_', ' '), "image:" + sample));
        }
        if (kinds.HasFlag(ChatAttachmentKinds.Audio))
        {
            rows.Add(("AUDIO (WAV, MP3, FLAC)", Theme.GlyphPlay, "Audio File", "audio"));
            rows.Add((null, Theme.GlyphPlay, "Sample: speech recording", "sampleaudio"));
        }

        string action = await _showMenu("Attach", rows);
        if (sessionVersion != _sessionVersion() || string.IsNullOrEmpty(action))
            return picked;

        try
        {
            if (action == "document")
            {
                FileResult file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a document", FileTypes = DocumentFileTypes });
                if (file != null)
                    _attachDocument(await ReadAllBytesAsync(file, sessionVersion, privateRead), file.FileName);
            }
            else if (action == "library")
            {
                FileResult file = (await MediaPicker.Default.PickPhotosAsync())?.FirstOrDefault();
                if (file != null)
                    AddImage(picked, await ReadAllBytesAsync(file, sessionVersion, privateRead), file.FileName);
            }
            else if (action == "imagefile")
            {
                FileResult file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose an image", FileTypes = FilePickerFileType.Images });
                if (file != null)
                    AddImage(picked, await ReadAllBytesAsync(file, sessionVersion, privateRead), file.FileName);
            }
            else if (action == "camera")
            {
                // The device's camera app; the photo is downscaled and re-encoded like a library photo (OpenCV
                // applies its EXIF orientation when decoding).
                FileResult file = await MediaPicker.Default.CapturePhotoAsync();
                if (file != null)
                    AddImage(picked, await ReadAllBytesAsync(file, sessionVersion, privateRead), "Photo");
            }
            else if (action.StartsWith("image:"))
            {
                string sample = action.Substring("image:".Length);
                AddImage(picked, await ImageUtil.ReadAppFileAsync(sample), sample);
            }
            else if (action == "audio")
            {
                FileResult file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose an audio file", FileTypes = AudioFileTypes });
                if (file != null)
                    picked.Add(Audio(await ReadAllBytesAsync(file, sessionVersion, privateRead), file.FileName));
            }
            else if (action == "sampleaudio")
            {
                picked.Add(Audio(await ImageUtil.ReadAppFileAsync(SampleAudio), SampleAudio));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _setStatus("Could not attach the file: " + ex.Message);
        }
        return picked;
    }

    private async Task<byte[]> ReadAllBytesAsync(FileResult file, int sessionVersion, bool privateRead)
    {
        try
        {
            if (sessionVersion != _sessionVersion()) throw new OperationCanceledException();
            using System.IO.Stream stream = await file.OpenReadAsync();
            using System.IO.MemoryStream ms = new System.IO.MemoryStream();
            await stream.CopyToAsync(ms);
            if (sessionVersion != _sessionVersion()) throw new OperationCanceledException();
            return ms.ToArray();
        }
        finally
        {
            if (privateRead)
                try { StorageFiles.DeleteTemporaryFile(FileSystem.CacheDirectory, file.FullPath); }
                catch (Exception ex) { _setStatus("Could not remove a temporary picker copy: " + ex.Message); }
        }
    }

    private void AddImage(List<DataContent> picked, byte[] bytes, string name)
    {
        UiStallWatchdog.Mark(string.Format("Attach image {0}: {1} bytes (decode on UI thread)", name, bytes.Length));
        Emgu.CV.Mat image = ImageUtil.Decode(bytes);
        if (image == null)
        {
            _setStatus("That file could not be read as an image.");
            return;
        }
        byte[] jpeg;
        // Downscale disposes the original when it returns a smaller copy.
        using (Emgu.CV.Mat small = ImageUtil.Downscale(image, MaxImageSize))
        using (Emgu.CV.Util.VectorOfByte buffer = new Emgu.CV.Util.VectorOfByte())
        {
            Emgu.CV.CvInvoke.Imencode(".jpg", small, buffer);
            jpeg = buffer.ToArray();
        }
        picked.Add(new DataContent(jpeg, "image/jpeg") { Name = name });
    }

    private static DataContent Audio(byte[] bytes, string name) => new DataContent(bytes, AudioMediaType(name)) { Name = name };

    private static string AudioMediaType(string fileName)
    {
        switch (System.IO.Path.GetExtension(fileName)?.ToLowerInvariant())
        {
            case ".mp3": return "audio/mpeg";
            case ".flac": return "audio/flac";
            default: return "audio/wav";
        }
    }
}
