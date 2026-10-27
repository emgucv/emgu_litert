//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Globalization;
using System.IO;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Controls.Shapes;

namespace Emgu.LiteRT.LM.Maui.Features;

/// <summary>
/// Documents the agent writes (create_document, Emgu.LiteRT.LM.Agents' <see cref="DocumentWriter"/>: Word, PDF or Markdown
/// from the model's Markdown): a card above the reply with Open and Share. A page that saves the chats itself keeps them
/// with the reply as attachments of kind <see cref="ChatWrittenDocuments.AttachmentKind"/> (<see cref="Take"/>; the file
/// name in the media type, "...; name=Packing list.docx") and shows them again when the chat is reopened
/// (<see cref="SavedCard"/>), never given back to the model. A tool group that comes when the message asks for a file,
/// with <see cref="MeetingFeature"/>'s tool when that feature is on the page (its notes are a document too).
/// </summary>
public sealed class WrittenDocumentsFeature : ChatFeature
{
    /// <summary>The kind of a reply's saved attachment that is a document it wrote.</summary>
    public const string Kind = ChatWrittenDocuments.AttachmentKind;

    private readonly List<GeneratedDocument> _sendDocuments = new List<GeneratedDocument>();
    private string _prompt = string.Empty;

    /// <inheritdoc/>
    public override string Name => "written_documents";

    /// <inheritdoc/>
    public override string Title => "Writing documents";

    /// <summary>Always on (nothing to switch).</summary>
    public override bool PersistEnabled => false;

    /// <inheritdoc/>
    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    private MeetingFeature Meetings => Host?.Services.Get<MeetingFeature>();

    /// <inheritdoc/>
    public override Task PrepareAsync(ChatMessageContext context)
    {
        _prompt = context?.Prompt ?? string.Empty;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override IEnumerable<ToolGroup> ToolGroups()
    {
        yield return new ToolGroup(ChatWrittenDocuments.GroupName, ChatWrittenDocuments.GroupSummary, ChatWrittenDocuments.GroupKeywords,
            () => new AITool[] { AgentTools.CreateDocument(SaveAsync, () => _prompt) }.Concat(Meetings?.Tools(Host?.HandsFree == true) ?? Array.Empty<AITool>()),
            () => Meetings?.Tools(Host?.HandsFree == true).Any() == true
                ? ChatWrittenDocuments.Instructions + "\n\n" + ChatWrittenDocuments.MeetingInstructions
                : ChatWrittenDocuments.Instructions);
    }

    /// <summary>create_document's save: the document is kept for the reply being written, with a card above it.</summary>
    /// <param name="document">The document</param>
    /// <param name="cancellationToken">Unused</param>
    /// <returns>What the model is told</returns>
    public Task<string> SaveAsync(GeneratedDocument document, CancellationToken cancellationToken)
    {
        lock (_sendDocuments)
            _sendDocuments.Add(document);
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            Host.AddCardToReply(CreateCard(document.FileName, document.MediaType, document.Format, document.Data));
            return AgentTools.DocumentCreatedResult(document);
        });
    }

    /// <summary>The documents written for the reply, as attachments to save with it (kind, media type with the file name, bytes).</summary>
    public List<(string Kind, string MediaType, byte[] Data)> Take()
    {
        lock (_sendDocuments)
            return _sendDocuments.Select(d => (Kind, d.MediaType + "; name=" + d.FileName, d.Data)).ToList();
    }

    /// <summary>Forget the documents written for the reply (before the next message).</summary>
    public void Clear()
    {
        lock (_sendDocuments)
            _sendDocuments.Clear();
    }

    /// <inheritdoc/>
    public override void OnNewConversation() => Clear();

    /// <inheritdoc/>
    public override void OnReplyCompleted(ChatExchangeEventArgs e)
    {
        // A page that saves the chats itself took them with the reply; the next reply starts afresh.
        Clear();
    }

    /// <summary>A document a saved reply wrote, for above it again when the chat is reopened: its name and type from the media type.</summary>
    /// <param name="mediaType">The saved attachment's media type, with the file name ("...; name=...")</param>
    /// <param name="data">The file</param>
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
        Color accent = ChatResources.FindColor("ChatAccent");
        Button Action(string text, string description)
        {
            var button = new Button
            {
                Text = text,
                FontSize = 14,
                BackgroundColor = Colors.Transparent,
                TextColor = accent,
                BorderColor = accent,
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
        Label status = new Label { IsVisible = false };
        ChatResources.ApplyStyle(status, "ChatListDetailStyle");
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
        var title = new Label { Text = "\U0001F4C4  " + fileName };
        ChatResources.ApplyStyle(title, "ChatListTitleStyle");
        var detail = new Label { Text = format + " · " + SizeText(data.Length) };
        ChatResources.ApplyStyle(detail, "ChatListDetailStyle");
        var content = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                title,
                detail,
                new HorizontalStackLayout { Spacing = 10, Margin = new Thickness(0, 6, 0, 0), Children = { open, share } },
                status
            }
        };
        var card = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(14, 12),
            Margin = new Thickness(6, 2),
            MaximumWidthRequest = 420,
            HorizontalOptions = LayoutOptions.Start,
            Content = content
        };
        ChatResources.SetThemeColor(card, VisualElement.BackgroundColorProperty, "ChatSurface");
        ChatResources.SetThemeColor(card, Border.StrokeProperty, "ChatRowBorder");
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
