//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Microsoft.Maui.Controls.Shapes;

namespace Maui.Demo.Lite;

// Documents the agent writes (create_document, Emgu.LiteRT.LM.Agents' DocumentWriter: Word, PDF or Markdown from the
// model's Markdown): a card above the reply with Open and Share, saved with the reply as an attachment of kind
// "document" (its file name in the media type, "...; name=Packing list.docx"), shown again when the chat is reopened and,
// like the generated pictures, never given back to the model.
public partial class LiteRtLmChatPage
{
    private const string DocumentKind = "document";
    private readonly List<GeneratedDocument> _sendDocuments = new List<GeneratedDocument>();

    private const string DocumentWritingInstructions = "When the user asks for a document or a file (Word, PDF, Markdown), write the whole " +
        "content into create_document's content in Markdown - not into your reply - then say in one short sentence that it is ready.";

    private Task<string> SaveWrittenDocumentAsync(GeneratedDocument document, CancellationToken cancellationToken)
    {
        lock (_sendDocuments)
            _sendDocuments.Add(document);
        StreamingBubble bubble = _agentBubble;
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            AddDocumentCard(bubble, document.FileName, document.MediaType, document.Format, document.Data);
            UiStallWatchdog.Mark("create_document: " + document.FileName + ", " + document.Data.Length + " bytes");
            return AgentTools.DocumentCreatedResult(document);
        });
    }

    private List<(string Kind, string MediaType, byte[] Data)> TakeWrittenDocuments()
    {
        lock (_sendDocuments)
            return _sendDocuments.Select(d => (DocumentKind, d.MediaType + "; name=" + d.FileName, d.Data)).ToList();
    }

    private void ClearWrittenDocuments()
    {
        lock (_sendDocuments)
            _sendDocuments.Clear();
    }

    // A saved chat's document: its name and type from the media type.
    private void AddSavedDocumentCard(string mediaType, byte[] data)
    {
        string type = mediaType, name = "Document";
        int parameter = mediaType.IndexOf(';');
        if (parameter >= 0)
        {
            type = mediaType.Substring(0, parameter).Trim();
            string rest = mediaType.Substring(parameter + 1).Trim();
            if (rest.StartsWith("name=", StringComparison.Ordinal))
                name = rest.Substring(5);
        }
        string format = type == DocumentWriter.PdfMediaType ? "PDF" : type == DocumentWriter.MarkdownMediaType ? "Markdown" : "Word";
        AddDocumentCard(null, name, type, format, data);
    }

    private static string SizeText(long bytes) =>
        bytes < 1024 ? bytes + " bytes" : bytes < 1024 * 1024 ? (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB"
            : (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

    // The card above the reply bubble (or at the end of a restored transcript).
    private void AddDocumentCard(StreamingBubble bubble, string fileName, string mediaType, string format, byte[] data)
    {
        Button Action(string text, string description)
        {
            var button = new Button
            {
                Text = text,
                FontFamily = Theme.TitleFont,
                FontSize = 14,
                BackgroundColor = Colors.Transparent,
                TextColor = Theme.Accent,
                BorderColor = Theme.Accent,
                BorderWidth = 1,
                CornerRadius = 12,
                Padding = new Thickness(16, 6),
                MinimumHeightRequest = 40
            };
            SemanticProperties.SetDescription(button, description);
            return button;
        }
        Button open = Action("Open", "Open " + fileName);
        Button share = Action("Share", "Share " + fileName);
        Label status = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText, IsVisible = false };
        open.Clicked += async (_, _) =>
        {
            try
            {
                string path = await WriteDocumentFileAsync(fileName, data);
                if (!await Launcher.Default.OpenAsync(new OpenFileRequest(fileName, new ReadOnlyFile(path, mediaType))))
                    throw new InvalidOperationException("No app can open it.");
            }
            catch (Exception ex)
            {
                status.Text = "Couldn't open it: " + ex.Message;
                status.IsVisible = true;
            }
        };
        share.Clicked += async (_, _) =>
        {
            try
            {
                string path = await WriteDocumentFileAsync(fileName, data);
                await Share.Default.RequestAsync(new ShareFileRequest(fileName, new ShareFile(path, mediaType)));
            }
            catch (Exception ex)
            {
                status.Text = "Couldn't share it: " + ex.Message;
                status.IsVisible = true;
            }
        };
        var content = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { Text = "\U0001F4C4  " + fileName, FontFamily = Theme.TitleFont, FontSize = 15, TextColor = Theme.PrimaryText },
                new Label { Text = format + " · " + SizeText(data.Length), FontFamily = Theme.BodyFont, FontSize = 13, TextColor = Theme.SecondaryText },
                new HorizontalStackLayout { Spacing = 10, Margin = new Thickness(0, 6, 0, 0), Children = { open, share } },
                status
            }
        };
        var card = new Border
        {
            BackgroundColor = Theme.CardBackground,
            Stroke = Theme.RowBorder,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(14, 12),
            Margin = new Thickness(6, 2),
            MaximumWidthRequest = 420,
            HorizontalOptions = LayoutOptions.Start,
            Content = content
        };
        SemanticProperties.SetDescription(card, "Document " + fileName + ", " + format);
        int index = bubble != null ? _transcript.Children.IndexOf(bubble.Row) : -1;
        if (index < 0)
            _transcript.Children.Add(card);
        else
            _transcript.Children.Insert(index, card);
        ScrollToEnd();
    }

    // Open and Share need a file: written to the cache, in a folder of its own so the name stays as it is.
    private static async Task<string> WriteDocumentFileAsync(string fileName, byte[] data)
    {
        string folder = System.IO.Path.Combine(FileSystem.CacheDirectory, "documents", Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(folder);
        string path = System.IO.Path.Combine(folder, fileName);
        await File.WriteAllBytesAsync(path, data);
        return path;
    }
}

#endif
