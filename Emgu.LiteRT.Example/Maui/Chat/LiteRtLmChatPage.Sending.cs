//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

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

// Sending a message and streaming the reply.
namespace Maui.Demo.Lite
{
    public partial class LiteRtLmChatPage
    {
        private void OnThinkingToggled(object sender, ToggledEventArgs e)
        {
            try { Preferences.Default.Set(ThinkPreference, e.Value); } catch (Exception) { }
        }

        // ---------- Sending a message ----------

        private async void OnSendClicked(object sender, EventArgs e)
        {
            if (_busy || _reloadingCpuThreads || _recorder.IsRecording)
                return;

            string prompt = _promptEditor.Text?.Trim();
            List<PendingAttachment> attachments = _pending.ToList();
            if (string.IsNullOrEmpty(prompt))
            {
                if (attachments.Count == 0)
                {
                    if (_document == null)
                        return;
                    prompt = "Summarize this document.";
                }
                // Attachments without a question: ask for the obvious.
                prompt = attachments.Any(a => a.Image != null)
                    ? (attachments.All(a => a.Image != null) ? "Describe this image." : "Describe the image and transcribe the audio.")
                    : "Transcribe this audio.";
            }
            await SendAsync(prompt, attachments);
        }

        // Send a message (prompt may be empty for a voice message: the model answers what was said) and stream
        // the reply into the transcript.
        private async Task SendAsync(string prompt, List<PendingAttachment> attachments)
        {
            ClearCameraTurn();
            Images.ClearDrawn();
            WrittenDocuments.Clear();
            UiStallWatchdog.Mark(string.Format("Send: {0} chars, {1} attachment(s), model {2}, agent {3}, think {4}, web {5}",
                prompt.Length, attachments.Count, SelectedOption.Name, UseAgent, _thinkingSwitch.IsToggled, _webSearchSwitch.IsToggled));
            // With read-aloud on (or in a voice chat) the reply is read while it is written.
            ReplyReader reader = StartReplyReader();
            // Cancelled when the user interrupts the reply (voice chat).
            CancellationTokenSource replyCancel = new CancellationTokenSource();
            _replyCancel = replyCancel;
            SetBusy(true);
            try
            {
                if (!await EnsureModelReadyAsync())
                    return;
                // A model that doesn't take audio is given the words of a voice message instead.
                if (!SelectedOption.AcceptsAudio && attachments.Any(IsVoiceRecording))
                {
                    string heard = await TranscribeVoiceAsync(attachments, replyCancel.Token);
                    if (heard == null)
                        return;
                    prompt = string.IsNullOrWhiteSpace(prompt) ? heard : prompt + " " + heard;
                }

                _promptEditor.Text = string.Empty;
                ClearPendingAttachments();
                // With a document attached: the excerpts (and maybe a picture or page) most related to the message.
                DocumentContext document = await GetDocumentContextAsync(prompt);
                if (document?.Image != null)
                    attachments.Add(document.Image);
                // A voice message the model hears as audio gets its words written down for the transcript (the model doesn't
                // see them), instead of just "Voice message".
                bool transcribe = string.IsNullOrWhiteSpace(prompt) && SpeechRecognizer.IsSupported && attachments.Any(IsVoiceRecording);
                var info = new UserMessageInfo { Language = await TextLanguage.DetectAsync(prompt), Transcribing = transcribe };

                // Task mode: for this message only.
                bool planTask = _taskChip.IsToggled && UseAgent && !_voiceMode;
                _taskChip.IsToggled = false;
                ChatOptions options = await MessageOptionsAsync(prompt, attachments);
                ConfigureConversation();
                // The model is given references to the attached images for the camera tools.
                int attachedImages = attachments.Count(a => a.Image != null);
                string imageReferences = UseAgent && attachedImages > 0 && LiveCamera.Supported
                    ? "Attached image references for analyze_visual_image: " + string.Join(", ", Enumerable.Range(1, attachedImages).Select(i => "image-" + i)) + "\n"
                    : null;
                int compactionsBefore = _conversation.CompactionCount;
                int searchedAt = compactionsBefore;
                ChatMessageItem userItem = null;
                _conversation.BeforeStreaming = async (message, token) =>
                {
                    userItem = message;
                    // Compacting may have summarized or dropped excerpts given before: they are searched again.
                    if (document != null && _conversation.CompactionCount != searchedAt)
                    {
                        searchedAt = _conversation.CompactionCount;
                        document = await RefreshDocumentContextAsync(prompt, document, message, attachments);
                        message.Context = JoinContext(imageReferences, document?.Text);
                    }
                    // Re-reading the earlier conversation (an opened chat, after compacting) is what takes the time.
                    SetStatus(_rereadNext || _conversation.CompactionCount != compactionsBefore ? "Reading the conversation so far..." : "Generating...");
                    _rereadNext = false;
                };
                // The reply's bubble is made when it is added (OnConversationMessagesChanged); these go with it.
                _pendingUserInfo = info;
                _pendingVoice = transcribe ? attachments.First(IsVoiceRecording).Data : null;
                _pendingDocumentStep = document?.Step;
                _pendingReader = reader;

                List<DataContent> contents = attachments.Select(a => new DataContent(a.Data, a.MediaType) { Name = a.Name }).ToList();
                IChatClient client = planTask ? Tasks.CreateClient(prompt, contents) : null;
                ChatMessageItem reply = null;
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        Task<ChatMessageItem> generation = reply == null
                            ? _conversation.SendAsync(prompt, contents, options, JoinContext(imageReferences, document?.Text), replyCancel.Token, client)
                            : _conversation.RegenerateAsync(reply, options, replyCancel.Token, client);
                        // Kept so teardown (OnNavigatedFrom / switching models) can wait for it instead of freeing the
                        // engine while LiteRT-LM is still generating into it.
                        _generation = generation;
                        reply = await generation;
                    }
                    catch (System.ClientModel.ClientResultException ex) when (attempt == 0 && ServerRejectedReasoning(ex))
                    {
                        // The server doesn't take reasoning_effort: send it again without.
                        UiStallWatchdog.Mark("Server rejected reasoning_effort: " + ex.Message);
                        reply = ChatMessages.LastOrDefault(m => m.Role == ChatMessageRole.Assistant);
                        options.Reasoning = null;
                        continue;
                    }
                    if (attempt == 0 && reply.State == ChatMessageState.Done && ToolRouter.TakeRerun() && !replyCancel.IsCancellationRequested)
                    {
                        // A stub ended the request (ToolRouter, rerunAfterStub): the message again, now with its group -
                        // without the stub's turn, which the model handled worse.
                        UiStallWatchdog.Mark("Sending the message again with the tool group: " + string.Join(", ", ToolRouter.Active));
                        options = await MessageOptionsAsync(prompt, attachments);
                        continue;
                    }
                    break;
                }

                bool interrupted = reply.State == ChatMessageState.Interrupted;
                if (interrupted)
                    UiStallWatchdog.Mark("Reply interrupted");
                StreamingBubble bubble = reply.Tag as StreamingBubble;
                if (bubble != null)
                {
                    bubble.TargetLanguage = TranslationFeature.Target(info.Language);
                    bubble.Refresh();
                    _ = Translations.CheckThinkingLanguageAsync(bubble);
                }
                MarkDocumentContextGiven(document);
                UiStallWatchdog.Mark(string.Format("Reply done: {0} chars, {1} chars thinking", reply.Text.Length, reply.Thinking.Length));
                // The camera frames the tools took are given to the model with the conversation from now on.
                foreach (byte[] jpeg in _cameraTurnFrames)
                    userItem?.AddAttachment(new DataContent(jpeg, "image/jpeg"));
                ClearCameraTurn();
                if (userItem != null)
                    await SaveExchangeAsync(userItem, reply, bubble);
                SetStatus(null);
                if (!interrupted)
                    reader?.Finish(reply.Text);
            }
            catch (Exception ex)
            {
                reader?.Stop();
                SetStatus("Error: " + ex.Message);
            }
            finally
            {
                _conversation.BeforeStreaming = null;
                _pendingUserInfo = null;
                _pendingVoice = null;
                _pendingDocumentStep = null;
                _pendingReader = null;
                // Not read aloud if the model wasn't ready (the reader has nothing to read).
                reader?.Finish(string.Empty);
                if (ReferenceEquals(_replyCancel, replyCancel))
                    _replyCancel = null;
                replyCancel.Dispose();
                ClearCameraTurn();
                SetBusy(false);
                UpdateMemoryLabel();
                // The agent may have saved or deleted a memory or a goal.
                _ = RefreshMemoryCountAsync();
                _ = Goals.RefreshCountAsync();
                _ = RefreshServerContextAsync();
            }
        }

        // Given to the model before a message: image references for the tools, then document excerpts; null for none.
        private static string JoinContext(string imageReferences, string document)
        {
            string context = (imageReferences ?? string.Empty) + (document ?? string.Empty);
            return context.Length > 0 ? context : null;
        }

        // The conversation's settings for the selected model: its context window and tokenizer, what the instructions and
        // tools take, and how it compacts (summaries by the model - CanSummarize - or dropping, for the small models).
        private void ConfigureConversation()
        {
            _conversation.CountTokens = text => TokenCounter(text);
            _conversation.ContextWindow = ContextWindow;
            _conversation.OverheadTokens = UseAgent ? AgentOverheadTokens + ToolGroupTokens : ChatOverheadTokens;
            _conversation.Compaction = SelectedOption.CanSummarize ? ChatCompaction.Summarize : ChatCompaction.Drop;
            _conversation.SummaryMaxTokens = SummaryMaxTokens;
            // A server's model may think anyway (LM Studio runs Gemma 4 with thinking on), which used up the summary's tokens.
            _conversation.SummaryOptions = SelectedChoice == ModelChoice.Server
                ? new ChatOptions
                {
                    MaxOutputTokens = SummaryMaxTokens + 1600,
                    Reasoning = _serverRejectsReasoning ? null : new ReasoningOptions { Effort = ReasoningEffort.None }
                }
                : null;
        }

        // The reply being written: its bubble gets the user message's data, the document step and the reader of this send.
        private UserMessageInfo _pendingUserInfo;
        private byte[] _pendingVoice;
        private string _pendingDocumentStep;
        private ReplyReader _pendingReader;
    }
}

#endif
