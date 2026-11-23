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
/// The "Check homework" chip. With photos of homework pages attached, each page is cut into questions (<see cref="HomeworkSegmenter"/>,
/// PaddleOCR's text detector and recognizer on LiteRT, four-point regions so a slanted page comes out straight), then checked
/// as a whole (the model reads it, solves its questions and compares: three requests a page), and each top-level question (Q2
/// with its parts a, b, c is one) gets a card with its region of the page cut out as a picture (tap to enlarge), its questions,
/// the answers read from the handwriting and the marks. A summary card and the score end the reply. A page with no numbered
/// questions found, or without the text models (they are downloaded the first time, about 34 MB), gets the whole-page text
/// check instead. The chip is shown for a model that takes images and is per message.
/// </summary>
public sealed class HomeworkFeature : ChatFeature
{
    /// <summary>The kind of the reply's saved file that is its plan card.</summary>
    public const string PlanKind = "homework_plan";
    /// <summary>The kind of the reply's saved file that is one question's card.</summary>
    public const string QuestionKind = "homework_question";

    private bool _check;

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
        new ChatChip("\U0001F4DD Check homework", () => _check, on =>
        {
            _check = on;
            if (on)
                Host?.SetStatus("Check homework: attach photos of the pages and send. Each page is checked as a whole, then each question gets its picture. A few minutes a page.");
        })
        {
            IsVisible = () => Host?.Model?.AcceptsImages == true && Host.HandsFree != true
        };

    /// <summary>Whether the message being sent is to be checked as homework (the chip was on); the chip is off again afterwards.</summary>
    public bool TakeCheck()
    {
        if (!_check)
            return false;
        _check = false;
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

    private static string Icon(HomeworkVerdict verdict, bool skipped) =>
        skipped ? "⤷" : verdict switch
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

    // One part of a question on a card (2a); a question without parts has one part with no label.
    private sealed class QuestionPart
    {
        public string Label = string.Empty;
        public string QuestionText = string.Empty, StudentAnswer = string.Empty, MyAnswer = string.Empty, HowToSolve = string.Empty, WhatWentWrong = string.Empty, Reason = string.Empty;
        public HomeworkVerdict Verdict;
    }

    private sealed class Question
    {
        // The question's parts; empty for a card saved by the per-question check of earlier versions: its own fields are the one part.
        public List<QuestionPart> Parts = new List<QuestionPart>();
        public string Label;
        public int Page;
        public byte[] Crop;
        public string QuestionText = string.Empty, StudentAnswer = string.Empty, HowToSolve = string.Empty, WhatWentWrong = string.Empty, Reason = string.Empty;
        public HomeworkVerdict Verdict;
        // Only in a plan saved by the per-question check of earlier versions.
        public bool Skipped;
    }

    // One question's card: the picture, the question, the answer read from the handwriting, the verdict.
    private View BuildQuestionCard(Question q)
    {
        Color primary = ChatResources.ThemeColor("ChatPrimaryText"), secondary = ChatResources.ThemeColor("ChatSecondaryText");
        Color success = ChatResources.FindColor("ChatSuccess"), danger = ChatResources.FindColor("ChatDanger");
        string title = TitleFont, body = BodyFont;
        var content = new VerticalStackLayout { Spacing = 6 };
        Label Caption(string text) => new Label { Text = text, FontFamily = title, FontSize = 11, TextColor = secondary };
        Label Body(string text, Color color = null) => new Label { Text = text, FontFamily = body, FontSize = 14, TextColor = color ?? primary };
        string mark = Icon(q.Verdict, q.Skipped);
        content.Children.Add(new Label { Text = mark + "  Question " + q.Label, FontFamily = title, FontSize = 15, TextColor = primary });
        if (q.Crop != null)
            content.Children.Add(Picture(q.Crop, 240));
        IEnumerable<QuestionPart> parts = q.Parts.Count > 0 ? q.Parts : new[]
        {
            new QuestionPart
            {
                QuestionText = q.QuestionText, StudentAnswer = q.StudentAnswer, HowToSolve = q.HowToSolve,
                WhatWentWrong = q.WhatWentWrong, Reason = q.Reason, Verdict = q.Verdict
            }
        };
        foreach (QuestionPart part in parts)
        {
            if (part.Label.Length > 0)
                content.Children.Add(new Label { Text = Icon(part.Verdict, false) + "  " + part.Label + ")", FontFamily = title, FontSize = 14, TextColor = primary, Margin = new Thickness(0, 4, 0, 0) });
            if (part.QuestionText.Length > 0)
            {
                content.Children.Add(Caption("Question"));
                content.Children.Add(Body(ReplyMarkdown.PlainText(part.QuestionText)));
            }
            if (part.StudentAnswer.Length > 0)
            {
                content.Children.Add(Caption("Answer read from the handwriting"));
                content.Children.Add(Body(ReplyMarkdown.PlainText(part.StudentAnswer)));
            }
            switch (part.Verdict)
            {
                case HomeworkVerdict.Correct:
                    content.Children.Add(Body("✅  Correct", success));
                    break;
                case HomeworkVerdict.Wrong:
                    content.Children.Add(Body("❌  Not right", danger));
                    if (part.HowToSolve.Length > 0)
                    {
                        content.Children.Add(Caption("How to solve it"));
                        content.Children.Add(Body(ReplyMarkdown.PlainText(part.HowToSolve)));
                    }
                    else if (part.MyAnswer.Length > 0)
                    {
                        content.Children.Add(Caption("The answer I get"));
                        content.Children.Add(Body(ReplyMarkdown.PlainText(part.MyAnswer)));
                    }
                    if (part.WhatWentWrong.Length > 0)
                    {
                        content.Children.Add(Caption("What went wrong"));
                        content.Children.Add(Body(ReplyMarkdown.PlainText(part.WhatWentWrong)));
                    }
                    break;
                case HomeworkVerdict.NotAnswered:
                    content.Children.Add(Body("❓  Not answered", secondary));
                    break;
                case HomeworkVerdict.Unclear:
                    content.Children.Add(Body("❓  Can't tell" + (part.Reason.Length > 0 ? ": " + part.Reason : string.Empty), secondary));
                    break;
            }
        }
        return Frame(content);
    }

    private sealed class SavedPart
    {
        public string Label { get; set; }
        public string Question { get; set; }
        public string StudentAnswer { get; set; }
        public string MyAnswer { get; set; }
        public string Verdict { get; set; }
        public string HowToSolve { get; set; }
        public string WhatWentWrong { get; set; }
        public string Reason { get; set; }
    }

    private sealed class SavedQuestion
    {
        public List<SavedPart> Parts { get; set; }
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
        Parts = q.Parts.Count == 0 ? null : q.Parts.Select(p => new SavedPart
        {
            Label = p.Label, Question = p.QuestionText, StudentAnswer = p.StudentAnswer, MyAnswer = p.MyAnswer, Verdict = p.Verdict.ToString(),
            HowToSolve = p.HowToSolve, WhatWentWrong = p.WhatWentWrong, Reason = p.Reason
        }).ToList(),
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
                    Crop = string.IsNullOrEmpty(saved.Crop) ? null : Convert.FromBase64String(saved.Crop),
                    Parts = (saved.Parts ?? new List<SavedPart>()).Select(p => new QuestionPart
                    {
                        Label = p.Label ?? string.Empty,
                        QuestionText = p.Question ?? string.Empty,
                        StudentAnswer = p.StudentAnswer ?? string.Empty,
                        MyAnswer = p.MyAnswer ?? string.Empty,
                        Verdict = Enum.TryParse(p.Verdict, out HomeworkVerdict partVerdict) ? partVerdict : HomeworkVerdict.Unclear,
                        HowToSolve = p.HowToSolve ?? string.Empty,
                        WhatWentWrong = p.WhatWentWrong ?? string.Empty,
                        Reason = p.Reason ?? string.Empty
                    }).ToList()
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
                return BuildSummaryCard(questions, saved.Outcome);
            }
        }
        catch (Exception ex) when (ex is JsonException || ex is FormatException)
        {
            // A card that can't be read is left out.
        }
        return null;
    }

    // The summary: a row per question with its mark and picture (tap to enlarge), then the outcome.
    private static View BuildSummaryCard(List<Question> questions, string outcome)
    {
        Color primary = ChatResources.ThemeColor("ChatPrimaryText"), secondary = ChatResources.ThemeColor("ChatSecondaryText");
        string title = TitleFont, body = BodyFont;
        var rows = new VerticalStackLayout { Spacing = 4 };
        int page = -1;
        int pages = questions.Select(x => x.Page).Distinct().Count();
        foreach (Question q in questions)
        {
            if (q.Page != page)
            {
                page = q.Page;
                if (pages > 1)
                    rows.Children.Add(new Label { Text = "Page " + (page + 1), FontFamily = title, FontSize = 12, TextColor = secondary, Margin = new Thickness(0, 4, 0, 0) });
            }
            var grid = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }
            };
            grid.Add(new Label { Text = Icon(q.Verdict, q.Skipped), FontSize = 15, WidthRequest = 22, VerticalOptions = LayoutOptions.Center }, 0, 0);
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
            rows.Children.Add(grid);
        }
        var content = new VerticalStackLayout { Spacing = 8 };
        var heading = new Label { Text = "\U0001F4DD  Homework: " + questions.Count + (questions.Count == 1 ? " question" : " questions"), FontFamily = title, FontSize = 15, TextColor = primary };
        SemanticProperties.SetDescription(heading, "The questions checked");
        content.Children.Add(heading);
        content.Children.Add(rows);
        if (!string.IsNullOrEmpty(outcome))
            content.Children.Add(new Label { Text = outcome, FontFamily = title, FontSize = 12, TextColor = secondary });
        return Frame(content);
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
            // 2. Every page as a whole, then each top-level question gets its picture.
            string score = await PageCardsAsync(client, pages, token);
            Host.SetStatus(string.Empty);
            yield return new ChatResponseUpdate(ChatRole.Assistant, score.Length > 0 ? score : "Nothing was checked.");
        }

        // The three requests on one page: read, solve, compare. The comparison is tidied (blanks, no score line), and orderings
        // of a table's rows are checked in code.
        private async Task<(string Read, string Solved, string Check)> PageTextsAsync(IChatClient client, int page, CancellationToken token)
        {
            string label = _images.Count > 1 ? "Page " + (page + 1) + " of " + _images.Count + ": " : string.Empty;
            var image = new[] { _images[page] };
            Host.SetStatus(label + "reading the page...");
            string read = await AskAsync(client, image, HomeworkCheck.ReadPrompt(_note), 900, ReasoningEffort.None, token);
            Host.SetStatus(label + "solving the questions...");
            string solved = await AskAsync(client, image, HomeworkCheck.SolvePrompt(HomeworkCheck.QuestionsOnly(read)), 1500, null, token);
            Host.SetStatus(label + "comparing...");
            string check = HomeworkCheck.TidyCheck(await AskAsync(client, null, HomeworkCheck.ComparePrompt(read, solved), 900, ReasoningEffort.None, token));
            // An ordering of a copied table's rows is checked by sorting, not by the model.
            check = HomeworkCheck.CheckOrderings(read, check);
            return (read, solved, check);
        }

        // A page with no questions found (or without the text models): the whole-page check, in text.
        private async Task<string> WholePageAsync(IChatClient client, int page, CancellationToken token)
        {
            string header = _images.Count > 1 ? "## Page " + (page + 1) + " of " + _images.Count + "\n\n" : string.Empty;
            (string read, string solved, string check) = await PageTextsAsync(client, page, token);
            return header + "**What I read** (check it against the page)\n\n" + read + "\n\n**My own answers**\n\n" + solved + "\n\n**The check**\n\n" + check + "\n\n" + HomeworkCheck.Summary(new[] { check });
        }

        // Each page as a whole, and after each page one card per top-level question (Q2 with parts a, b, c is one):
        // its region of the page, then a block for each part. At the end a summary card and the score (one mark per question).
        private async Task<string> PageCardsAsync(IChatClient client, List<IReadOnlyList<HomeworkPageUnit>> pages, CancellationToken token)
        {
            var summary = new List<Question>();
            var all = new List<HomeworkSection>();
            for (int page = 0; page < pages.Count; page++)
            {
                token.ThrowIfCancellationRequested();
                (string read, string solved, string check) = await PageTextsAsync(client, page, token);
                // The question's whole picture, by its number.
                var crops = new Dictionary<string, byte[]>();
                foreach (HomeworkPageUnit unit in pages[page])
                    if (!crops.ContainsKey(unit.Unit.Question))
                        crops[unit.Unit.Question] = unit.ParentCrop ?? unit.Crop;
                IReadOnlyList<HomeworkSection> sections = HomeworkPageCheck.Group(
                    HomeworkPageCheck.Parse(read, solved, check), pages[page].Select(u => u.Unit.Question).Distinct().ToList());
                foreach (HomeworkSection section in sections)
                {
                    var q = new Question { Label = section.Question, Page = page, Verdict = section.Verdict };
                    if (crops.TryGetValue(section.Question, out byte[] crop))
                        q.Crop = crop;
                    foreach (HomeworkPageItem item in section.Items)
                        q.Parts.Add(new QuestionPart
                        {
                            Label = item.Part,
                            QuestionText = item.QuestionText,
                            StudentAnswer = item.StudentAnswer,
                            MyAnswer = item.MyAnswer,
                            Verdict = item.Verdict,
                            // The comparison's own words: what the student wrote, the answer and a hint.
                            WhatWentWrong = item.Verdict == HomeworkVerdict.Wrong ? item.Detail : string.Empty,
                            Reason = item.Verdict == HomeworkVerdict.Unclear ? item.Detail : string.Empty
                        });
                    if (q.Parts.Count == 0)
                        q.Parts.Add(new QuestionPart { Verdict = HomeworkVerdict.Unclear, Reason = "the page check didn't list this question" });
                    summary.Add(q);
                    all.Add(section);
                    await MainThread.InvokeOnMainThreadAsync(() => Host.AddCardToReply(_feature.BuildQuestionCard(q)));
                    Host.AddCardToReply(null, new ChatSavedFile(QuestionKind, "application/json", SaveQuestion(q)));
                }
            }
            if (summary.Count == 0)
                return string.Empty;
            await MainThread.InvokeOnMainThreadAsync(() => Host.AddCardToReply(BuildSummaryCard(summary, "Done")));
            // Kept with the reply, so the card comes back when the chat is reopened.
            Host.AddCardToReply(null, new ChatSavedFile(PlanKind, "application/json", JsonSerializer.SerializeToUtf8Bytes(new SavedPlan
            {
                Outcome = "Done",
                Rows = summary.Select(q => new SavedRow { Label = q.Label, Page = q.Page, Verdict = q.Verdict.ToString() }).ToList()
            })));
            return HomeworkCheck.Summary(new[] { HomeworkPageCheck.Marks(all) });
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
