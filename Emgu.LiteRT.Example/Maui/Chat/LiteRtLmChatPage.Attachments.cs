//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls.Shapes;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Util;
using Emgu.LiteRT.LM.Extensions.AI;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Maui;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using Emgu.LiteRT.LM.Chat.Sqlite;

// Attaching images, audio and documents to the next message.
namespace Maui.Demo.Lite;

public partial class LiteRtLmChatPage
{
    // ---------- Attachments ----------

    private async void OnAttachClicked()
    {
        if (_busy || _reloadingCpuThreads)
            return;
        int sessionVersion = _sessionVersion;
        bool privateRead = IsPrivateChat;

        var rows = new List<(string Section, string Glyph, string Text, string Value)>();
        rows.Add(("DOCUMENT (PDF, WORD, TEXT)", Theme.GlyphText, "Document", "document"));
        if (SelectedOption.AcceptsImages)
        {
            rows.Add(("IMAGE", Theme.GlyphImage, "Photo Library", "library"));
            if (MediaPicker.Default.IsCaptureSupported)
                rows.Add((null, Theme.GlyphCamera, "Take Photo", "camera"));
            foreach (string sample in SampleImages)
                rows.Add((null, Theme.GlyphImage, "Sample: " + System.IO.Path.GetFileNameWithoutExtension(sample).Replace('_', ' '), "image:" + sample));
        }
        if (SelectedOption.AcceptsAudio)
        {
            rows.Add(("AUDIO (WAV, MP3, FLAC)", Theme.GlyphPlay, "Audio File", "audio"));
            rows.Add((null, Theme.GlyphPlay, "Sample: speech recording", "sampleaudio"));
        }

        string action = await _sheet.ShowAsync("Attach", rows);
        if (sessionVersion != _sessionVersion || string.IsNullOrEmpty(action))
            return;

        try
        {
            if (action == "document")
            {
                FileResult file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a document", FileTypes = DocumentReader.FileTypes });
                if (file != null)
                    AttachDocument(await ReadAllBytesAsync(file, sessionVersion, privateRead), file.FileName);
            }
            else if (action == "library")
            {
                FileResult file = (await MediaPicker.Default.PickPhotosAsync())?.FirstOrDefault();
                if (file != null)
                    AddPendingImage(await ReadAllBytesAsync(file, sessionVersion, privateRead), file.FileName);
            }
            else if (action == "camera")
            {
                // The device's camera app; the photo is downscaled and re-encoded like a library photo (OpenCV
                // applies its EXIF orientation when decoding).
                FileResult file = await MediaPicker.Default.CapturePhotoAsync();
                if (file != null)
                    AddPendingImage(await ReadAllBytesAsync(file, sessionVersion, privateRead), "Photo");
            }
            else if (action.StartsWith("image:"))
            {
                string sample = action.Substring("image:".Length);
                AddPendingImage(await ImageUtil.ReadAppFileAsync(sample), sample);
            }
            else if (action == "audio")
            {
                FileResult file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose an audio file", FileTypes = AudioFileTypes });
                if (file != null)
                    AddPendingAudio(await ReadAllBytesAsync(file, sessionVersion, privateRead), file.FileName);
            }
            else if (action == "sampleaudio")
            {
                AddPendingAudio(await ImageUtil.ReadAppFileAsync(SampleAudio), SampleAudio);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SetStatus("Could not attach the file: " + ex.Message);
        }
    }

    private async Task<byte[]> ReadAllBytesAsync(FileResult file, int sessionVersion, bool privateRead)
    {
        try
        {
            if (sessionVersion != _sessionVersion) throw new OperationCanceledException();
            using System.IO.Stream stream = await file.OpenReadAsync();
            using System.IO.MemoryStream ms = new System.IO.MemoryStream();
            await stream.CopyToAsync(ms);
            if (sessionVersion != _sessionVersion) throw new OperationCanceledException();
            return ms.ToArray();
        }
        finally
        {
            if (privateRead)
                try { StorageFiles.DeleteTemporaryFile(FileSystem.CacheDirectory, file.FullPath); }
                catch (Exception ex) { SetStatus("Could not remove a temporary picker copy: " + ex.Message); }
        }
    }

    // Decode the image here (any format the platform's photo picker returns that OpenCV reads), downscale it
    // and re-encode it as JPEG, which LiteRT-LM's image decoder (stb) reads.
    private void AddPendingImage(byte[] bytes, string name)
    {
        UiStallWatchdog.Mark(string.Format("Attach image {0}: {1} bytes (decode on UI thread)", name, bytes.Length));
        Emgu.CV.Mat image = ImageUtil.Decode(bytes);
        if (image == null)
        {
            SetStatus("That file could not be read as an image.");
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
        AddPending(new PendingAttachment { Attachment = ChatAttachment.Image(jpeg), Image = jpeg, Data = jpeg, MediaType = "image/jpeg", Name = name });
    }

    private void AddPendingAudio(byte[] bytes, string name)
    {
        AddPending(new PendingAttachment { Attachment = ChatAttachment.Audio(bytes), Data = bytes, MediaType = AudioMediaType(name), Name = name });
    }

    private static string AudioMediaType(string fileName)
    {
        switch (System.IO.Path.GetExtension(fileName)?.ToLowerInvariant())
        {
            case ".mp3": return "audio/mpeg";
            case ".flac": return "audio/flac";
            default: return "audio/wav";
        }
    }

    private void AddPending(PendingAttachment attachment)
    {
        _pending.Add(attachment);
        SetStatus(null);

        Border chip = AttachmentChip(attachment, false);
        var remove = new Label
        {
            Text = Theme.GlyphClose,
            FontFamily = Theme.IconFont,
            FontSize = 18,
            TextColor = Theme.SecondaryText,
            VerticalOptions = LayoutOptions.Center
        };
        ((HorizontalStackLayout)chip.Content).Children.Add(remove);
        remove.OnTap(() =>
        {
            if (!_busy)
                RemovePending(attachment);
        });

        attachment.Chip = chip;
        _attachmentStrip.Children.Add(chip);
        _attachmentScroll.IsVisible = true;
    }

    private void RemovePending(PendingAttachment attachment)
    {
        _pending.Remove(attachment);
        _attachmentStrip.Children.Remove(attachment.Chip);
        _attachmentScroll.IsVisible = _pending.Count > 0;
    }

    private void ClearPendingAttachments()
    {
        _pending.Clear();
        _attachmentStrip.Children.Clear();
        _attachmentScroll.IsVisible = false;
    }

    // A thumbnail (image) or an icon and file name (audio); larger in the transcript than in the composer.
    private static Border AttachmentChip(PendingAttachment attachment, bool inTranscript)
    {
        var content = new HorizontalStackLayout { Spacing = 6 };
        if (attachment.Image != null)
        {
            byte[] image = attachment.Image;
            content.Children.Add(new Image
            {
                Source = ImageSource.FromStream(() => new System.IO.MemoryStream(image)),
                HeightRequest = inTranscript ? 140 : 48,
                MaximumWidthRequest = inTranscript ? 240 : 96,
                Aspect = Aspect.AspectFit
            });
        }
        else
        {
            content.Children.Add(Theme.MakeIcon(Theme.GlyphPlay, Theme.Accent, 20));
            content.Children.Add(new Label
            {
                Text = attachment.Name,
                FontFamily = Theme.BodyFont,
                FontSize = 13,
                TextColor = Theme.PrimaryText,
                LineBreakMode = LineBreakMode.MiddleTruncation,
                MaximumWidthRequest = 180,
                VerticalOptions = LayoutOptions.Center
            });
        }
        return new Border
        {
            BackgroundColor = inTranscript ? Colors.White : Theme.TileBackground,
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Padding = attachment.Image != null ? new Thickness(4) : new Thickness(8, 6),
            HorizontalOptions = LayoutOptions.End,
            Content = content
        };
    }
}
