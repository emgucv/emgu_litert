//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Models;
using Microsoft.Maui.Controls.Shapes;

namespace Maui.Demo.Lite;

// Task mode (TODO item 23): with the "Plan" chip on, a message is first broken into a short plan (the model writes a numbered
// list), shown as a checklist the user can edit, skip steps of and stop. Each step then runs on its own - a fresh conversation
// for the model, given the task, the plan and the earlier steps' short results (TaskPlan.StepPrompt), so its tool output doesn't
// fill the small window - with its tool calls as step lines and its result in the card,
// and last the final answer is written from the results into the reply. Tools that change something outside the app still ask
// with their own Confirm / Cancel cards. The saved chat holds the request and the final answer (the plan card isn't saved);
// the model is given the conversation again with the next message.
public partial class LiteRtLmChatPage
{
    // Completed by the card's buttons: true to run the plan, false to just answer. (A test completes it.)
    private TaskCompletionSource<bool> _taskDecision;
    // The plan being run (for the card's sake and for tests).
    private TaskPlan _taskPlan;
    private TaskCard _taskCard;

    // The tools the plan may use, by name, for the planner.
    private string TaskToolSummary()
    {
        var tools = new List<string> { "date and time", "calculator", "unit and currency conversion", "weather", "Wikipedia" };
        if (_webSearchSwitch.IsToggled)
            tools.Add("web search and page reading");
        tools.AddRange(ToolRouter.Groups.Select(g => g.Name.Replace('_', ' ')));
        return string.Join(", ", tools);
    }

    // One question to the model in a conversation of its own (like a summary): its reply.
    private async Task<string> AskModelOnceAsync(string prompt, int maxTokens)
    {
        if (SelectedChoice == ModelChoice.Server)
            return await SummarizeOnServerAsync(prompt);
        LanguageModel model = _model;
        ResetAgent();
        _chat?.LoadHistory(Array.Empty<Emgu.LiteRT.LM.Models.ChatMessage>());
        Chat asker = model.CreateChat();
        try
        {
            asker.MaxOutputTokens = maxTokens;
            asker.EnableThinking = false;
            return (await asker.SendAsync(prompt)).Text.Trim();
        }
        finally
        {
            await Task.Run(() => asker.Dispose());
        }
    }

    private async Task<ChatReply> RunTaskAsync(string request, List<PendingAttachment> attachments, StreamingBubble bubble,
        CancellationToken token)
    {
        SetStatus("Planning...");
        UiStallWatchdog.Mark("Task mode: planning");
        List<string> steps = TaskPlan.Parse(await AskModelOnceAsync(TaskPlan.PlanPrompt(request, TaskToolSummary()), 400));
        if (steps.Count < 2)
        {
            // Not a task to break up: answer it as usual.
            UiStallWatchdog.Mark("Task mode: no plan");
            SetStatus("Generating...");
            return await RunAgentAsync(request, attachments, bubble, token);
        }
        var plan = new TaskPlan(steps);
        _taskPlan = plan;
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _taskDecision = decision;
        TaskCard card = await MainThread.InvokeOnMainThreadAsync(() => AddTaskCard(bubble, plan, decision));
        _taskCard = card;
        SetStatus("Review the plan, then run it.");
        bool run;
        using (token.Register(() => decision.TrySetCanceled(token)))
            run = await decision.Task;
        _taskDecision = null;
        if (!run)
        {
            await MainThread.InvokeOnMainThreadAsync(() => card.Close("Answering without a plan"));
            SetStatus("Generating...");
            return await RunAgentAsync(request, attachments, bubble, token);
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        card.Stop = stop.Cancel;
        await MainThread.InvokeOnMainThreadAsync(card.Begin);
        try
        {
            for (int i = 0; i < plan.Steps.Count && !stop.IsCancellationRequested; i++)
            {
                TaskPlan.Step step = plan.Steps[i];
                if (step.State == TaskPlan.State.Skipped)
                    continue;
                step.State = TaskPlan.State.Running;
                await MainThread.InvokeOnMainThreadAsync(card.Refresh);
                SetStatus(string.Format("Step {0} of {1}: {2}", i + 1, plan.Steps.Count, step.Text));
                UiStallWatchdog.Mark("Task mode: step " + (i + 1));
                // A conversation of its own: the task, the plan and the results so far are in the message.
                _session = null;
                _agentPrimeHistory = null;
                StreamingBubble stepBubble = await MainThread.InvokeOnMainThreadAsync(() => AddStepBubble(bubble));
                try
                {
                    ChatReply reply = await RunAgentAsync(plan.StepPrompt(request, i), i == 0 ? attachments : new List<PendingAttachment>(), stepBubble, stop.Token);
                    step.Result = TaskPlan.ShortResult(reply.Text);
                    step.State = TaskPlan.State.Done;
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    step.State = TaskPlan.State.Skipped;
                }
                catch (Exception ex) when (!stop.IsCancellationRequested)
                {
                    step.State = TaskPlan.State.Failed;
                    step.Result = TaskPlan.ShortResult(ex.Message);
                    UiStallWatchdog.Mark("Task mode: step failed: " + ex.Message);
                }
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    stepBubble.Row.IsVisible = false;
                    card.Refresh();
                });
            }
            token.ThrowIfCancellationRequested();
            if (stop.IsCancellationRequested)
            {
                foreach (TaskPlan.Step pending in plan.Steps.Where(s => s.State == TaskPlan.State.Pending))
                    pending.State = TaskPlan.State.Skipped;
                await MainThread.InvokeOnMainThreadAsync(() => card.Close("Stopped"));
                return new ChatReply(plan.StoppedSummary());
            }
            await MainThread.InvokeOnMainThreadAsync(() => card.Close("Done"));
            // The final answer, from the results, in the reply.
            SetStatus("Writing the answer...");
            _session = null;
            _agentPrimeHistory = null;
            return await RunAgentAsync(plan.FinalPrompt(request), new List<PendingAttachment>(), bubble, token);
        }
        finally
        {
            // The model is given the conversation (the request and the answer) again with the next message.
            _needsPrime = true;
            _session = null;
        }
    }

    // A step's bubble goes above the reply's (before it in the transcript); it isn't shown - the plan card and the tool step lines are.
    private StreamingBubble AddStepBubble(StreamingBubble before)
    {
        StreamingBubble stepBubble = AddStreamingBubble();
        stepBubble.Row.IsVisible = false; // the plan card and the step lines show the progress
        _transcript.Children.Remove(stepBubble.Row);
        int index = _transcript.Children.IndexOf(before.Row);
        if (index < 0)
            _transcript.Children.Add(stepBubble.Row);
        else
            _transcript.Children.Insert(index, stepBubble.Row);
        return stepBubble;
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
    private TaskCard AddTaskCard(StreamingBubble before, TaskPlan plan, TaskCompletionSource<bool> decision)
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
        int index = _transcript.Children.IndexOf(before.Row);
        if (index < 0)
            _transcript.Children.Add(card.View);
        else
            _transcript.Children.Insert(index, card.View);
        ScrollToEnd();
        return card;
    }
}

#endif
