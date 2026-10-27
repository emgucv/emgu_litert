//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.IO;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Path = System.IO.Path;

namespace Emgu.LiteRT.LM.Maui.Features
{
    /// <summary>
    /// Exporting a saved chat with its attachments as a ZIP, shared through the platform's share sheet: an action on
    /// each chat's row (<see cref="Action"/>, for <see cref="ChatPageOptions.ChatActions"/>) and a button in the
    /// settings for the chat open now. The ZIP is written by the app's exporter (Emgu.LiteRT.LM.Chat.Sqlite's
    /// <c>ChatExporter.ExportAsync</c> over its <c>ChatHistoryStore</c>: a Markdown transcript with the pictures,
    /// recordings and the attached document's saved content) into <see cref="ExportFolder"/>, where it stays for at
    /// least a day for the receiving app to read.
    /// </summary>
    public sealed class ExportFeature : ChatFeature
    {
        private readonly Func<int, Stream, CancellationToken, Task<int>> _exporter;
        private bool _exportingChat;
        private CancellationTokenSource _chatExportCancel;

        /// <summary>Create the feature.</summary>
        /// <param name="exporter">Writes a chat's ZIP to a stream, given its id; returns how many attachments were missing (e.g. <c>(id, stream, cancel) => ChatExporter.ExportAsync(store, id, stream, cancel)</c>)</param>
        public ExportFeature(Func<int, Stream, CancellationToken, Task<int>> exporter)
        {
            _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        }

        /// <summary>Where the ZIPs are made, under the app's cache folder (a storage manager may clear it).</summary>
        public static string ExportFolder => Path.Combine(FileSystem.CacheDirectory, "sharing-root", "chat-exports");

        /// <inheritdoc/>
        public override string Name => "export";

        /// <inheritdoc/>
        public override string Title => "Export";

        /// <inheritdoc/>
        public override bool PersistEnabled => false;

        /// <summary>Whether a chat is being exported (storage changes and private mode should wait).</summary>
        public bool IsExporting => _exportingChat;

        /// <summary>The "Export" button on a chat's row.</summary>
        public ChatAction Action => new ChatAction("Export", entry => ExportChatAsync(entry.Id));

        /// <inheritdoc/>
        public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

        /// <inheritdoc/>
        public override void OnDetached()
        {
            _chatExportCancel?.Cancel();
            base.OnDetached();
        }

        /// <inheritdoc/>
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
                    ChatPage.SectionTitle("Export"),
                    export,
                    ChatPage.Note("A ZIP with the chat as Markdown and its pictures and recordings, to share or keep. Saved chats also have an Export button in the chat list.")
                }
            };
        }

        /// <summary>Export a saved chat and offer the ZIP through the share sheet (nothing in a private chat, or while busy).</summary>
        /// <param name="id">The chat's id</param>
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
                string root = ExportFolder;
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
                        missing = await _exporter(id, stream, cancel);
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
}
