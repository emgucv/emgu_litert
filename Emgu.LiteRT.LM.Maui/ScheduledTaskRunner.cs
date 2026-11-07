//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Diagnostics;
using Emgu.LiteRT.LM;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Extensions.AI;
using Microsoft.Extensions.AI;
using LanguageModel = Emgu.LiteRT.LM.Models.LanguageModel;

namespace Emgu.LiteRT.LM.Maui;

/// <summary>
/// What a scheduled task's run needs, given once when the app starts (<see cref="ScheduledTaskRunner.Configure"/>): an
/// alarm can start the app's process with no page open, so nothing here may come from a page.
/// </summary>
public sealed class ScheduledTaskRunnerOptions
{
    /// <summary>Where the tasks are kept.</summary>
    public IAgentTaskStore Tasks { get; set; }

    /// <summary>The models, as the chat page has them: the one the chat uses (its "model" setting) runs the tasks.</summary>
    public IList<ChatModelOption> Models { get; } = new List<ChatModelOption>();

    /// <summary>The chat page's settings ("model", "use_gpu_&lt;platform&gt;", "context_tokens", "think", "memory").</summary>
    public IChatPageSettings Settings { get; set; }

    /// <summary>The saved chats: no run while a private chat is open (<see cref="IChatHistoryStore.IsPrivate"/>).</summary>
    public IChatHistoryStore History { get; set; }

    /// <summary>
    /// Saves a run as a new chat - the task (titled <see cref="ScheduledTaskRun.ChatTitle"/>), the message, the reply and
    /// the model's id - and returns the chat's id.
    /// </summary>
    public Func<ScheduledTask, ChatMessageItem, ChatMessageItem, string, Task<int>> SaveRunAsync { get; set; }

    /// <summary>
    /// The skills a run may use, made for each run: tools that need no screen and change nothing outside the app (the
    /// date, the calculator, the weather and the web, the memories and goals) - no device actions, camera or clipboard.
    /// </summary>
    public Func<ScheduledTask, IEnumerable<ChatSkill>> CreateSkills { get; set; }

    /// <summary>The agent's instructions, before the run's own (<see cref="ScheduledTaskRun.Instructions"/>).</summary>
    public string Instructions { get; set; } = ChatSkillSet.DefaultInstructions;

    /// <summary>Changes the engine's settings for a model a run loads itself (as <see cref="ChatPageOptions.ConfigureEngine"/>), or null.</summary>
    public Action<EngineSettings> ConfigureEngine { get; set; }

    /// <summary>The longest a run may take, the model's load included.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>What a scheduled task's run did.</summary>
public sealed class ScheduledTaskOutcome
{
    internal ScheduledTaskOutcome(ScheduledTask task) => Task = task;

    /// <summary>The task, or null if it was deleted.</summary>
    public ScheduledTask Task { get; }

    /// <summary>The answer, or null if it didn't run.</summary>
    public string Answer { get; internal set; }

    /// <summary>Whether to tell the user (<see cref="ScheduledTaskRun.ShouldNotify"/>).</summary>
    public bool Notify { get; internal set; }

    /// <summary>The notification's text (<see cref="ScheduledTaskRun.NotificationText"/>), or null.</summary>
    public string NotificationText { get; internal set; }

    /// <summary>The run's saved chat, or null.</summary>
    public int? ChatId { get; internal set; }

    /// <summary>Try again after this long (the chat is busy with a reply, or private), or null.</summary>
    public TimeSpan? RetryIn { get; internal set; }

    /// <summary>Why it didn't run (hot, low battery, the model isn't downloaded...), or null.</summary>
    public string Skipped { get; internal set; }

    /// <summary>The model that ran it, its backend, and how long loading it and the reply took (zero for a borrowed model).</summary>
    public string Model { get; internal set; }

    /// <inheritdoc cref="Model"/>
    public string Backend { get; internal set; }

    /// <inheritdoc cref="Model"/>
    public TimeSpan LoadTime { get; internal set; }

    /// <inheritdoc cref="Model"/>
    public TimeSpan ReplyTime { get; internal set; }

    /// <summary>The run in a line, for the log.</summary>
    public override string ToString() => Skipped != null ? "skipped: " + Skipped
        : RetryIn != null ? "retry in " + RetryIn.Value.TotalMinutes + " min"
        : Model + " (" + Backend + "), load " + LoadTime.TotalSeconds.ToString("F1") + " s, reply " + ReplyTime.TotalSeconds.ToString("F1") +
          " s, notify " + Notify + ", chat " + ChatId;
}

/// <summary>
/// Runs scheduled tasks (Emgu.LiteRT.LM.Agents' <see cref="ScheduledTask"/>) without the chat page: a new agent
/// conversation with the task and the run's skills, saved as a chat; whether to tell the user comes from the answer. A
/// chat page that has its model loaded lends it (no second engine), else the run loads the chat's model and frees it after.
/// One run at a time. On Android, <see cref="ScheduledTaskAlarms"/> starts runs on time, in a foreground service.
/// </summary>
public static class ScheduledTaskRunner
{
    private static readonly SemaphoreSlim _one = new(1, 1);
    private static readonly List<WeakReference<ChatPage>> _pages = new();

    /// <summary>What runs need, or null until <see cref="Configure"/>.</summary>
    public static ScheduledTaskRunnerOptions Options { get; private set; }

    /// <summary>Raised (on any thread) after a run, or a skipped one.</summary>
    public static event EventHandler<ScheduledTaskOutcome> Completed;

    /// <summary>
    /// Give what runs need. Call it when the app starts (e.g. in MauiProgram.CreateMauiApp): an alarm can start the app's
    /// process for a run with no page.
    /// </summary>
    /// <param name="options">The options</param>
    public static void Configure(ScheduledTaskRunnerOptions options)
    {
        if (options?.Tasks == null || options.Settings == null || options.CreateSkills == null)
            throw new ArgumentException("Tasks, Settings and CreateSkills are needed", nameof(options));
        Options = options;
    }

    // A chat page, which may lend its model to a run.
    internal static void Register(ChatPage page)
    {
        lock (_pages)
        {
            _pages.RemoveAll(p => !p.TryGetTarget(out _));
            _pages.Add(new WeakReference<ChatPage>(page));
        }
    }

    /// <summary>
    /// Run a task now: its answer saved as a chat and the task's last run recorded; or skipped (paused, deleted, hot, low
    /// battery, no model), or to be tried again later (<see cref="ScheduledTaskOutcome.RetryIn"/>).
    /// </summary>
    /// <param name="taskId">The task's id</param>
    /// <param name="cancellationToken">Stops the run</param>
    public static async Task<ScheduledTaskOutcome> RunAsync(string taskId, CancellationToken cancellationToken = default)
    {
        ScheduledTaskRunnerOptions options = Options ?? throw new InvalidOperationException("ScheduledTaskRunner.Configure wasn't called.");
        await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ScheduledTask task = await options.Tasks.GetTaskAsync(taskId).ConfigureAwait(false);
            var outcome = new ScheduledTaskOutcome(task);
            if (task == null)
                outcome.Skipped = "the task was deleted";
            else if (task.Paused)
                outcome.Skipped = "paused";
            else
                await RunTaskAsync(options, task, outcome, cancellationToken).ConfigureAwait(false);
            if (task != null && outcome.RetryIn == null && !task.Paused)
            {
                task.LastRun = DateTime.Now;
                task.LastResult = outcome.Skipped != null ? "Skipped: " + outcome.Skipped
                    : outcome.Notify ? outcome.NotificationText : "Nothing to report" + (string.IsNullOrEmpty(outcome.NotificationText) ? "" : ": " + outcome.NotificationText);
                if (outcome.ChatId != null)
                    task.LastChatId = outcome.ChatId;
                try { await options.Tasks.UpdateTaskAsync(task).ConfigureAwait(false); }
                catch (Exception) { }
            }
            Trace.WriteLine("Scheduled task " + taskId + ": " + outcome);
            try { Completed?.Invoke(null, outcome); }
            catch (Exception) { }
            return outcome;
        }
        finally
        {
            _one.Release();
        }
    }

    /// <summary>Record that a task's run was skipped (e.g. tried again too often while the chat was busy).</summary>
    /// <param name="taskId">The task's id</param>
    /// <param name="reason">Why, e.g. "the chat was busy"</param>
    public static async Task RecordSkipAsync(string taskId, string reason)
    {
        IAgentTaskStore store = Options?.Tasks;
        ScheduledTask task = store == null ? null : await store.GetTaskAsync(taskId).ConfigureAwait(false);
        if (task == null)
            return;
        task.LastRun = DateTime.Now;
        task.LastResult = "Skipped: " + reason;
        await store.UpdateTaskAsync(task).ConfigureAwait(false);
        var outcome = new ScheduledTaskOutcome(task) { Skipped = reason };
        try { Completed?.Invoke(null, outcome); }
        catch (Exception) { }
    }

    private static async Task RunTaskAsync(ScheduledTaskRunnerOptions options, ScheduledTask task, ScheduledTaskOutcome outcome, CancellationToken cancellationToken)
    {
        string pause = new WorkloadPolicy().PauseReason(await DeviceWorkload.ReadAsync().ConfigureAwait(false), false);
        if (pause != null)
        {
            outcome.Skipped = pause;
            return;
        }
        if (options.History?.IsPrivate == true)
        {
            outcome.RetryIn = TimeSpan.FromMinutes(15);
            return;
        }
        ModelLoan loan = await BorrowAsync().ConfigureAwait(false);
        if (loan == ModelLoan.Busy)
        {
            outcome.RetryIn = TimeSpan.FromMinutes(5);
            return;
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);
        LanguageModel ownModel = null;
        try
        {
            ChatModelOption option;
            IChatClient client;
            LanguageModel model;
            if (loan != null)
            {
                option = loan.Option;
                model = loan.Model;
                client = loan.Client;
                outcome.Backend = loan.Backend;
            }
            else
            {
                string saved = options.Settings.Get<string>("model", null);
                option = options.Models.FirstOrDefault(m => m.Id == saved) ?? options.Models.FirstOrDefault();
                if (option == null)
                {
                    outcome.Skipped = "no model";
                    return;
                }
                if (option.IsRemote)
                {
                    model = null;
                    client = new ChatClientBuilder(option.Remote.CreateChatClient()).UseFunctionInvocation().Build();
                    outcome.Backend = "remote";
                }
                else
                {
                    var clock = Stopwatch.StartNew();
                    (ownModel, outcome.Backend, outcome.Skipped) = await LoadAsync(options, option, timeout.Token).ConfigureAwait(false);
                    if (ownModel == null)
                        return;
                    outcome.LoadTime = clock.Elapsed;
                    model = ownModel;
                    client = new ChatClientBuilder(new LiteRtLmChatClient(model) { EnableContextManagement = true }).UseFunctionInvocation().Build();
                }
            }
            outcome.Model = option.DisplayName;
            var conversation = new ChatConversation(client, model);
            if (loan != null)
                loan.Configure(conversation);
            else
                ConfigureConversation(conversation, option, model);
            var watch = Stopwatch.StartNew();
            ChatMessageItem reply = await SendAsync(options, task, option, conversation, timeout.Token).ConfigureAwait(false);
            outcome.ReplyTime = watch.Elapsed;
            outcome.Answer = reply.Text;
            outcome.Notify = ScheduledTaskRun.ShouldNotify(task, reply.Text);
            outcome.NotificationText = ScheduledTaskRun.NotificationText(reply.Text);
            ChatMessageItem message = conversation.Messages.FirstOrDefault(m => m.Role == ChatMessageRole.User);
            if (options.SaveRunAsync != null && message != null)
            {
                try { outcome.ChatId = await options.SaveRunAsync(task, message, reply, option.Id).ConfigureAwait(false); }
                catch (Exception ex) { Trace.WriteLine("Saving the scheduled task's run failed: " + ex.Message); }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            outcome.Skipped = "it took longer than " + options.Timeout.TotalMinutes + " minutes";
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            outcome.Skipped = "it failed: " + ex.Message;
        }
        finally
        {
            loan?.Dispose();
            if (ownModel != null)
                await Task.Run(() => ownModel.Dispose()).ConfigureAwait(false);
        }
    }

    // The chat's model on its backend, without downloading it; the CPU if the GPU fails. The model, its backend, or why not.
    private static async Task<(LanguageModel, string, string)> LoadAsync(ScheduledTaskRunnerOptions options, ChatModelOption option, CancellationToken cancellationToken)
    {
        ChatModelLoad load = await ChatModelLoader.LoadAsync(option, ChatModelLoader.PreferredBackend(options.Settings, option),
            ChatModelLoader.ChatContextWindowFor(options.Settings, option), false, null, options.ConfigureEngine, null,
            cancellationToken).ConfigureAwait(false);
        return (load.Model, load.Backend, load.Error);
    }

    // As ChatPage.ConfigureConversation, for a model the run loaded.
    private static void ConfigureConversation(ChatConversation conversation, ChatModelOption option, LanguageModel model)
    {
        conversation.CountTokens = text => model != null && model.Initialized ? model.CountTokens(text) : ChatConversation.ApproximateTokens(text);
        conversation.ContextWindow = model != null && model.MaxNumTokens > 0 ? model.MaxNumTokens
            : option.ContextWindow > 0 ? option.ContextWindow : conversation.ContextWindow;
        conversation.Compaction = ChatCompaction.Drop;
        conversation.OverheadTokens = option.CanUseTools ? 900 : 50;
        conversation.DescribeToolCall = AgentTools.Describe;
    }

    // The task as a message with the run's skills, sent again (up to twice) when a tool group's stub asks for it.
    private static async Task<ChatMessageItem> SendAsync(ScheduledTaskRunnerOptions options, ScheduledTask task, ChatModelOption option,
        ChatConversation conversation, CancellationToken cancellationToken)
    {
        var skills = new ChatSkillSet(() => FunctionInvokingChatClient.CurrentContext?.Options, () =>
        {
            FunctionInvocationContext context = FunctionInvokingChatClient.CurrentContext;
            if (context != null)
                context.Terminate = true;
        });
        skills.AddRange(options.CreateSkills(task) ?? Enumerable.Empty<ChatSkill>());
        var context = new ChatMessageContext
        {
            Prompt = task.Description,
            MemoryOn = options.Settings.Get("memory", true),
            ModelCanUseTools = option.CanUseTools,
            // Nothing that needs the screen: the user isn't there.
            HandsFree = true
        };
        string instructions = string.Join("\n\n", new[] { options.Instructions, ScheduledTaskRun.Instructions(task, DateTime.Now) }
            .Where(t => !string.IsNullOrEmpty(t)));
        bool think = option.CanThink && options.Settings.Get("think", true);
        async Task<ChatOptions> OptionsAsync()
        {
            ChatSkillComposition composition = await skills.ComposeAsync(context, instructions).ConfigureAwait(false);
            return new ChatOptions
            {
                Instructions = composition.Instructions,
                Tools = composition.Tools.Count > 0 ? composition.Tools : null,
                ToolMode = composition.Tools.Count > 0 ? ChatToolMode.Auto : null,
                Reasoning = new ReasoningOptions { Effort = think ? ReasoningEffort.Medium : ReasoningEffort.None }
            };
        }
        ChatMessageItem reply = await conversation.SendAsync(ScheduledTaskRun.ChatTitle(task), null, await OptionsAsync().ConfigureAwait(false),
            null, cancellationToken).ConfigureAwait(false);
        for (int rerun = 0; rerun < 2 && !cancellationToken.IsCancellationRequested && skills.TakeRerun(); rerun++)
            reply = await conversation.RegenerateAsync(reply, await OptionsAsync().ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        return reply;
    }

    // An open chat page's model, lent for a run (null: no page has one loaded; Busy: one is replying or private).
    private static async Task<ModelLoan> BorrowAsync()
    {
        List<ChatPage> pages = new();
        lock (_pages)
        {
            foreach (WeakReference<ChatPage> reference in _pages)
                if (reference.TryGetTarget(out ChatPage page))
                    pages.Add(page);
        }
        foreach (ChatPage page in pages)
        {
            ModelLoan loan = await MainThread.InvokeOnMainThreadAsync(page.LendModelForTask).ConfigureAwait(false);
            if (loan != null)
                return loan;
        }
        return null;
    }
}

/// <summary>A chat page's model lent to a scheduled task's run: the page is busy until it is given back (disposed).</summary>
internal sealed class ModelLoan : IDisposable
{
    /// <summary>The page is busy (replying, loading, private): try again later.</summary>
    internal static readonly ModelLoan Busy = new(null, null, null, null, null, null);

    private Action _giveBack;

    internal ModelLoan(IChatClient client, ChatModelOption option, LanguageModel model, string backend, Action<ChatConversation> configure, Action giveBack)
    {
        Client = client;
        Option = option;
        Model = model;
        Backend = backend;
        Configure = configure;
        _giveBack = giveBack;
    }

    internal IChatClient Client { get; }
    internal ChatModelOption Option { get; }
    internal LanguageModel Model { get; }
    internal string Backend { get; }
    internal Action<ChatConversation> Configure { get; }

    public void Dispose()
    {
        Action giveBack = Interlocked.Exchange(ref _giveBack, null);
        giveBack?.Invoke();
    }
}
