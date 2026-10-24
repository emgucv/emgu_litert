// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.IO;
using Path = System.IO.Path;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls.Shapes;

namespace Maui.Demo.Lite
{
    public partial class LiteRtLmChatPage
    {
        private Border _exportChatButton;
        private bool _exportingChat;
        private CancellationTokenSource _chatExportCancel;

        private Border BuildExportChatButton()
        {
            var button = new Border
            {
                WidthRequest = 44, HeightRequest = 44, Stroke = Theme.RowBorder, StrokeThickness = 1,
                BackgroundColor = Theme.CardBackground, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22) },
                VerticalOptions = LayoutOptions.Center, IsVisible = false,
                Content = new Microsoft.Maui.Controls.Shapes.Path
                {
                    Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString("M5 20h14v-2H5v2zm7-18L5.33 8.67l1.42 1.42L11 5.83V16h2V5.83l4.25 4.25 1.42-1.42L12 2z"),
                    Fill = Theme.PrimaryText, WidthRequest = 22, HeightRequest = 22, Aspect = Stretch.Uniform,
                    HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
                }
            };
            button.OnTap(() => { if (_conversationId.HasValue) _ = ExportChatAsync(_conversationId.Value); });
            SemanticProperties.SetDescription(button, "Export this chat with attachments");
            ToolTipProperties.SetText(button, "Export chat with attachments");
            return button;
        }

        private void UpdateExportChatButton()
        {
            if (_exportChatButton == null) return;
            _exportChatButton.IsVisible = !IsPrivateChat && _conversationId.HasValue;
            _exportChatButton.IsEnabled = CanChangeModel && !_exportingChat;
            _exportChatButton.Opacity = _exportChatButton.IsEnabled ? 1 : 0.5;
        }

        private async Task ExportChatAsync(int id)
        {
            if (IsPrivateChat) { SetStatus("End the private chat before exporting saved chats."); return; }
            if (!CanChangeModel || _exportingChat) return;
            _exportingChat = true;
            _chatExportCancel?.Dispose();
            _chatExportCancel = new CancellationTokenSource();
            CancellationToken cancel = _chatExportCancel.Token;
            UpdateExportChatButton();
            string folder = null;
            bool readyToShare = false;
            try
            {
                SetStatus("Preparing chat export...");
                string root = Path.Combine(FileSystem.CacheDirectory, "sharing-root", "chat-exports");
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
                        missing = await ChatExporter.ExportAsync(_store, id, stream, cancel);
                    cancel.ThrowIfCancellationRequested();
                    File.Move(path + ".part", path);
                    return (Path: path, Missing: missing);
                }, cancel);
                cancel.ThrowIfCancellationRequested();
                readyToShare = true;
                SetStatus(result.Missing > 0 ? "Export prepared. Some missing attachments are noted in chat.md." : null);
                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "Export chat with attachments",
                    File = new ShareFile(result.Path, "application/zip"),
                    PresentationSourceBounds = new Rect(Math.Max(0, Width / 2), Math.Max(0, Height / 2), 1, 1)
                });
                // The receiving app can read after RequestAsync returns. Keep the ZIP in cache for at least a day.
            }
            catch (OperationCanceledException) { SetStatus(null); }
            catch (Exception ex) { await DisplayAlertAsync("Couldn't export chat", ex.Message, "OK"); }
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
                UpdateExportChatButton();
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
