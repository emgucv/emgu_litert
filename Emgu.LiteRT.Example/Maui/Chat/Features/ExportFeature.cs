// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.IO;
using Path = System.IO.Path;
using Emgu.LiteRT.LM.Maui;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Maui.Demo.Lite;

// Exporting a saved chat with its attachments as a ZIP (ChatExporter), shared through the platform's share sheet: an
// action on each chat's row (Action, for ChatPageOptions.ChatActions) and a button in the settings for the chat open now.
internal sealed class ExportFeature : DemoFeature
{
    private bool _exportingChat;
    private CancellationTokenSource _chatExportCancel;

    public override string Name => "export";

    public override string Title => "Export";

    public override bool PersistEnabled => false;

    // Whether a chat is being exported (storage changes and private mode wait).
    public bool IsExporting => _exportingChat;

    // The "Export" button on a chat's row.
    public ChatAction Action => new ChatAction("Export", entry => ExportChatAsync(entry.Id));

    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    public override void OnDetached()
    {
        _chatExportCancel?.Cancel();
        base.OnDetached();
    }

    public override View BuildSettingsSection()
    {
        var export = new Button { Text = "Export this chat...", HorizontalOptions = LayoutOptions.Fill };
        SemanticProperties.SetDescription(export, "Export this chat with attachments");
        export.Clicked += async (_, _) =>
        {
            if (Host.CurrentChatId is int id)
                await ExportChatAsync(id);
            else
                Host.SetStatus("Nothing to export yet: the chat is saved after its first reply.");
        };
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                Theme.SectionTitle("Export"),
                export,
                Note("A ZIP with the chat as Markdown and its pictures and recordings, to share or keep. Saved chats also have an Export button in the chat list.")
            }
        };
    }

    public async Task ExportChatAsync(int id)
    {
        if (Host.IsPrivate) { Host.SetStatus("End the private chat before exporting saved chats."); return; }
        if (Host.IsBusy || _exportingChat) return;
        _exportingChat = true;
        _chatExportCancel?.Dispose();
        _chatExportCancel = new CancellationTokenSource();
        CancellationToken cancel = _chatExportCancel.Token;
        string folder = null;
        bool readyToShare = false;
        try
        {
            Host.SetStatus("Preparing chat export...");
            string root = Path.Combine(FileSystem.CacheDirectory, "sharing-root", "chat-exports");
            ChatStore store = Demo.Store;
            var result = await Task.Run(async () =>
            {
                Directory.CreateDirectory(root);
                DeleteOldChatExports(root);
                folder = Path.Combine(root, "export-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                string name = "chat-" + id + ".zip";
                string path = Path.Combine(folder, name);
                int missing;
                using (var stream = new FileStream(path + ".part", FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    missing = await ChatExporter.ExportAsync(store, id, stream, cancel);
                cancel.ThrowIfCancellationRequested();
                File.Move(path + ".part", path);
                return (Path: path, Missing: missing);
            }, cancel);
            cancel.ThrowIfCancellationRequested();
            readyToShare = true;
            Host.SetStatus(result.Missing > 0 ? "Export prepared. Some missing attachments are noted in chat.md." : null);
            Size page = Host.PageSize;
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export chat with attachments",
                File = new ShareFile(result.Path, "application/zip"),
                PresentationSourceBounds = new Rect(Math.Max(0, page.Width / 2), Math.Max(0, page.Height / 2), 1, 1)
            });
            // The receiving app can read after RequestAsync returns. Keep the ZIP in cache for at least a day.
        }
        catch (OperationCanceledException) { Host.SetStatus(null); }
        catch (Exception ex) { await Host.AskAsync("Couldn't export chat", ex.Message, "OK"); }
        finally
        {
            if (!readyToShare && folder != null)
            {
                // Only our two known files; no recursive deletion of a computed path here.
                try { File.Delete(Path.Combine(folder, "chat-" + id + ".zip.part")); File.Delete(Path.Combine(folder, "chat-" + id + ".zip")); Directory.Delete(folder); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            _exportingChat = false;
        }
    }

    private static void DeleteOldChatExports(string root)
    {
        string fullRoot = Path.GetFullPath(root);
        foreach (string candidate in Directory.EnumerateDirectories(fullRoot, "export-*"))
        {
            string folder = Path.GetFullPath(candidate);
            string name = Path.GetFileName(folder);
            if (Path.GetDirectoryName(folder) != fullRoot || !name.StartsWith("export-", StringComparison.Ordinal)
                || !Guid.TryParseExact(name.Substring("export-".Length), "N", out _))
                continue;
            try
            {
                if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0
                    && Directory.GetLastWriteTimeUtc(folder) < DateTime.UtcNow.AddDays(-1))
                    Directory.Delete(folder, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
