//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Text.Json;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Models;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Controls.Shapes;

namespace Emgu.LiteRT.LM.Maui.Features;

/// <summary>
/// Task mode: with the "Plan" chip on, a message is first broken into a short plan (the model writes a numbered list,
/// Emgu.LiteRT.LM.Agents' <see cref="TaskPlan"/>), shown as a checklist above the reply the user can edit, skip steps of
/// and stop. Each step then runs on its own - a fresh request for the model, given the task, the plan and the earlier
/// steps' short results (<see cref="TaskPlan.StepPrompt"/>), so its tool output doesn't fill the small window - with its
/// tool calls as step lines and its result in the card, and last the final answer is written from the results into the
/// reply. Tools that change something outside the app still ask with their own Confirm / Cancel cards. The saved chat
/// holds the request, the step lines, the final answer and the plan (a saved file of kind <see cref="PlanKind"/>: the
/// card comes back, closed, when the chat is reopened); the model is given the conversation again with the next message. The chip is this feature's, shown for a model that can use tools and not hands-free; the page runs
/// the message through the client of <see cref="CreateMessageClient"/> when the chip is on.
/// </summary>
/// <para>
/// Research mode is the same machinery with the "🔎 Research" chip (shown with a Tavily key; it turns web search on): the
/// question becomes 3 to 6 sub-questions (<see cref="TaskPlan.ResearchPlanPrompt"/>), each one is searched and a page read -
/// at most <see cref="TaskPlan.MaxResearchToolCalls"/> tool calls - and its notes kept with the pages' addresses as numbered
/// sources; the answer is written from the notes alone, citing [n], and the sources are listed after it.
/// </para>
public sealed class TaskFeature : ChatFeature
{
    /// <summary>The kind of a reply's saved file that is its plan (JSON: the steps with their state and result, and how it ended).</summary>
    public const string PlanKind = "task_plan";

    private bool _planNext;
    private bool _researchNext;

    /// <inheritdoc/>
    public override string Name => "plan";

    /// <inheritdoc/>
    public override string Title => "Plan";

    /// <summary>The chip is per message (nothing to switch in the settings).</summary>
    public override bool PersistEnabled => false;

    /// <inheritdoc/>
    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    /// <inheritdoc/>
    public override ChatChip BuildComposerChip() =>
        new ChatChip("\U0001F4CB Plan", () => _planNext, on =>
        {
            _planNext = on;
            if (on)
                _researchNext = false;
        })
        {
            IsVisible = () => Host?.Model?.CanUseTools == true && Host.HandsFree != true
        };

    /// <summary>The Plan chip and, with a Tavily key, the Research chip (one at a time).</summary>
    public override IEnumerable<ChatChip> BuildComposerChips()
    {
        yield return BuildComposerChip();
        yield return new ChatChip("\U0001F50E Research", () => _researchNext, on =>
        {
            _researchNext = on;
            if (!on)
                return;
            // Research searches the web: web search on, and not the plain plan at the same time.
            _planNext = false;
            Host?.Services.Get<WebFeature>()?.TurnOn();
            Host?.SetStatus("Research: the next message is researched on the web in a few steps, with sources. It can take several minutes.");
        })
        {
            IsVisible = () => Host?.Model?.CanUseTools == true && Host.HandsFree != true && Host.Services.Get<WebFeature>()?.HasSearchKey == true
        };
    }

    /// <summary>Whether the message being sent is to be researched (the chip was on); the chip is off again afterwards.</summary>
    public bool TakeResearch()
    {
        if (!_researchNext)
            return false;
        _researchNext = false;
        Host?.RefreshChips();
        return true;
    }

    /// <summary>Whether the message being sent is to be planned (the chip was on); the chip is off again afterwards.</summary>
    public bool TakePlan()
    {
        if (!_planNext)
            return false;
        _planNext = false;
        Host?.RefreshChips();
        return true;
    }

    // The tools the plan may use, by name, for the planner: the core tools and the skills' (their groups' names).
    private string PlannerToolSummary
    {
        get
        {
            var tools = new List<string> { "date and time", "calculator", "unit and currency conversion", "weather", "Wikipedia" };
            string skills = Host.Skills.ToolSummary;
            if (!string.IsNullOrEmpty(skills))
                tools.Add(skills);
            return string.Join(", ", tools);
        }
    }

    /// <summary>The task-mode client for a message with the Plan chip on (a model with tools, not hands-free); null otherwise.</summary>
    /// <param name="request">The message, as prepared</param>
    public override IChatClient CreateMessageClient(ChatSendRequest request)
    {
        bool research = TakeResearch();
        if (!(TakePlan() || research) || Host?.Model?.CanUseTools != true || Host.HandsFree)
            return null;
        return CreateClient(request.Text ?? string.Empty, request.Attachments.ToList(), research);
    }

    /// <summary>The chat client of a message to plan and run step by step.</summary>
    /// <param name="request">The message's text</param>
    /// <param name="attachments">Its attachments (given to the planner, every step and the answer; in research to the first part)</param>
    /// <param name="research">True for research mode (sub-questions searched on the web, with sources)</param>
    public IChatClient CreateClient(string request, IList<DataContent> attachments, bool research = false) =>
        new TaskModeClient(this, request, attachments, research);

    // Completed by the card's buttons: true to run the plan, false to just answer. (A test completes it.)
    private TaskCompletionSource<bool> _taskDecision;
    // The plan being run (for the card's sake and for tests).
    private TaskPlan _taskPlan;
    private TaskCard _taskCard;

    private static string TitleFont => ChatResources.Find<string>("ChatTitleFont");
    private static string BodyFont => ChatResources.Find<string>("ChatBodyFont");
    private static Color Accent => ChatResources.FindColor("ChatAccent");
    private static Color PrimaryText => ChatResources.ThemeColor("ChatPrimaryText");
    private static Color SecondaryText => ChatResources.ThemeColor("ChatSecondaryText");

    // A task, as the chat client of the message that asks for it (ChatConversation.SendAsync with this client): it plans
    // the task, shows the plan above the reply and waits for Run (or "Just answer"); then each step runs as a request of its
    // own, given the task, the plan and the earlier steps' short results (TaskPlan.StepPrompt), its tool calls streamed as
    // the reply's step lines; last the final answer is written from the results and streamed as the reply. Not a task to
    // break up, or answered without the plan: the message goes to the model as usual.
    private sealed class TaskModeClient : IChatClient
    {
        private readonly TaskFeature _feature;
        private readonly string _request;
        private readonly IList<DataContent> _attachments;
        private readonly bool _research;

        public TaskModeClient(TaskFeature feature, string request, IList<DataContent> attachments, bool research)
        {
            _feature = feature;
            _request = request;
            _attachments = attachments ?? new List<DataContent>();
            _research = research;
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
            IChatClient client = Host.ChatClient;
            if (_research && options?.Tools?.Any(t => t.Name == "search_web") != true)
            {
                // No web search tool (no key, or web search off): say so rather than research from memory.
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                    "Research needs web search: turn on \U0001F50E Web search and add a Tavily API key in the settings, then ask again.");
                yield break;
            }
            Host.SetStatus(_research ? "Planning the research..." : "Planning...");
            List<string> steps = _research
                ? TaskPlan.Parse(await Host.AskModelOnceAsync(TaskPlan.ResearchPlanPrompt(_request, ToolNames(options)), 400), TaskPlan.MaxResearchSteps)
                : TaskPlan.Parse(await PlanWithAttachmentsAsync(client, token));
            bool run = false;
            TaskPlan plan = null;
            TaskCard card = null;
            if (steps.Count >= 2 || (_research && steps.Count >= 1))
            {
                // A task's steps (and its final answer) all see the attachments: a step's result is short, and a later
                // step that only had the earlier ones' results lost what the picture showed. Research gives them to the
                // first part only (its parts are web searches).
                plan = new TaskPlan(steps, _research) { ToolNames = ToolNames(options), WithAttachments = !_research && _attachments.Count > 0 };
                _feature._taskPlan = plan;
                var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _feature._taskDecision = decision;
                card = await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    TaskCard created = _feature.CreateTaskCard(plan, decision);
                    Host.AddCardToReply(created.View);
                    return created;
                });
                _feature._taskCard = card;
                Host.SetStatus(_research ? "Review the research plan, then start it." : "Review the plan, then run it.");
                using (token.Register(() => decision.TrySetCanceled(token)))
                    run = await decision.Task;
                _feature._taskDecision = null;
                if (!run)
                    await MainThread.InvokeOnMainThreadAsync(() => card.Close("Answering without a plan"));
            }
            if (!run)
            {
                // Not a task to break up: answered as usual.
                Host.SetStatus("Generating...");
                await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(messages, options, token))
                    yield return update;
                yield break;
            }
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            card.Stop = stop.Cancel;
            await MainThread.InvokeOnMainThreadAsync(card.Begin);
            // Research takes minutes a part on a phone: it goes on in the background (on Android a foreground service with a
            // notification; without one Android freezes the app soon after it leaves the screen, and may close it - a Pixel
            // Fold did, the research lost).
            BackgroundWork background = _research ? await BackgroundWork.BeginAsync("Researching", _request, stop.Cancel) : null;
            bool finished = false;
            try
            {
            int toRun = plan.Steps.Count(st => st.State != TaskPlan.State.Skipped), runIndex = 0;
            for (int i = 0; i < plan.Steps.Count && !stop.IsCancellationRequested; i++)
            {
                TaskPlan.Step step = plan.Steps[i];
                if (step.State == TaskPlan.State.Skipped)
                    continue;
                step.State = TaskPlan.State.Running;
                await MainThread.InvokeOnMainThreadAsync(card.Refresh);
                runIndex++;
                // Of the parts that run (skipped ones aren't counted).
                Host.SetStatus(string.Format(_research ? "Researching {0} of {1}: {2}" : "Step {0} of {1}: {2}", runIndex, Math.Max(toRun, runIndex), step.Text));
                background?.Update(string.Format("{0} of {1}: {2}", runIndex, Math.Max(toRun, runIndex), step.Text));
                // A request of its own: the task, the plan and the results so far are in the message.
                var contents = new List<AIContent>();
                if (i == 0 || plan.WithAttachments)
                    contents.AddRange(_attachments);
                contents.Add(new TextContent(plan.StepPrompt(_request, i)));
                var stepMessages = new[] { new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, contents) };
                var answer = new System.Text.StringBuilder();
                // Research: the step's searches and page reads, for its sources and its cap.
                using var stepStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var callNames = new Dictionary<string, string>();
                var searchPages = new List<(string Title, string Url)>();
                var readPages = new List<string>();
                var toolSources = new List<(string Title, string Url)>();
                int toolCalls = 0;
                bool capped = false;
                IAsyncEnumerator<ChatResponseUpdate> stream = client.GetStreamingResponseAsync(stepMessages, options, stepStop.Token).GetAsyncEnumerator(stepStop.Token);
                try
                {
                    while (true)
                    {
                        ChatResponseUpdate update;
                        try
                        {
                            if (!await stream.MoveNextAsync())
                                break;
                            update = stream.Current;
                        }
                        catch (OperationCanceledException) when (stop.IsCancellationRequested)
                        {
                            step.State = TaskPlan.State.Skipped;
                            break;
                        }
                        catch (OperationCanceledException) when (stepStop.IsCancellationRequested)
                        {
                            // Research: too many searches for one sub-question; its notes are what it found.
                            capped = true;
                            break;
                        }
                        catch (Exception ex) when (!stop.IsCancellationRequested)
                        {
                            step.State = TaskPlan.State.Failed;
                            step.Result = TaskPlan.ShortResult(ex.Message);
                            System.Diagnostics.Debug.WriteLine("Task mode: step failed: " + ex.Message);
                            break;
                        }
                        answer.Append(update.Contents.OfType<TextContent>().Select(t => t.Text).DefaultIfEmpty(string.Empty).Aggregate(string.Concat));
                        // The step's tool calls and results are the reply's step lines; its text is only its result.
                        var calls = update.Contents.Where(c => c is FunctionCallContent || c is FunctionResultContent).ToList();
                        if (calls.Count > 0)
                            yield return new ChatResponseUpdate(update.Role, calls);
                        if (_research)
                        {
                            foreach (FunctionCallContent call in update.Contents.OfType<FunctionCallContent>())
                            {
                                callNames[call.CallId ?? string.Empty] = call.Name;
                                if (call.Name == "read_web_page" && call.Arguments != null && call.Arguments.TryGetValue("url", out object url) && url != null)
                                    readPages.Add(url.ToString());
                                if (TaskPlan.ToolSource(call.Name) is (string Title, string Url) source && !toolSources.Contains(source))
                                    toolSources.Add(source);
                                if (++toolCalls > TaskPlan.MaxResearchToolCalls)
                                    stepStop.Cancel();
                            }
                            foreach (FunctionResultContent result in update.Contents.OfType<FunctionResultContent>())
                                if (callNames.TryGetValue(result.CallId ?? string.Empty, out string name) && name == "search_web")
                                    searchPages.AddRange(TaskPlan.SearchResultPages(result.Result?.ToString()));
                        }
                    }
                }
                finally
                {
                    await stream.DisposeAsync();
                }
                if (step.State == TaskPlan.State.Running)
                {
                    string notes = new ChatReply(answer.ToString()).Text;
                    if (capped && string.IsNullOrWhiteSpace(notes))
                        notes = "(stopped after " + TaskPlan.MaxResearchToolCalls + " searches and page reads without notes)";
                    step.Result = plan.ShortResultFor(notes);
                    step.State = TaskPlan.State.Done;
                }
                if (_research)
                {
                    // The pages it read (titles from the search results when they were among them), else the search's top two.
                    var pages = readPages.Count > 0
                        ? readPages.Select(u => (searchPages.FirstOrDefault(p => string.Equals(p.Url, u, StringComparison.OrdinalIgnoreCase)).Title, u))
                        : searchPages.Take(2);
                    // A tool's own source (Twelve Data...) first: it answered without a page.
                    plan.AddSources(step, toolSources.Concat(pages).ToList());
                }
                await MainThread.InvokeOnMainThreadAsync(card.Refresh);
            }
            token.ThrowIfCancellationRequested();
            if (stop.IsCancellationRequested)
            {
                foreach (TaskPlan.Step pending in plan.Steps.Where(st => st.State == TaskPlan.State.Pending))
                    pending.State = TaskPlan.State.Skipped;
                await MainThread.InvokeOnMainThreadAsync(() => card.Close("Stopped"));
                yield return new ChatResponseUpdate(ChatRole.Assistant, plan.StoppedSummary());
                yield break;
            }
            await MainThread.InvokeOnMainThreadAsync(() => card.Close("Done"));
            // The final answer, from the results, in the reply - without tools for research (it writes from the notes).
            Host.SetStatus(_research ? "Writing the report..." : "Writing the answer...");
            background?.Update("Writing the report...");
            var finalContents = new List<AIContent>();
            if (plan.WithAttachments)
                finalContents.AddRange(_attachments);
            finalContents.Add(new TextContent(plan.FinalPrompt(_request)));
            var final = new[] { new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, finalContents) };
            ChatOptions finalOptions = _research && options != null ? CloneWithoutTools(options) : options;
            if (!_research)
            {
                await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(final, finalOptions, token))
                    yield return update;
            }
            else
            {
                // The report line by line (a line is held until it ends), up to a sources section the model wrote itself.
                var pending = new System.Text.StringBuilder();
                bool dropping = false;
                ChatFinishReason? finish = null;
                await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(final, finalOptions, token))
                {
                    finish = update.FinishReason ?? finish;
                    string text = string.Concat(update.Contents.OfType<TextContent>().Select(t => t.Text));
                    var other = update.Contents.Where(c => !(c is TextContent)).ToList();
                    if (other.Count > 0)
                        yield return new ChatResponseUpdate(update.Role, other);
                    if (dropping || text.Length == 0)
                        continue;
                    pending.Append(text);
                    string buffered = pending.ToString();
                    int end = buffered.LastIndexOf('\n');
                    if (end < 0)
                        continue;
                    var shown = new System.Text.StringBuilder();
                    foreach (string line in buffered.Substring(0, end + 1).Split('\n').SkipLast(1))
                    {
                        if (TaskPlan.IsSourcesHeading(line))
                        {
                            dropping = true;
                            break;
                        }
                        if (TaskPlan.IsCitationsLine(line))
                            continue;
                        shown.Append(line).Append('\n');
                    }
                    pending.Clear();
                    if (!dropping)
                        pending.Append(buffered.Substring(end + 1));
                    if (shown.Length > 0)
                        yield return new ChatResponseUpdate(ChatRole.Assistant, shown.ToString());
                }
                if (!dropping && pending.Length > 0 && !TaskPlan.IsSourcesHeading(pending.ToString()) && !TaskPlan.IsCitationsLine(pending.ToString()))
                    yield return new ChatResponseUpdate(ChatRole.Assistant, pending.ToString());
                // A report the length limit ended says so (the conversation adds the notice).
                if (finish == ChatFinishReason.Length)
                    yield return new ChatResponseUpdate { Role = ChatRole.Assistant, FinishReason = finish };
            }
            // The sources, as the pages' real addresses (not the model's).
            if (_research && plan.Sources.Count > 0)
                yield return new ChatResponseUpdate(ChatRole.Assistant, plan.SourcesMarkdown());
            finished = true;
            }
            finally
            {
                if (finished)
                    background?.Finish("The research is done: tap to read the report.");
                else
                    background?.Dispose();
            }
        }

        // The plan, asked once outside the conversation; with attachments the planner sees them too (a page of numbered
        // questions is planned as a step per question).
        private async Task<string> PlanWithAttachmentsAsync(IChatClient client, CancellationToken token)
        {
            if (_attachments.Count == 0)
                return await Host.AskModelOnceAsync(TaskPlan.PlanPrompt(_request, _feature.PlannerToolSummary), 400, token);
            var contents = new List<AIContent>(_attachments) { new TextContent(TaskPlan.PlanPrompt(_request, _feature.PlannerToolSummary, true)) };
            ChatResponse response = await client.GetResponseAsync(new[] { new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, contents) },
                new ChatOptions { MaxOutputTokens = 400, Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } }, token);
            return new ChatReply(response.Text ?? string.Empty).Text.Trim();
        }

        private static List<string> ToolNames(ChatOptions options) =>
            options?.Tools?.Select(t => t.Name).ToList() ?? new List<string>();

        private static ChatOptions CloneWithoutTools(ChatOptions options)
        {
            ChatOptions clone = options.Clone();
            clone.Tools = null;
            clone.ToolMode = null;
            return clone;
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class TaskCard
    {
        public Border View;
        public Action Refresh;
        // The plan is running: its steps' buttons give way to Stop.
        public Action Begin;
        public Action<string> Close;
        // Stops the steps (set when they start).
        public Action Stop;
        // After each refresh (the research estimate follows the skipped parts).
        public Action Refreshed;
    }

    /// <inheritdoc/>
    public override View RestoreSavedFile(ChatSavedFile file)
    {
        if (file.Kind != PlanKind)
            return null;
        (TaskPlan plan, string outcome) = LoadPlan(file.Data);
        if (plan == null)
            return null;
        TaskCard card = CreateTaskCard(plan, new TaskCompletionSource<bool>());
        card.Close(outcome);
        return card.View;
    }

    private sealed class SavedStep
    {
        public string Text { get; set; }
        public string State { get; set; }
        public string Result { get; set; }
    }

    private sealed class SavedPlan
    {
        public string Outcome { get; set; }
        public bool Research { get; set; }
        public List<SavedStep> Steps { get; set; }
    }

    private static byte[] SavePlan(TaskPlan plan, string outcome) => JsonSerializer.SerializeToUtf8Bytes(new SavedPlan
    {
        Outcome = outcome,
        Research = plan.Research,
        Steps = plan.Steps.Select(s => new SavedStep { Text = s.Text, State = s.State.ToString(), Result = s.Result }).ToList()
    });

    // A saved plan; null for one that can't be read (the card is left out).
    private static (TaskPlan Plan, string Outcome) LoadPlan(byte[] data)
    {
        try
        {
            SavedPlan saved = JsonSerializer.Deserialize<SavedPlan>(data);
            if (saved?.Steps == null || saved.Steps.Count == 0)
                return (null, null);
            var plan = new TaskPlan(saved.Steps.Select(s => s.Text ?? string.Empty), saved.Research);
            for (int i = 0; i < saved.Steps.Count; i++)
            {
                plan.Steps[i].Result = saved.Steps[i].Result;
                plan.Steps[i].State = Enum.TryParse(saved.Steps[i].State, out TaskPlan.State state) ? state : TaskPlan.State.Pending;
            }
            return (plan, saved.Outcome);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string TaskStateIcon(TaskPlan.State state) => state switch
    {
        TaskPlan.State.Running => "▶",
        TaskPlan.State.Done => "✓",
        TaskPlan.State.Skipped => "−",
        TaskPlan.State.Failed => "✗",
        _ => "○"
    };

    // The plan above the reply: its steps with their state and result, and the buttons - Run, Edit and "Just answer" until it
    // runs, then Stop.
    private TaskCard CreateTaskCard(TaskPlan plan, TaskCompletionSource<bool> decision)
    {
        var card = new TaskCard();
        bool running = false, finished = false;
        Color accent = Accent, primaryText = PrimaryText, secondaryText = SecondaryText, success = ChatResources.FindColor("ChatSuccess");
        string titleFont = TitleFont, bodyFont = BodyFont;
        var rows = new VerticalStackLayout { Spacing = 6 };
        var editor = new Editor { IsVisible = false, AutoSize = EditorAutoSizeOption.TextChanges, FontFamily = bodyFont, FontSize = 14, TextColor = primaryText, MinimumHeightRequest = 100 };
        var status = new Label { FontFamily = titleFont, FontSize = 12, TextColor = secondaryText, IsVisible = false };
        Button MakeButton(string text, bool primary) => new Button
        {
            Text = text,
            FontFamily = titleFont,
            FontSize = 13,
            BackgroundColor = primary ? accent : Colors.Transparent,
            TextColor = primary ? Colors.White : accent,
            BorderColor = accent,
            BorderWidth = primary ? 0 : 1,
            CornerRadius = 12,
            Padding = new Thickness(14, 4),
            MinimumHeightRequest = 36
        };
        Button runButton = MakeButton(plan.Research ? "Start research" : "Run plan", true), editButton = MakeButton("Edit", false),
            plainButton = MakeButton("Just answer", false), stopButton = MakeButton("Stop", false);
        stopButton.IsVisible = false;
        var buttons = new HorizontalStackLayout { Spacing = 8, Children = { runButton, editButton, plainButton, stopButton } };

        void Refresh()
        {
            rows.Children.Clear();
            for (int i = 0; i < plan.Steps.Count; i++)
            {
                TaskPlan.Step step = plan.Steps[i];
                var grid = new Grid
                {
                    ColumnSpacing = 8,
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
                };
                grid.Add(new Label
                {
                    Text = TaskStateIcon(step.State),
                    FontSize = 15,
                    TextColor = step.State == TaskPlan.State.Done ? success : step.State == TaskPlan.State.Running ? accent : secondaryText,
                    VerticalOptions = LayoutOptions.Start,
                    WidthRequest = 18
                }, 0, 0);
                var text = new VerticalStackLayout { Spacing = 2 };
                text.Children.Add(new Label
                {
                    Text = step.Text,
                    FontFamily = bodyFont,
                    FontSize = 14,
                    TextColor = step.State == TaskPlan.State.Skipped ? secondaryText : primaryText,
                    TextDecorations = step.State == TaskPlan.State.Skipped ? TextDecorations.Strikethrough : TextDecorations.None
                });
                if (!string.IsNullOrWhiteSpace(step.Result))
                    text.Children.Add(new Label
                    {
                        // The model's Markdown and LaTeX ("$162\pi \approx 508.94\text{ cm}^3$") as plain text (162π ≈ 508.94 cm³).
                        Text = ReplyMarkdown.PlainText(step.Result),
                        FontFamily = bodyFont,
                        FontSize = 12,
                        FontAttributes = FontAttributes.Italic,
                        TextColor = step.State == TaskPlan.State.Failed ? Colors.IndianRed : secondaryText,
                        MaxLines = 3,
                        LineBreakMode = LineBreakMode.TailTruncation
                    });
                grid.Add(text, 1, 0);
                // Steps that haven't run can be skipped (and the skip undone) before and while the plan runs.
                if (!finished && (step.State == TaskPlan.State.Pending || (step.State == TaskPlan.State.Skipped && !running)) && !editor.IsVisible)
                {
                    TaskPlan.Step captured = step;
                    Label skip = ChatPage.Link(step.State == TaskPlan.State.Skipped ? "Undo" : "Skip", () =>
                    {
                        captured.State = captured.State == TaskPlan.State.Skipped ? TaskPlan.State.Pending : TaskPlan.State.Skipped;
                        Refresh();
                    });
                    skip.FontSize = 12;
                    skip.VerticalOptions = LayoutOptions.Start;
                    grid.Add(skip, 2, 0);
                }
                rows.Children.Add(grid);
            }
            runButton.IsVisible = editButton.IsVisible = plainButton.IsVisible = !running && !finished && !editor.IsVisible;
            stopButton.IsVisible = running && !finished;
            card.Refreshed?.Invoke();
        }

        runButton.Clicked += (_, _) =>
        {
            if (plan.Steps.All(s => s.State == TaskPlan.State.Skipped))
                return;
            running = true;
            Refresh();
            decision.TrySetResult(true);
        };
        plainButton.Clicked += (_, _) => decision.TrySetResult(false);
        stopButton.Clicked += (_, _) =>
        {
            stopButton.IsEnabled = false;
            card.Stop?.Invoke();
        };
        editButton.Clicked += (_, _) =>
        {
            if (!editor.IsVisible)
            {
                editor.Text = TaskPlan.ToLines(plan.Steps.Select(s => s.Text));
                editor.IsVisible = true;
                editButton.Text = "Done editing";
                rows.IsVisible = false;
                runButton.IsVisible = plainButton.IsVisible = false;
                return;
            }
            List<string> edited = TaskPlan.FromLines(editor.Text).Take(plan.Research ? TaskPlan.MaxResearchSteps : TaskPlan.MaxSteps).ToList();
            if (edited.Count > 0)
            {
                plan.Steps.Clear();
                foreach (string text in edited)
                    plan.Steps.Add(new TaskPlan.Step { Text = text });
            }
            editor.IsVisible = false;
            editButton.Text = "Edit";
            rows.IsVisible = true;
            Refresh();
        };

        var content = new VerticalStackLayout { Spacing = 8 };
        content.Children.Add(new Label { Text = plan.Research ? "\U0001F50E  Research" : "\U0001F4CB  Plan", FontFamily = titleFont, FontSize = 15, TextColor = primaryText });
        content.Children.Add(rows);
        if (plan.Research)
        {
            // What it may cost, before it starts (searches at "advanced" depth, page reads).
            var estimate = new Label { FontFamily = bodyFont, FontSize = 11, TextColor = secondaryText };
            void Estimate() => estimate.Text = string.Format("Up to about {0} Tavily credits. Each part is a search and a page read; on a phone this can take several minutes a part.",
                plan.Steps.Count(st => st.State != TaskPlan.State.Skipped) * TaskPlan.ResearchCreditsPerStep);
            Estimate();
            content.Children.Add(estimate);
            card.Refreshed += Estimate;
        }
        content.Children.Add(new Label { Text = "One step per line, in the order they run.", FontFamily = bodyFont, FontSize = 11, TextColor = secondaryText, IsVisible = false });
        content.Children.Add(editor);
        content.Children.Add(status);
        content.Children.Add(buttons);
        card.View = new Border
        {
            Stroke = ChatResources.ThemeColor("ChatRowBorder"),
            StrokeThickness = 1,
            BackgroundColor = ChatResources.ThemeColor("ChatSurface"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Padding = new Thickness(12, 10),
            Margin = new Thickness(6, 0),
            HorizontalOptions = LayoutOptions.Start,
            MaximumWidthRequest = 420,
            Content = content
        };
        SemanticProperties.SetDescription(card.View, plan.Research ? "The research plan" : "The plan for this task");
        card.Refresh = Refresh;
        card.Begin = () =>
        {
            running = true;
            editor.IsVisible = false;
            rows.IsVisible = true;
            Refresh();
        };
        card.Close = outcome =>
        {
            // Kept with the reply, so the card comes back when the chat is reopened.
            if (!finished && Host != null)
                Host.AddCardToReply(null, new ChatSavedFile(PlanKind, "application/json", SavePlan(plan, outcome)));
            finished = true;
            running = false;
            status.Text = outcome;
            status.IsVisible = true;
            buttons.IsVisible = false;
            editor.IsVisible = false;
            rows.IsVisible = true;
            Refresh();
        };
        Refresh();
        return card;
    }
}
