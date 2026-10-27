//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Chat.Sqlite;

/// <summary>
/// Exports a saved chat as a ZIP: a portable Markdown transcript (chat.md, with the thinking, the voice messages'
/// transcripts and the document excerpts given to the model) with relative links to its attachments (the attached and
/// generated pictures and the recordings), and the saved content of its attached document (document/document.md and
/// its images; the original file isn't kept by the store). No database, credentials, memories or embeddings.
/// </summary>
public static class ChatExporter
{
    /// <summary>Write the chat's ZIP to a stream (left open).</summary>
    /// <param name="store">The saved chats</param>
    /// <param name="chatId">The chat's id</param>
    /// <param name="output">Where the ZIP goes</param>
    /// <param name="cancel">Cancels it</param>
    /// <returns>How many attachments and document images were missing (noted in the Markdown)</returns>
    /// <exception cref="InvalidOperationException">The chat no longer exists</exception>
    public static async Task<int> ExportAsync(ChatHistoryStore store, int chatId, Stream output, CancellationToken cancel = default)
    {
        if (store == null)
            throw new ArgumentNullException(nameof(store));
        ChatRecord chat = await store.GetChatAsync(chatId).ConfigureAwait(false)
            ?? throw new InvalidOperationException("This saved chat no longer exists.");
        List<ChatMessageRecord> messages = await store.GetMessagesAsync<ChatMessageRecord>(chatId).ConfigureAwait(false);
        cancel.ThrowIfCancellationRequested();
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var markdown = new StringBuilder();
        markdown.AppendLine("# " + Inline(chat.Title ?? "Chat")).AppendLine();
        markdown.AppendLine("- Model: " + Inline(chat.Model));
        markdown.AppendLine("- Created (UTC): " + Date(chat.Created));
        markdown.AppendLine("- Updated (UTC): " + Date(chat.Updated)).AppendLine();
        markdown.AppendLine("Exported from Emgu LiteRT. Dates below are UTC. Includes saved messages, thinking, document excerpts, and available attachments.").AppendLine();
        int missing = 0;
        foreach (var message in messages)
        {
            cancel.ThrowIfCancellationRequested();
            string role = message.Role == ChatMessageRecord.UserRole ? "You"
                : message.Role == ChatMessageRecord.AssistantRole ? "Assistant" : "Tool step";
            markdown.AppendLine("## " + role + " · " + Date(message.Created)).AppendLine();
            if (!string.IsNullOrWhiteSpace(message.Thinking))
                Details(markdown, "Thinking", message.Thinking);
            markdown.AppendLine(message.Text ?? string.Empty).AppendLine();
            if (!string.IsNullOrWhiteSpace(message.Transcript))
                markdown.AppendLine("> Voice message, as written down: " + message.Transcript.Replace("\n", " ")).AppendLine();
            if (!string.IsNullOrWhiteSpace(message.Context))
                Details(markdown, "Document excerpts given to the model", message.Context);
            int number = 0;
            List<ChatSavedAttachment> attachments = store.GetAttachments(message, includeMissing: true);
            int references = (message.Attachments ?? string.Empty).Split('\n').Count(s => !string.IsNullOrWhiteSpace(s));
            if (references > attachments.Count)
            {
                missing += references - attachments.Count;
                markdown.AppendLine("> Some saved attachment references are invalid and could not be exported.").AppendLine();
            }
            foreach (var attachment in attachments)
            {
                cancel.ThrowIfCancellationRequested();
                string path = "attachments/message-" + message.Id.ToString(CultureInfo.InvariantCulture) + "-" + (++number).ToString(CultureInfo.InvariantCulture)
                    + Extension(attachment.MediaType);
                string label = attachment.Kind == "generated" ? "Generated image" : attachment.Kind == "image" ? "Attached image"
                    : attachment.Kind == "audio" ? "Audio recording" : "Attachment";
                try
                {
                    using var input = File.OpenRead(attachment.Path);
                    using var target = archive.CreateEntry(path, CompressionLevel.Fastest).Open();
                    await input.CopyToAsync(target, 81920, cancel).ConfigureAwait(false);
                    bool image = attachment.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
                    markdown.AppendLine((image ? "!" : "") + "[" + label + "](" + path + ")").AppendLine();
                }
                catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
                {
                    missing++;
                    markdown.AppendLine("> " + label + " unavailable: the saved file is missing.").AppendLine();
                }
            }
        }
        if (!string.IsNullOrEmpty(chat.DocumentId))
        {
            DocumentRecord document = await store.GetDocumentAsync(chat.DocumentId).ConfigureAwait(false);
            if (document != null)
            {
                List<DocumentItemRecord> items = await store.GetDocumentItemsAsync(document.Id).ConfigureAwait(false);
                var content = new StringBuilder("# " + Inline(document.Name)).AppendLine().AppendLine();
                content.AppendLine("Saved document content only. The original document file was not retained by the app; this is not a copy of the original PDF.").AppendLine();
                foreach (var item in items)
                {
                    cancel.ThrowIfCancellationRequested();
                    content.AppendLine("## " + Inline(item.Location ?? "Passage " + (item.Position + 1))).AppendLine();
                    if (item.IsImage && item.Data?.Length > 0)
                    {
                        string path = "image-" + item.Id.ToString(CultureInfo.InvariantCulture) + Extension(item.MediaType);
                        using var target = archive.CreateEntry("document/" + path, CompressionLevel.Fastest).Open();
                        await target.WriteAsync(item.Data, 0, item.Data.Length, cancel).ConfigureAwait(false);
                        content.AppendLine("![Document image](" + path + ")").AppendLine();
                    }
                    else if (item.IsImage)
                    {
                        missing++;
                        content.AppendLine("> The saved document image is unavailable.").AppendLine();
                    }
                    else content.AppendLine(item.Text ?? string.Empty).AppendLine();
                }
                await WriteTextAsync(archive, "document/document.md", content.ToString(), cancel).ConfigureAwait(false);
                markdown.AppendLine("## Attached document").AppendLine();
                markdown.AppendLine("[" + Inline(document.Name) + "](document/document.md) — saved extracted text and images; original file not retained.").AppendLine();
            }
            else
            {
                missing++;
                markdown.AppendLine("> The attached document is unavailable. Excerpts saved with messages are included above.").AppendLine();
            }
        }
        await WriteTextAsync(archive, "chat.md", markdown.ToString(), cancel).ConfigureAwait(false);
        return missing;
    }

    private static async Task WriteTextAsync(ZipArchive archive, string path, string text, CancellationToken cancel)
    {
        using var stream = archive.CreateEntry(path, CompressionLevel.Fastest).Open();
        byte[] bytes = new UTF8Encoding(false).GetBytes(text);
        await stream.WriteAsync(bytes, 0, bytes.Length, cancel).ConfigureAwait(false);
    }

    // Keep metadata and user reasoning as text even if they contain Markdown delimiters or HTML.
    private static string Inline(string text)
    {
        var result = new StringBuilder();
        foreach (char c in text ?? string.Empty)
        {
            if ("\\`*_{}[]()#+-.!|>".Contains(c)) result.Append('\\');
            result.Append(c == '<' ? "&lt;" : c == '&' ? "&amp;" : c == '\r' || c == '\n' ? " " : c.ToString());
        }
        return result.ToString();
    }

    private static void Details(StringBuilder markdown, string title, string text)
    {
        // A fence longer than any run of backticks in the content cannot be closed by the content.
        int longest = 0, run = 0;
        foreach (char c in text) { run = c == '`' ? run + 1 : 0; longest = Math.Max(longest, run); }
        string fence = new string('`', Math.Max(3, longest + 1));
        markdown.AppendLine("<details><summary>" + title + "</summary>").AppendLine();
        markdown.AppendLine(fence + "text").AppendLine(text).AppendLine(fence).AppendLine();
        markdown.AppendLine("</details>").AppendLine();
    }

    private static string Date(DateTime date) => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    private static string Extension(string mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "image/png" => ".png", "image/jpeg" => ".jpg", "image/webp" => ".webp", "image/gif" => ".gif",
        "audio/wav" or "audio/x-wav" => ".wav", "audio/mpeg" => ".mp3", "audio/flac" => ".flac",
        "audio/mp4" => ".m4a", "application/pdf" => ".pdf", _ => ".bin"
    };
}
