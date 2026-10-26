//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Globalization;
using System.IO;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Maui;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Controls.Shapes;

namespace Maui.Demo.Lite;

// Documents the agent writes (create_document, Emgu.LiteRT.LM.Agents' DocumentWriter: Word, PDF or Markdown from the
// model's Markdown): a card above the reply with Open and Share, saved with the reply as an attachment of kind
// "document" (its file name in the media type, "...; name=Packing list.docx"), shown again when the chat is reopened and,
// like the generated pictures, never given back to the model. A tool group of the router, with the meeting notes' tools
// (they write a document too).
internal sealed class WrittenDocumentsFeature : DemoFeature
{
    public const string Kind = "document";
    private readonly List<GeneratedDocument> _sendDocuments = new List<GeneratedDocument>();

    public const string Instructions = "When the user asks for a document or a file (Word, PDF, Markdown), write the whole " +
        "content into create_document's content in Markdown - not into your reply - then say in one short sentence that it is ready.";

    public override string Name => "written_documents";

    public override string Title => "Writing documents";

    public override bool PersistEnabled => false;

    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    private MeetingFeature Meetings => Host?.Services.Get<MeetingFeature>();

    public override IEnumerable<ToolGroup> ToolGroups() => new[]
    {
        new ToolGroup(ChatToolGroups.WriteDocument, ChatToolGroups.WriteDocumentSummary, ChatToolGroups.WriteDocumentKeywords,
            () => new AITool[] { AgentTools.CreateDocument(SaveAsync, () => Prompt) }.Concat(Meetings?.Tools(Host?.HandsFree == true) ?? Array.Empty<AITool>()),
            () => JoinInstructions(Instructions, Meetings?.Tools(Host?.HandsFree == true).Any() == true ? MeetingFeature.Instructions : null))
    };

    // create_document's save: kept for the reply being written, with a card above it.
    public Task<string> SaveAsync(GeneratedDocument document, CancellationToken cancellationToken)
    {
        lock (_sendDocuments)
            _sendDocuments.Add(document);
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            Host.AddCardToReply(CreateCard(document.FileName, document.MediaType, document.Format, document.Data));
            UiStallWatchdog.Mark("create_document: " + document.FileName + ", " + document.Data.Length + " bytes");
            return AgentTools.DocumentCreatedResult(document);
        });
    }

    // The documents written for the reply, to save with it.
    public List<(string Kind, string MediaType, byte[] Data)> Take()
    {
        lock (_sendDocuments)
            return _sendDocuments.Select(d => (Kind, d.MediaType + "; name=" + d.FileName, d.Data)).ToList();
    }

    public void Clear()
    {
        lock (_sendDocuments)
            _sendDocuments.Clear();
    }

    public override void OnNewConversation() => Clear();

    // A document a saved reply wrote, for above it again when the chat is reopened: its name and type from the media type.
    public View SavedCard(string mediaType, byte[] data)
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
        return CreateCard(name, type, format, data);
    }

    private static string SizeText(long bytes) =>
        bytes < 1024 ? bytes + " bytes" : bytes < 1024 * 1024 ? (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB"
            : (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

    // The card above the reply bubble (or at the end of a restored transcript).
    private View CreateCard(string fileName, string mediaType, string format, byte[] data)
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
        return card;
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
