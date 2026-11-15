//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Models;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Maui.Features;

/// <summary>
/// The "Check homework" chip: with a photo of a homework page attached, the model doesn't just solve the questions, it
/// checks the student's answers, in three separate requests so each is small enough for a small model: it reads each
/// question and exactly what the student wrote (<see cref="HomeworkCheck.ReadPrompt"/>; the reading is shown, so a misread digit is
/// seen), solves each question on its own without looking at the student's answer (<see cref="HomeworkCheck.SolvePrompt"/>), and then
/// compares the two (<see cref="HomeworkCheck.ComparePrompt"/>): correct, not the same, or can't tell. The chip is this feature's, shown
/// for a model that takes images; it is per message.
/// </summary>
public sealed class HomeworkFeature : ChatFeature
{
    private bool _checkNext;

    /// <inheritdoc/>
    public override string Name => "homework";

    /// <inheritdoc/>
    public override string Title => "Check homework";

    /// <summary>The chip is per message (nothing to switch in the settings).</summary>
    public override bool PersistEnabled => false;

    /// <inheritdoc/>
    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    /// <inheritdoc/>
    public override ChatChip BuildComposerChip() =>
        new ChatChip("\U0001F4DD Check homework", () => _checkNext, on =>
        {
            _checkNext = on;
            if (on)
                Host?.SetStatus("Check homework: attach a photo of the page and send. The model reads the answers written on it, solves the questions itself and compares.");
        })
        {
            IsVisible = () => Host?.Model?.AcceptsImages == true && Host.HandsFree != true
        };

    /// <summary>Whether the message being sent is to be checked as homework (the chip was on); the chip is off again afterwards.</summary>
    public bool TakeCheck()
    {
        if (!_checkNext)
            return false;
        _checkNext = false;
        Host?.RefreshChips();
        return true;
    }

    /// <inheritdoc/>
    public override IChatClient CreateMessageClient(ChatSendRequest request)
    {
        if (!TakeCheck())
            return null;
        return new HomeworkClient(this, request.Text, request.Attachments.Where(a => a.MediaType != null && a.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)).ToList());
    }

    // The three requests as one reply: what was read, the model's own answers and the check.
    private sealed class HomeworkClient : IChatClient
    {
        private readonly HomeworkFeature _feature;
        private readonly string _note;
        private readonly IList<DataContent> _images;

        public HomeworkClient(HomeworkFeature feature, string note, IList<DataContent> images)
        {
            _feature = feature;
            _note = note;
            _images = images;
        }

        private IChatPageHost Host => _feature.Host;

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions options = null,
            CancellationToken cancellationToken = default)
        {
            var updates = new List<ChatResponseUpdate>();
            await foreach (ChatResponseUpdate update in GetStreamingResponseAsync(messages, options, cancellationToken))
                updates.Add(update);
            return updates.ToChatResponse();
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token = default)
        {
            if (_images.Count == 0)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Attach a photo of the homework page (+ > Photo or Camera), then send again with \U0001F4DD Check homework on.");
                yield break;
            }
            IChatClient client = Host.ChatClient;
            // Each page on its own: two pages in one request had their question numbers run together and the answers of one
            // page matched to the other's questions.
            var checks = new List<string>();
            for (int page = 0; page < _images.Count; page++)
            {
                var image = new[] { _images[page] };
                string label = _images.Count > 1 ? "Page " + (page + 1) + " of " + _images.Count + ": " : string.Empty;
                string header = _images.Count > 1 ? "## Page " + (page + 1) + " of " + _images.Count + "\n\n" : string.Empty;
                Host.SetStatus(label + "reading the page...");
                string read = await AskAsync(client, image, HomeworkCheck.ReadPrompt(_note), 900, ReasoningEffort.None, token);
                yield return new ChatResponseUpdate(ChatRole.Assistant, header + "**What I read** (check it against the page)\n\n" + read + "\n\n");
                Host.SetStatus(label + "solving the questions...");
                string solved = await AskAsync(client, image, HomeworkCheck.SolvePrompt(HomeworkCheck.QuestionsOnly(read)), 1500, null, token);
                yield return new ChatResponseUpdate(ChatRole.Assistant, "**My own answers**\n\n" + solved + "\n\n");
                Host.SetStatus(label + "comparing...");
                string check = HomeworkCheck.TidyCheck(await AskAsync(client, null, HomeworkCheck.ComparePrompt(read, solved), 900, ReasoningEffort.None, token));
                checks.Add(check);
                yield return new ChatResponseUpdate(ChatRole.Assistant, "**The check**\n\n" + check + "\n\n");
            }
            string summary = HomeworkCheck.Summary(checks);
            if (summary.Length > 0)
                yield return new ChatResponseUpdate(ChatRole.Assistant, summary);
            Host.SetStatus(string.Empty);
        }

        // One request of its own (no tools, no earlier chat), with the photo when given; the thinking is dropped from the text.
        private static async Task<string> AskAsync(IChatClient client, IEnumerable<DataContent> images, string prompt, int maxTokens, ReasoningEffort? effort, CancellationToken token)
        {
            var contents = new List<AIContent>();
            if (images != null)
                contents.AddRange(images);
            contents.Add(new TextContent(prompt));
            var options = new ChatOptions { MaxOutputTokens = maxTokens };
            if (effort.HasValue)
                options.Reasoning = new ReasoningOptions { Effort = effort.Value };
            ChatResponse response = await client.GetResponseAsync(new[] { new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, contents) }, options, token);
            return new ChatReply(response.Text ?? string.Empty).Text.Trim();
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
