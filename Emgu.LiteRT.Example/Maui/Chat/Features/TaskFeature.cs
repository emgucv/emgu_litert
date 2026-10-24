//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Models;
using Microsoft.Maui.Controls.Shapes;
using Emgu.LiteRT.LM.Agents;
using Microsoft.Extensions.AI;

namespace Maui.Demo.Lite;

// Task mode (TODO item 23): with the "Plan" chip on, a message is first broken into a short plan (the model writes a numbered
// list), shown as a checklist the user can edit, skip steps of and stop. Each step then runs on its own - a fresh conversation
// for the model, given the task, the plan and the earlier steps' short results (TaskPlan.StepPrompt), so its tool output doesn't
// fill the small window - with its tool calls as step lines and its result in the card,
// and last the final answer is written from the results into the reply. Tools that change something outside the app still ask
// with their own Confirm / Cancel cards. The saved chat holds the request and the final answer (the plan card isn't saved);
// the model is given the conversation again with the next message.
internal sealed class TaskFeature
{
    private readonly IChatHost _host;

    public TaskFeature(IChatHost host) { _host = host; }

    // The chat client of a message the Plan chip is on for.
    public IChatClient CreateClient(string request, IList<DataContent> attachments) => new TaskModeClient(this, request, attachments);

    // Completed by the card's buttons: true to run the plan, false to just answer. (A test completes it.)
    private TaskCompletionSource<bool> _taskDecision;
    // The plan being run (for the card's sake and for tests).
    private TaskPlan _taskPlan;
    private TaskCard _taskCard;

    // A task, as the chat client of the message that asks for it (ChatConversation.SendAsync with this client): it plans
    // the task, shows the plan above the reply and waits for Run (or "Just answer"); then each step runs as a request of its
    // own, given the task, the plan and the earlier steps' short results (TaskPlan.StepPrompt), its tool calls streamed as
    // the reply's step lines; last the final answer is written from the results and streamed as the reply. Not a task to
    // break up, or answered without the plan: the message goes to the model as usual.
    private sealed class TaskModeClient : IChatClient
    {
        private readonly TaskFeature _feature;
        private readonly IChatHost _host;
        private readonly string _request;
        private readonly IList<DataContent> _attachments;

        public TaskModeClient(TaskFeature feature, string request, IList<DataContent> attachments)
        {
            _feature = feature;
            _host = feature._host;
            _request = request;
            _attachments = attachments;
        }

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
            IChatClient client = _host.ChatClient;
            _host.SetStatus("Planning...");
            UiStallWatchdog.Mark("Task mode: planning");
            List<string> steps = TaskPlan.Parse(await _host.AskModelOnceAsync(TaskPlan.PlanPrompt(_request, _host.ToolSummary), 400));
            bool run = false;
            TaskPlan plan = null;
            TaskCard card = null;
            if (steps.Count >= 2)
            {
                plan = new TaskPlan(steps);
                _feature._taskPlan = plan;
                var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _feature._taskDecision = decision;
                card = await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    TaskCard created = _feature.CreateTaskCard(plan, decision);
                    _host.AddCardToReply(created.View);
                    return created;
                });
                _feature._taskCard = card;
                _host.SetStatus("Review the plan, then run it.");
                using (token.Register(() => decision.TrySetCanceled(token)))
                    run = await decision.Task;
                _feature._taskDecision = null;
                if (!run)
                    await MainThread.InvokeOnMainThreadAsync(() => card.Close("Answering without a plan"));
            }
            else
                UiStallWatchdog.Mark("Task mode: no plan");
            if (!run)
            {
                // Not a task to break up: answered as usual.
                _host.SetStatus("Generating...");
                await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(messages, options, token))
                    yield return update;
                yield break;
            }

            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            card.Stop = stop.Cancel;
            await MainThread.InvokeOnMainThreadAsync(card.Begin);
            for (int i = 0; i < plan.Steps.Count && !stop.IsCancellationRequested; i++)
            {
                TaskPlan.Step step = plan.Steps[i];
                if (step.State == TaskPlan.State.Skipped)
                    continue;
                step.State = TaskPlan.State.Running;
                await MainThread.InvokeOnMainThreadAsync(card.Refresh);
                _host.SetStatus(string.Format("Step {0} of {1}: {2}", i + 1, plan.Steps.Count, step.Text));
                UiStallWatchdog.Mark("Task mode: step " + (i + 1));
                // A request of its own: the task, the plan and the results so far are in the message.
                var contents = new List<AIContent>();
                if (i == 0)
                    contents.AddRange(_attachments);
                contents.Add(new TextContent(plan.StepPrompt(_request, i)));
                var stepMessages = new[] { new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, contents) };
                var answer = new System.Text.StringBuilder();
                var tools = new List<ChatResponseUpdate>();
                IAsyncEnumerator<ChatResponseUpdate> stream = client.GetStreamingResponseAsync(stepMessages, options, stop.Token).GetAsyncEnumerator(stop.Token);
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
                        catch (Exception ex) when (!stop.IsCancellationRequested)
                        {
                            step.State = TaskPlan.State.Failed;
                            step.Result = TaskPlan.ShortResult(ex.Message);
                            UiStallWatchdog.Mark("Task mode: step failed: " + ex.Message);
                            break;
                        }
                        answer.Append(update.Contents.OfType<TextContent>().Select(t => t.Text).DefaultIfEmpty(string.Empty).Aggregate(string.Concat));
                        // The step's tool calls and results are the reply's step lines; its text is only its result.
                        var calls = update.Contents.Where(c => c is FunctionCallContent || c is FunctionResultContent).ToList();
                        if (calls.Count > 0)
                            yield return new ChatResponseUpdate(update.Role, calls);
                    }
                }
                finally
                {
                    await stream.DisposeAsync();
                }
                if (step.State == TaskPlan.State.Running)
                {
                    step.Result = TaskPlan.ShortResult(new ChatReply(answer.ToString()).Text);
                    step.State = TaskPlan.State.Done;
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
            // The final answer, from the results, in the reply.
            _host.SetStatus("Writing the answer...");
            var final = new[] { new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, plan.FinalPrompt(_request)) };
            await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(final, options, token))
                yield return update;
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
        var rows = new VerticalStackLayout { Spacing = 6 };
        var editor = new Editor { IsVisible = false, AutoSize = EditorAutoSizeOption.TextChanges, FontFamily = Theme.BodyFont, FontSize = 14, TextColor = Theme.PrimaryText, MinimumHeightRequest = 100 };
        var status = new Label { FontFamily = Theme.TitleFont, FontSize = 12, TextColor = Theme.SecondaryText, IsVisible = false };
        Button MakeButton(string text, bool primary) => new Button
        {
            Text = text,
            FontFamily = Theme.TitleFont,
            FontSize = 13,
            BackgroundColor = primary ? Theme.Accent : Colors.Transparent,
            TextColor = primary ? Colors.White : Theme.Accent,
            BorderColor = Theme.Accent,
            BorderWidth = primary ? 0 : 1,
            CornerRadius = 12,
            Padding = new Thickness(14, 4),
            MinimumHeightRequest = 36
        };
        Button runButton = MakeButton("Run plan", true), editButton = MakeButton("Edit", false), plainButton = MakeButton("Just answer", false), stopButton = MakeButton("Stop", false);
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
                    TextColor = step.State == TaskPlan.State.Done ? Theme.Success : step.State == TaskPlan.State.Running ? Theme.Accent : Theme.SecondaryText,
                    VerticalOptions = LayoutOptions.Start,
                    WidthRequest = 18
                }, 0, 0);
                var text = new VerticalStackLayout { Spacing = 2 };
                text.Children.Add(new Label
                {
                    Text = step.Text,
                    FontFamily = Theme.BodyFont,
                    FontSize = 14,
                    TextColor = step.State == TaskPlan.State.Skipped ? Theme.SecondaryText : Theme.PrimaryText,
                    TextDecorations = step.State == TaskPlan.State.Skipped ? TextDecorations.Strikethrough : TextDecorations.None
                });
                if (!string.IsNullOrWhiteSpace(step.Result))
                    text.Children.Add(new Label
                    {
                        Text = step.Result,
                        FontFamily = Theme.BodyFont,
                        FontSize = 12,
                        FontAttributes = FontAttributes.Italic,
                        TextColor = step.State == TaskPlan.State.Failed ? Colors.IndianRed : Theme.SecondaryText,
                        MaxLines = 3,
                        LineBreakMode = LineBreakMode.TailTruncation
                    });
                grid.Add(text, 1, 0);
                // Steps that haven't run can be skipped (and the skip undone) before and while the plan runs.
                if (!finished && (step.State == TaskPlan.State.Pending || (step.State == TaskPlan.State.Skipped && !running)) && !editor.IsVisible)
                {
                    TaskPlan.Step captured = step;
                    var skip = new Label
                    {
                        Text = step.State == TaskPlan.State.Skipped ? "Undo" : "Skip",
                        FontFamily = Theme.TitleFont,
                        FontSize = 12,
                        TextColor = Theme.Accent,
                        VerticalOptions = LayoutOptions.Start
                    };
                    skip.OnTap(() =>
                    {
                        captured.State = captured.State == TaskPlan.State.Skipped ? TaskPlan.State.Pending : TaskPlan.State.Skipped;
                        Refresh();
                    });
                    grid.Add(skip, 2, 0);
                }
                rows.Children.Add(grid);
            }
            runButton.IsVisible = editButton.IsVisible = plainButton.IsVisible = !running && !finished && !editor.IsVisible;
            stopButton.IsVisible = running && !finished;
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
            List<string> edited = TaskPlan.FromLines(editor.Text);
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
        content.Children.Add(new Label { Text = "\U0001F4CB  Plan", FontFamily = Theme.TitleFont, FontSize = 15, TextColor = Theme.PrimaryText });
        content.Children.Add(rows);
        content.Children.Add(new Label { Text = "One step per line, in the order they run.", FontFamily = Theme.BodyFont, FontSize = 11, TextColor = Theme.SecondaryText, IsVisible = false });
        content.Children.Add(editor);
        content.Children.Add(status);
        content.Children.Add(buttons);
        card.View = new Border
        {
            Stroke = Theme.Chevron,
            StrokeThickness = 1,
            BackgroundColor = Theme.TileBackground,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Padding = new Thickness(12, 10),
            Margin = new Thickness(6, 0),
            HorizontalOptions = LayoutOptions.Start,
            MaximumWidthRequest = 420,
            Content = content
        };
        SemanticProperties.SetDescription(card.View, "The plan for this task");
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
