//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Text.Json;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Models;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Controls.Shapes;

namespace Emgu.LiteRT.LM.Maui.CV;

/// <summary>
/// The "Check homework" chip: with photos of homework pages attached, each page is cut into one picture per question (or
/// part of one: <see cref="HomeworkSegmenter"/>, PaddleOCR's text detector and recognizer on LiteRT, four-point regions so a
/// slanted page comes out straight), a plan lists the questions to check (Run, Stop, and Skip for a question), and then each
/// question is checked on its own with a card: its picture first (tap to enlarge), the question, the answer read from the
/// handwriting, and a green check - or a red cross with how to solve it and what went wrong. Per question the model reads the
/// picture, solves the question without seeing the student's answer, and judges the two (<see cref="HomeworkQuestionCheck"/>);
/// an answer that matches the solution is accepted and a blank one isn't judged, both without a request. A page with no
/// numbered questions found, or without the text models (they are downloaded the first time, about 34 MB), gets the
/// whole-page text check instead. The chip is shown for a model that takes images and is per message.
/// </summary>
public sealed class HomeworkFeature : ChatFeature
{
    /// <summary>The kind of the reply's saved file that is its plan card.</summary>
    public const string PlanKind = "homework_plan";
    /// <summary>The kind of the reply's saved file that is one question's card.</summary>
    public const string QuestionKind = "homework_question";

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
                Host?.SetStatus("Check homework: attach photos of the pages and send. Each question is cut out, read, solved and checked, one by one.");
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

    // ---------- the cards ----------

    private static string TitleFont => ChatResources.Find<string>("ChatTitleFont");
    private static string BodyFont => ChatResources.Find<string>("ChatBodyFont");

    private static Border Frame(View content) => new Border
    {
        Stroke = ChatResources.ThemeColor("ChatRowBorder"),
        StrokeThickness = 1,
        BackgroundColor = ChatResources.ThemeColor("ChatSurface"),
        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
        Padding = new Thickness(12, 10),
        Margin = new Thickness(6, 0),
        HorizontalOptions = LayoutOptions.Start,
        MaximumWidthRequest = 440,
        Content = content
    };

    private static string Icon(HomeworkVerdict verdict, bool skipped, bool running) =>
        skipped ? "⤷" : running ? "◐" : verdict switch
        {
            HomeworkVerdict.Correct => "✅",
            HomeworkVerdict.Wrong => "❌",
            HomeworkVerdict.Unclear or HomeworkVerdict.NotAnswered => "❓",
            _ => "○"
        };

    // A picture that opens full screen when tapped.
    private static Image Picture(byte[] jpeg, double maxHeight)
    {
        var image = new Image
        {
            Source = ImageSource.FromStream(() => new MemoryStream(jpeg)),
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Start,
            MaximumHeightRequest = maxHeight
        };
        SemanticProperties.SetDescription(image, "The question as photographed, tap to enlarge");
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await ChatImageViewer.ShowAsync(image, jpeg);
        image.GestureRecognizers.Add(tap);
        return image;
    }

    private sealed class Question
    {
        public string Label;
        public int Page;
        public byte[] Crop, ParentCrop;
        public IReadOnlyList<string> Text = new List<string>();
        public string QuestionText = string.Empty, StudentAnswer = string.Empty, HowToSolve = string.Empty, WhatWentWrong = string.Empty, Reason = string.Empty;
        public HomeworkVerdict Verdict;
        public bool Skipped, Running;
        public string Status = string.Empty;
        public Action Refresh;
    }

    // One question's card: the picture, the question, the answer read from the handwriting, the verdict. Refreshed as the question is checked.
    private View BuildQuestionCard(Question q)
    {
        Color primary = ChatResources.ThemeColor("ChatPrimaryText"), secondary = ChatResources.ThemeColor("ChatSecondaryText");
        Color success = ChatResources.FindColor("ChatSuccess"), danger = ChatResources.FindColor("ChatDanger");
        string title = TitleFont, body = BodyFont;
        var content = new VerticalStackLayout { Spacing = 6 };
        Label Caption(string text) => new Label { Text = text, FontFamily = title, FontSize = 11, TextColor = secondary };
        Label Body(string text, Color color = null) => new Label { Text = text, FontFamily = body, FontSize = 14, TextColor = color ?? primary };
        void Refresh()
        {
            content.Children.Clear();
            string mark = Icon(q.Verdict, q.Skipped, q.Running);
            content.Children.Add(new Label { Text = mark + "  Question " + q.Label, FontFamily = title, FontSize = 15, TextColor = primary });
            if (q.Crop != null)
                content.Children.Add(Picture(q.Crop, 240));
            if (q.QuestionText.Length > 0)
            {
                content.Children.Add(Caption("Question"));
                content.Children.Add(Body(ReplyMarkdown.PlainText(q.QuestionText)));
            }
            if (q.StudentAnswer.Length > 0)
            {
                content.Children.Add(Caption("Answer read from the handwriting"));
                content.Children.Add(Body(ReplyMarkdown.PlainText(q.StudentAnswer)));
            }
            switch (q.Verdict)
            {
                case HomeworkVerdict.Correct:
                    content.Children.Add(Body("✅  Correct", success));
                    break;
                case HomeworkVerdict.Wrong:
                    content.Children.Add(Body("❌  Not right", danger));
                    if (q.HowToSolve.Length > 0)
                    {
                        content.Children.Add(Caption("How to solve it"));
                        content.Children.Add(Body(ReplyMarkdown.PlainText(q.HowToSolve)));
                    }
                    if (q.WhatWentWrong.Length > 0)
                    {
                        content.Children.Add(Caption("What went wrong"));
                        content.Children.Add(Body(ReplyMarkdown.PlainText(q.WhatWentWrong)));
                    }
                    break;
                case HomeworkVerdict.NotAnswered:
                    content.Children.Add(Body("❓  Not answered", secondary));
                    break;
                case HomeworkVerdict.Unclear:
                    content.Children.Add(Body("❓  Can't tell" + (q.Reason.Length > 0 ? ": " + q.Reason : string.Empty), secondary));
                    break;
            }
            if (q.Running && q.Status.Length > 0)
                content.Children.Add(new Label { Text = q.Status, FontFamily = title, FontSize = 12, TextColor = secondary });
        }
        q.Refresh = () => MainThread.BeginInvokeOnMainThread(Refresh);
        Refresh();
        return Frame(content);
    }

    private sealed class SavedQuestion
    {
        public string Label { get; set; }
        public string Question { get; set; }
        public string StudentAnswer { get; set; }
        public string Verdict { get; set; }
        public string HowToSolve { get; set; }
        public string WhatWentWrong { get; set; }
        public string Reason { get; set; }
        public string Crop { get; set; }
    }

    private static byte[] SaveQuestion(Question q) => JsonSerializer.SerializeToUtf8Bytes(new SavedQuestion
    {
        Label = q.Label,
        Question = q.QuestionText,
        StudentAnswer = q.StudentAnswer,
        Verdict = q.Verdict.ToString(),
        HowToSolve = q.HowToSolve,
        WhatWentWrong = q.WhatWentWrong,
        Reason = q.Reason,
        Crop = q.Crop == null ? null : Convert.ToBase64String(q.Crop)
    });

    private sealed class SavedRow
    {
        public string Label { get; set; }
        public int Page { get; set; }
        public string Verdict { get; set; }
        public bool Skipped { get; set; }
    }

    private sealed class SavedPlan
    {
        public string Outcome { get; set; }
        public List<SavedRow> Rows { get; set; }
    }

    /// <inheritdoc/>
    public override View RestoreSavedFile(ChatSavedFile file)
    {
        try
        {
            if (file.Kind == QuestionKind)
            {
                SavedQuestion saved = JsonSerializer.Deserialize<SavedQuestion>(file.Data);
                if (saved == null)
                    return null;
                return BuildQuestionCard(new Question
                {
                    Label = saved.Label,
                    QuestionText = saved.Question ?? string.Empty,
                    StudentAnswer = saved.StudentAnswer ?? string.Empty,
                    Verdict = Enum.TryParse(saved.Verdict, out HomeworkVerdict verdict) ? verdict : HomeworkVerdict.Unclear,
                    HowToSolve = saved.HowToSolve ?? string.Empty,
                    WhatWentWrong = saved.WhatWentWrong ?? string.Empty,
                    Reason = saved.Reason ?? string.Empty,
                    Crop = string.IsNullOrEmpty(saved.Crop) ? null : Convert.FromBase64String(saved.Crop)
                });
            }
            if (file.Kind == PlanKind)
            {
                SavedPlan saved = JsonSerializer.Deserialize<SavedPlan>(file.Data);
                if (saved?.Rows == null)
                    return null;
                var questions = saved.Rows.Select(r => new Question
                {
                    Label = r.Label,
                    Page = r.Page,
                    Skipped = r.Skipped,
                    Verdict = Enum.TryParse(r.Verdict, out HomeworkVerdict verdict) ? verdict : HomeworkVerdict.Pending
                }).ToList();
                return BuildPlanCard(questions, new TaskCompletionSource<bool>(), saved.Outcome, null).View;
            }
        }
        catch (Exception ex) when (ex is JsonException || ex is FormatException)
        {
            // A card that can't be read is left out.
        }
        return null;
    }

    private sealed class PlanCard
    {
        public View View;
        public Action Refresh;
        public Action Begin;
        public Action<string> Close;
        public Action Stop;
    }

    // The plan: the pages' questions as rows (their pictures, Skip), Run and Stop. A saved plan (outcome given) has no buttons.
    private PlanCard BuildPlanCard(List<Question> questions, TaskCompletionSource<bool> decision, string closedWith, Action stop)
    {
        var card = new PlanCard();
        bool running = false, finished = closedWith != null;
        Color accent = ChatResources.FindColor("ChatAccent"), accentText = ChatResources.FindColor("ChatAccentText");
        Color primary = ChatResources.ThemeColor("ChatPrimaryText"), secondary = ChatResources.ThemeColor("ChatSecondaryText");
        string title = TitleFont, body = BodyFont;
        var rows = new VerticalStackLayout { Spacing = 4 };
        var status = new Label { FontFamily = title, FontSize = 12, TextColor = secondary, IsVisible = closedWith != null, Text = closedWith ?? string.Empty };
        Button Make(string text, bool filled) => new Button
        {
            Text = text,
            FontFamily = title,
            FontSize = 13,
            BackgroundColor = filled ? accent : Colors.Transparent,
            TextColor = filled ? Colors.White : accentText,
            BorderColor = accent,
            BorderWidth = filled ? 0 : 1,
            CornerRadius = 12,
            Padding = new Thickness(14, 4),
            MinimumHeightRequest = 36
        };
        Button run = Make("Check them", true), stopButton = Make("Stop", false);
        stopButton.IsVisible = false;
        var buttons = new HorizontalStackLayout { Spacing = 8, Children = { run, stopButton }, IsVisible = !finished };

        void Refresh()
        {
            rows.Children.Clear();
            int page = -1;
            foreach (Question q in questions)
            {
                if (q.Page != page)
                {
                    page = q.Page;
                    int pages = questions.Select(x => x.Page).Distinct().Count();
                    if (pages > 1)
                        rows.Children.Add(new Label { Text = "Page " + (page + 1), FontFamily = title, FontSize = 12, TextColor = secondary, Margin = new Thickness(0, 4, 0, 0) });
                }
                var grid = new Grid
                {
                    ColumnSpacing = 8,
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
                };
                grid.Add(new Label { Text = Icon(q.Verdict, q.Skipped, q.Running), FontSize = 15, WidthRequest = 22, VerticalOptions = LayoutOptions.Center }, 0, 0);
                if (q.Crop != null)
                {
                    Image thumbnail = Picture(q.Crop, 36);
                    thumbnail.MaximumWidthRequest = 150;
                    grid.Add(thumbnail, 1, 0);
                }
                grid.Add(new Label
                {
                    Text = "Question " + q.Label,
                    FontFamily = body,
                    FontSize = 14,
                    TextColor = q.Skipped ? secondary : primary,
                    TextDecorations = q.Skipped ? TextDecorations.Strikethrough : TextDecorations.None,
                    VerticalOptions = LayoutOptions.Center
                }, 2, 0);
                if (!finished && !running && q.Crop != null)
                {
                    Question captured = q;
                    Label skip = ChatPage.Link(q.Skipped ? "Undo" : "Skip", () => { captured.Skipped = !captured.Skipped; Refresh(); });
                    skip.FontSize = 12;
                    skip.VerticalOptions = LayoutOptions.Center;
                    grid.Add(skip, 3, 0);
                }
                rows.Children.Add(grid);
            }
            run.IsVisible = !running && !finished;
            stopButton.IsVisible = running && !finished;
        }
        run.Clicked += (_, _) =>
        {
            if (questions.All(q => q.Skipped))
                return;
            running = true;
            Refresh();
            decision.TrySetResult(true);
        };
        stopButton.Clicked += (_, _) =>
        {
            stopButton.IsEnabled = false;
            card.Stop?.Invoke();
            stop?.Invoke();
        };
        var content = new VerticalStackLayout { Spacing = 8 };
        var heading = new Label { Text = "\U0001F4DD  Homework: " + questions.Count + (questions.Count == 1 ? " question" : " questions"), FontFamily = title, FontSize = 15, TextColor = primary };
        SemanticProperties.SetDescription(heading, "The questions to check");
        content.Children.Add(heading);
        content.Children.Add(rows);
        content.Children.Add(status);
        content.Children.Add(buttons);
        card.View = Frame(content);
        card.Refresh = () => MainThread.BeginInvokeOnMainThread(Refresh);
        card.Begin = () => MainThread.BeginInvokeOnMainThread(() => { running = true; Refresh(); });
        card.Close = outcome => MainThread.BeginInvokeOnMainThread(() =>
        {
            // Kept with the reply, so the card comes back when the chat is reopened.
            if (!finished && Host != null)
                Host.AddCardToReply(null, new ChatSavedFile(PlanKind, "application/json", JsonSerializer.SerializeToUtf8Bytes(new SavedPlan
                {
                    Outcome = outcome,
                    Rows = questions.Select(q => new SavedRow { Label = q.Label, Page = q.Page, Verdict = q.Verdict.ToString(), Skipped = q.Skipped }).ToList()
                })));
            finished = true;
            running = false;
            status.Text = outcome;
            status.IsVisible = true;
            buttons.IsVisible = false;
            Refresh();
        });
        Refresh();
        return card;
    }

    // ---------- the check ----------

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
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Attach photos of the homework pages (+ > Photo or Camera), then send again with \U0001F4DD Check homework on.");
                yield break;
            }
            IChatClient client = Host.ChatClient;
            // 1. Cut the pages into questions (the text models are downloaded the first time).
            var pages = new List<IReadOnlyList<HomeworkPageUnit>>();
            string problem = null;
            using (var segmenter = new HomeworkSegmenter(FileSystem.CacheDirectory))
            {
                try
                {
                    Host.SetStatus("Getting the text finder ready...");
                    await segmenter.PrepareAsync(2, message => Host.SetStatus(message), token);
                    for (int page = 0; page < _images.Count; page++)
                    {
                        Host.SetStatus("Finding the questions on page " + (page + 1) + "...");
                        byte[] bytes = _images[page].Data.ToArray();
                        pages.Add(await Task.Run(() => segmenter.Segment(bytes), token));
                    }
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    problem = ex.Message;
                    pages.Clear();
                }
            }
            token.ThrowIfCancellationRequested();
            if (problem != null || pages.All(p => p.Count == 0))
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, problem != null
                    ? "I couldn't get the text finder ready (" + problem + "), so each page is checked as a whole.\n\n"
                    : "I couldn't find numbered questions on the photos, so each page is checked as a whole.\n\n");
                for (int page = 0; page < _images.Count; page++)
                    yield return new ChatResponseUpdate(ChatRole.Assistant, await WholePageAsync(client, page, token));
                Host.SetStatus(string.Empty);
                yield break;
            }
            // 2. The plan.
            var questions = new List<Question>();
            for (int page = 0; page < pages.Count; page++)
                foreach (HomeworkPageUnit unit in pages[page])
                    questions.Add(new Question { Label = unit.Label, Page = page, Crop = unit.Crop, ParentCrop = unit.ParentCrop, Text = unit.Text });
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            PlanCard plan = await MainThread.InvokeOnMainThreadAsync(() =>
            {
                PlanCard created = _feature.BuildPlanCard(questions, decision, null, () => stop.Cancel());
                Host.AddCardToReply(created.View);
                return created;
            });
            Host.SetStatus("Review the questions, then check them.");
            using (token.Register(() => decision.TrySetCanceled(token)))
                await decision.Task;
            // 3. One by one.
            string outcome = "Done";
            foreach (Question q in questions)
            {
                if (q.Skipped)
                    continue;
                if (stop.IsCancellationRequested)
                {
                    outcome = "Stopped";
                    break;
                }
                q.Running = true;
                plan.Refresh();
                await MainThread.InvokeOnMainThreadAsync(() => Host.AddCardToReply(_feature.BuildQuestionCard(q)));
                try
                {
                    await CheckAsync(client, q, stop.Token);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested && !token.IsCancellationRequested)
                {
                    q.Verdict = HomeworkVerdict.Unclear;
                    q.Reason = "stopped";
                    outcome = "Stopped";
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    q.Verdict = HomeworkVerdict.Unclear;
                    q.Reason = ex.Message;
                }
                q.Running = false;
                q.Status = string.Empty;
                q.Refresh?.Invoke();
                plan.Refresh();
                Host.AddCardToReply(null, new ChatSavedFile(QuestionKind, "application/json", SaveQuestion(q)));
                if (outcome == "Stopped")
                    break;
            }
            token.ThrowIfCancellationRequested();
            plan.Close(outcome);
            Host.SetStatus(string.Empty);
            string score = HomeworkCheck.Summary(new[] { string.Join("\n", questions.Where(q => !q.Skipped).Select(q =>
                q.Verdict == HomeworkVerdict.Correct ? "✅" : q.Verdict == HomeworkVerdict.Wrong ? "❌" : q.Verdict == HomeworkVerdict.Pending ? string.Empty : "❓")) });
            yield return new ChatResponseUpdate(ChatRole.Assistant, score.Length > 0 ? score : "Nothing was checked.");
        }

        // One question, by HomeworkQuestionChecker; the card follows as the result fills in.
        private async Task CheckAsync(IChatClient client, Question q, CancellationToken token)
        {
            var result = new HomeworkQuestionResult();
            void Progress(string text)
            {
                // "read": the question and the answer are known, show them while the solving goes on.
                if (text != "read")
                {
                    q.Status = text;
                    Host.SetStatus("Question " + q.Label + ": " + text);
                }
                q.QuestionText = result.Question;
                q.StudentAnswer = result.StudentAnswer;
                q.Refresh?.Invoke();
            }
            try
            {
                await HomeworkQuestionChecker.CheckAsync(client, q.Label, q.Crop, q.ParentCrop, q.Text, _note, result, Progress, token);
            }
            finally
            {
                q.Verdict = result.Verdict;
                q.QuestionText = result.Question;
                q.StudentAnswer = result.StudentAnswer;
                q.HowToSolve = result.HowToSolve;
                q.WhatWentWrong = result.WhatWentWrong;
                q.Reason = result.Reason;
            }
        }

        // A page with no questions found (or without the text models): today's whole-page check, in text.
        private async Task<string> WholePageAsync(IChatClient client, int page, CancellationToken token)
        {
            string label = _images.Count > 1 ? "Page " + (page + 1) + " of " + _images.Count + ": " : string.Empty;
            string header = _images.Count > 1 ? "## Page " + (page + 1) + " of " + _images.Count + "\n\n" : string.Empty;
            var image = new[] { _images[page] };
            Host.SetStatus(label + "reading the page...");
            string read = await AskAsync(client, image, HomeworkCheck.ReadPrompt(_note), 900, ReasoningEffort.None, token);
            Host.SetStatus(label + "solving the questions...");
            string solved = await AskAsync(client, image, HomeworkCheck.SolvePrompt(HomeworkCheck.QuestionsOnly(read)), 1500, null, token);
            Host.SetStatus(label + "comparing...");
            string check = HomeworkCheck.TidyCheck(await AskAsync(client, null, HomeworkCheck.ComparePrompt(read, solved), 900, ReasoningEffort.None, token));
            return header + "**What I read** (check it against the page)\n\n" + read + "\n\n**My own answers**\n\n" + solved + "\n\n**The check**\n\n" + check + "\n\n" + HomeworkCheck.Summary(new[] { check });
        }

        // One request of its own (no tools, no earlier chat), with the pictures when given; the thinking is dropped from the text.
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
