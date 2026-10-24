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

// The agent: each message's tools and instructions, the chat client, web search and the Tavily key.
namespace Maui.Demo.Lite
{
    public partial class LiteRtLmChatPage
    {
        // ---------- Web search (agent) ----------

        // Models that can call tools always chat through the agent, with the tools chosen per message (the date and time,
        // the location if shared, the search tools with the Web search chip on), so changing them keeps the conversation.
        private bool UseAgent => SelectedOption.CanUseTools;

        // The options of a message: for models that use tools (the agent), its tools - the core ones (date and time,
        // calculator, units, memories, the location if the user shares it, the web tools with the Web search chip on), then
        // the groups the conversation needs (LiteRtLmChatPage.Tools.cs), with a stub for each other group - and the
        // instructions; thinking for all (Think chip). A voice chat answers without thinking first: a spoken reply should
        // start quickly. (Sent to a server as reasoning_effort, unless it rejected that - then its model thinks as the
        // server is set up to.)
        private async Task<ChatOptions> MessageOptionsAsync(string prompt, List<PendingAttachment> attachments)
        {
            ChatOptions options = new ChatOptions
            {
                Reasoning = SelectedChoice == ModelChoice.Server && _serverRejectsReasoning ? null
                    : new ReasoningOptions { Effort = _thinkingSwitch.IsToggled && SelectedOption.CanThink && !_voiceMode ? ReasoningEffort.Medium : ReasoningEffort.None }
            };
            if (!UseAgent)
                return options;
            await RouteToolsAsync(prompt, attachments);
            options.Tools = ChatAgentTools.Create(_tavilyKey, _webSearchSwitch.IsToggled, _shareLocationSwitch.IsToggled,
                !IsPrivateChat && _memorySwitch.IsToggled ? _store : null, privateChat: IsPrivateChat,
                readClipboard: AgentTools.MentionsClipboard(prompt));
            foreach (AITool tool in ToolRouter.Tools())
                options.Tools.Add(tool);
            options.Instructions = JoinInstructions(IsPrivateChat ? PrivateInstructions : AgentInstructions,
                await MemoryInstructionsAsync(), WebSearchInstructions(), IsPrivateChat ? PrivateInstructions : null,
                ToolRouter.Instructions());
            options.ToolMode = ChatToolMode.Auto;
            return options;
        }

        private static string JoinInstructions(params string[] parts)
        {
            string joined = string.Join("\n\n", parts.Where(p => !string.IsNullOrEmpty(p)));
            return joined.Length > 0 ? joined : null;
        }

        // The client of the selected model - LiteRT-LM's, or the server's - with function invocation for the tools passed
        // with each message (so the settings and the Web search chip can change them during a conversation); made on first
        // use, and again for another model or backend.
        private IChatClient EnsureClient()
        {
            if (_chatClient != null)
                return _chatClient;
            IChatClient inner;
            if (SelectedChoice == ModelChoice.Server)
                inner = CreateServerChatClient();
            else
            {
                if (_model == null)
                    throw new InvalidOperationException("The model isn't loaded.");
                var localClient = new LiteRtLmChatClient(_model);
                localClient.EnableContextManagement = true;
                localClient.ContextChanged += info => UiStallWatchdog.Mark("Context: " + info);
                inner = localClient;
            }
            _chatClient = new ChatClientBuilder(inner).UseFunctionInvocation().Build();
            return _chatClient;
        }

        // Release the client (and with it the model's open conversation); the conversation's messages stay.
        private void ResetAgent()
        {
            _chatClient?.Dispose();
            _chatClient = null;
        }

        private void OnWebSearchToggled(object sender, ToggledEventArgs e)
        {
            try { Preferences.Default.Set(WebSearchPreference, e.Value); } catch (Exception) { }
            UpdateWebSearchNotice();
            // Applies from the next message; the conversation continues.
            if (e.Value && !_busy)
                SetStatus(string.IsNullOrEmpty(_tavilyKey)
                    ? "Web search requires a Tavily API key in Settings > Web search. Wikipedia and other keyless tools are available."
                    : "Web search on: Wikipedia and the web.");
            else if (!_busy)
                SetStatus(null);
        }

        private void UpdateWebSearchNotice()
        {
            _webSearchNotice.IsVisible = SelectedOption.CanUseTools && _webSearchSwitch.IsToggled
                && string.IsNullOrWhiteSpace(_tavilyKey);
        }

        private string WebSearchInstructions()
        {
            if (!_webSearchSwitch.IsToggled)
                return "Web search is turned off. If a request needs current online information, explain that the user must enable Web search; general web search also requires a Tavily API key in Settings > Web search. Do not invent current facts.";
            if (string.IsNullOrWhiteSpace(_tavilyKey))
                return "General web search is unavailable because no Tavily API key is configured. Wikipedia, reading URLs the user supplies, weather and currency conversion are available without a key. For news, recent stock movements or other requests needing general web search, explicitly tell the user: Add a Tavily API key in Settings > Web search, then retry. Do not merely say you lack internet or real-time access, and do not invent current facts.";
            return "General web search is available through search_web. Use it for news and recent events, and cite the sources returned.";
        }

        private async Task LoadTavilyKeyAsync()
        {
            try
            {
                _tavilyKey = await SecureStorage.Default.GetAsync(TavilyKeyStorageName) ?? string.Empty;
                _savedTavilyKey = _tavilyKey;
                _tavilyKeyEntry.Text = _tavilyKey;
                _tavilyKeyStatus.Text = _tavilyKey.Length > 0 ? "API key loaded from secure storage." : "No API key saved.";
            }
            catch (Exception)
            {
                _tavilyKeyStatus.Text = "Could not load the saved API key from secure storage. Enter it again and select Save API key to retry.";
            }
            _tavilyKeyEntry.IsEnabled = true;
            _saveTavilyKeyButton.IsEnabled = true;
            UpdateWebSearchNotice();
        }

        private async void OnTavilyKeyChanged()
        {
            await _tavilyKeySaveGate.WaitAsync();
            try
            {
                string key = _tavilyKeyEntry.Text?.Trim() ?? string.Empty;
                if (key == _savedTavilyKey) return;
                _tavilyKey = key;
                UpdateWebSearchNotice();
                _saveTavilyKeyButton.IsEnabled = false;
                _tavilyKeyStatus.Text = "Saving API key to secure storage...";
                if (key.Length > 0)
                    await SecureStorage.Default.SetAsync(TavilyKeyStorageName, key);
                else
                {
                    bool removed = SecureStorage.Default.Remove(TavilyKeyStorageName);
                    if (!removed && !string.IsNullOrEmpty(_savedTavilyKey))
                        throw new InvalidOperationException("Secure storage removal failed.");
                }

                // Read back before reporting success: some platform keychain failures don't throw.
                string stored = await SecureStorage.Default.GetAsync(TavilyKeyStorageName) ?? string.Empty;
                if (!string.Equals(stored, key, StringComparison.Ordinal))
                    throw new InvalidOperationException("Secure storage verification failed.");
                _savedTavilyKey = key;
                _tavilyKeyStatus.Text = key.Length > 0
                    ? "API key saved securely. It will be available after restarting the app."
                    : "API key removed from secure storage.";
                if (!_busy)
                    SetStatus(key.Length > 0
                        ? (_webSearchSwitch.IsToggled ? "Tavily API key saved. Web search is on." : "Tavily API key saved. Enable Web search to use it.")
                        : "Tavily key removed. General web search requires a Tavily API key.");
            }
            catch (Exception)
            {
                // Never include the key or the exception in UI, logs or ordinary preferences.
                _savedTavilyKey = null;
                _tavilyKeyStatus.Text = _tavilyKey.Length > 0
                    ? "API key could not be saved securely. It is available for this session only. Select Save API key to retry."
                    : "Could not confirm removal from secure storage. A previously saved key may return after restarting. Select Save API key to retry.";
#if MACCATALYST
                _tavilyKeyStatus.Text += " This Mac build needs code signing and a provisioning profile with Keychain access enabled.";
#endif
                SetStatus(_tavilyKeyStatus.Text);
            }
            finally
            {
                _saveTavilyKeyButton.IsEnabled = true;
                _tavilyKeySaveGate.Release();
            }
        }

        // Wait for the selected model before sending: loads it if needed. If it still needs downloading, the card asks
        // for the download first and the message isn't sent.
        private async Task<bool> EnsureModelReadyAsync()
        {
            if (ModelReady)
                return true;
            Task<bool> load = PrepareModelAsync(false);
            if (load == null)
            {
                SetStatus("Download the model first (above).");
                return false;
            }
            SetStatus("Waiting for the model to load...");
            bool ready = await load;
            SetStatus(null);
            return ready && ModelReady;
        }

        // The tools the plan may use, by name, for the planner.
        private string TaskToolSummary()
        {
            var tools = new List<string> { "date and time", "calculator", "unit and currency conversion", "weather", "Wikipedia" };
            if (_webSearchSwitch.IsToggled)
                tools.Add("web search and page reading");
            tools.AddRange(ToolRouter.Groups.Select(g => g.Name.Replace('_', ' ')));
            return string.Join(", ", tools);
        }

        // One question to the model in a request of its own (like a summary): its reply. The conversation's client takes it as
        // a new conversation and is given the chat again with the next message.
        private async Task<string> AskModelOnceAsync(string prompt, int maxTokens)
        {
            if (SelectedChoice == ModelChoice.Server)
                return await SummarizeOnServerAsync(prompt);
            ChatResponse response = await _clientProxy.GetResponseAsync(new[] { new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, prompt) },
                new ChatOptions { MaxOutputTokens = maxTokens, Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } });
            return new ChatReply(response.Text ?? string.Empty).Text.Trim();
        }
    }
}
