// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if WINDOWS || IOS || ANDROID || MACCATALYST
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Emgu.LiteRT.Models;

namespace Maui.Demo.Lite
{
    public partial class LiteRtLmChatPage
    {
        private Switch _privateChatSwitch;
        private Label _privateChatBadge;
        private IDisposable _privateDiagnostics;
        private readonly List<Task> _privateDocumentTasks = new();
        private int _sessionVersion;
        private bool _changingPrivateMode;
        private bool IsPrivateChat => _store.IsPrivate;
        private const string PrivateInstructions = "This is a private chat. Do not claim to remember or save anything for future chats. " +
            "You cannot access saved memories or past conversations. Use only this conversation and the available tools.";

        private View BuildPrivateChatSection()
        {
            _privateChatSwitch = new Switch { OnColor = Theme.Accent };
            _privateChatSwitch.Toggled += OnPrivateChatToggled;
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            row.Add(new Label { Text = "Private chat", FontFamily = Theme.TitleFont, FontSize = 15, TextColor = Theme.PrimaryText,
                VerticalOptions = LayoutOptions.Center }, 0, 0);
            row.Add(_privateChatSwitch, 1, 0);
            SemanticProperties.SetDescription(_privateChatSwitch, "Start or end a private chat that is not saved");
            return new VerticalStackLayout { Spacing = 6, Children = { row, new Label {
                Text = "Starts a fresh chat that is not saved. Memories and past-chat search are unavailable. Pictures and document indexes stay in this session and are discarded when it ends. Web tools still send requests online when enabled; files you explicitly save remain saved.",
                FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText } } };
        }

        private void SetPrivateSwitch(bool value)
        {
            _privateChatSwitch.Toggled -= OnPrivateChatToggled;
            _privateChatSwitch.IsToggled = value;
            _privateChatSwitch.Toggled += OnPrivateChatToggled;
        }

        private async void OnPrivateChatToggled(object sender, ToggledEventArgs e)
        {
            bool requested = e.Value;
            if (_changingPrivateMode || requested == IsPrivateChat) return;
            if (!CanChangeModel || _exportingChat)
            {
                SetPrivateSwitch(IsPrivateChat);
                SetStatus("Wait for active work to finish before changing Private chat.");
                return;
            }
            // Keep the requested value while handling the native switch event. Resetting it here can cause
            // Catalyst to deliver a second change event that immediately reverses the accepted transition.
            _changingPrivateMode = true;
            _privateChatSwitch.IsEnabled = false;
            try
            {
                if (!requested && !await ConfirmEndPrivateChatAsync()) return;
                await ChangePrivateModeAsync(requested);
            }
            finally
            {
                SetPrivateSwitch(IsPrivateChat);
                _privateChatSwitch.IsEnabled = true;
                _changingPrivateMode = false;
            }
        }

        private Task<bool> ConfirmEndPrivateChatAsync()
        {
            if (!ChatMessages.Any() && _pending.Count == 0 && _document == null && string.IsNullOrWhiteSpace(_promptEditor.Text))
                return Task.FromResult(true);
            return DisplayAlertAsync("End private chat?", "This conversation and its session pictures and documents will be discarded. It cannot be reopened.", "End private chat", "Cancel");
        }

        private async Task ChangePrivateModeAsync(bool value)
        {
            _reloadingCpuThreads = true;
            _sessionVersion++;
            try
            {
                // Cancel every producer before awaiting the shared embedding load.
                DocumentState document = _document;
                RemoveDocument();
                _chatIndexing?.Cancel();
                _photoIndexing?.Cancel();
                await StopChatIndexingAsync();
                try { await (_photoIndexTask ?? Task.CompletedTask); } catch (OperationCanceledException) { }
                if (document?.Ready != null) { try { await document.Ready; } catch (OperationCanceledException) { } }
                await FinishPrivateDocumentsAsync();
                StopSpeaking();
                ClearPrivateSessionContent();
                ResetAgent();
                // Drop generated-image prompt/caption caches as well as transcript references.
                if (IsPrivateChat)
                {
                    await _embedderGate.WaitAsync();
                    try { await ReleaseEmbedderAsync(); }
                    finally { _embedderGate.Release(); }
                    await Images.ResetModelAsync();
                }
                _store.IsPrivate = value;
                if (value) { _privateDiagnostics ??= UiStallWatchdog.SuppressPrivateDetails(); }
                else { _privateDiagnostics?.Dispose(); _privateDiagnostics = null; }
                StartNewChat();
                SetPrivateSwitch(value);
                UpdatePrivateChatState();
                SetStatus(value ? "Private chat · not saved. Memories and past-chat search are off." : "Private chat ended. New messages will be saved.");
            }
            catch (Exception ex) { SetStatus("Could not change private-chat mode: " + ex.Message); }
            finally
            {
                _reloadingCpuThreads = false;
                UpdatePrivateChatState();
                if (!IsPrivateChat && _pageVisible) { StartPhotoIndexing(); StartChatIndexing(); }
            }
        }

        private void UpdatePrivateChatState()
        {
            _privateChatBadge.IsVisible = IsPrivateChat;
            _memorySwitch.IsEnabled = !IsPrivateChat;
            _memoryCountLabel.IsVisible = !IsPrivateChat;
            UpdateSearchStorageState();
            UpdateExportChatButton();
            if (!IsPrivateChat) _ = RefreshMemoryCountAsync();
        }

        private void ClearPrivateSessionContent()
        {
            RemoveDocument();
            ClearPendingAttachments();
            _promptEditor.Text = string.Empty;
            Images.ClearDrawn();
            ClearWrittenDocuments();
            _agentBubble = null;
            _statusLabel.Text = string.Empty;
            _statusLabel.IsVisible = false;
            Images.ClearPromptCache();
            _privateDocumentTasks.RemoveAll(t => t.IsCompleted);
        }

        private async Task FinishPrivateDocumentsAsync()
        {
            foreach (Task task in _privateDocumentTasks.ToArray())
                try { await task; } catch (OperationCanceledException) { }
            _privateDocumentTasks.Clear();
        }

        private async Task<bool> LeavePrivateChatAsync()
        {
            if (!IsPrivateChat) return true;
            if (!CanChangeModel || !await ConfirmEndPrivateChatAsync()) return false;
            await ChangePrivateModeAsync(false);
            return !IsPrivateChat;
        }

        private async Task CleanupPrivateChatAsync()
        {
            try
            {
                DocumentState document = _document;
                RemoveDocument();
                await CancelRecordingAsync();
                await FinishPrivateDocumentsAsync();
                // A send may still be querying the document, before its native generation Task even exists.
                while (_busy) await Task.Delay(50);
                await CleanupAfterCpuReloadAsync();
                if (document?.Ready != null) { try { await document.Ready; } catch (OperationCanceledException) { } }
                await FinishPrivateDocumentsAsync();
                ClearPrivateSessionContent();
                _sheet.Dismiss(null);
                ClearTranscript();
            }
            finally { _privateDiagnostics?.Dispose(); _privateDiagnostics = null; }
        }
    }
}
#endif
