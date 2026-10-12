//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls.Shapes;
using Emgu.LiteRT.LM.Models;

namespace Maui.Demo.Lite
{
    // Questions about a document: "+" > Document attaches a PDF, Word or text file to the chat. Its text is split into
    // passages and indexed with an embedding model (EmbeddingGemma 2 Text Vision, downloaded on first use) together with
    // its pictures and images of its pages without text (scans, charts drawn as shapes) - the latter only for models
    // that take images. Each message is then sent with the passages most related to it (the model's context can't
    // hold a whole document) and, for models that take images, the best-matching picture or page when it matches well.
    public partial class LiteRtLmChatPage
    {
        // The attached document: what is indexed so far, and its chip above the message box.
        private sealed class DocumentState
        {
            public string Name;
            public int PageCount;
            public DocumentIndex Index;
            public Task<bool> Ready;
            public CancellationTokenSource Cancel = new CancellationTokenSource();
            public Border Chip;
            public Label Detail;
            public ActivityIndicator Spinner;
        }

        // What a message is sent with from the document.
        private sealed class DocumentContext
        {
            // Given to the model before the message (null if nothing new).
            public string Text;
            // A picture or page image to attach (null if none).
            public PendingAttachment Image;
            // The line shown above the reply, e.g. "Looked in report.pdf: pages 2 and 5".
            public string Step;
            public List<DocumentPassage> Passages = new List<DocumentPassage>();
            public DocumentImage ImageItem;
        }

        private const string DocumentStepPrefix = "Looked in ";
        // Images are only attached when they match the message about as well as the best passage does, and well in
        // absolute terms (cosine similarity, EmbeddingGemma 2). An absolute threshold alone doesn't work: a paper's
        // diagrams, full of text, matched every question about the paper at 0.62-0.76, while the images that helped
        // (a scanned page with the answer, the photo asked about, the diagram of the formula asked about) scored
        // -0.01 to +0.12 against the best passage, and the ones that didn't -0.04 to -0.22. A message that asks for a
        // picture gets more leeway.
        private const float ImageMatchScore = 0.6f;
        private const float ImageMargin = 0.02f;
        private const float AskedImageMatchScore = 0.52f;
        private const float AskedImageMargin = 0.08f;
        // At most this many pictures and page images are indexed (each takes seconds on a phone's CPU).
        private static int MaxDocumentImages => DeviceInfo.Idiom == DeviceIdiom.Desktop ? 24 : 10;
        private static readonly Regex OverviewQuestion = new Regex(
            @"\b(summar|overview|tl;?dr|main points|key points|what is (this|the) (document|file|pdf|paper|report) about|what does (this|the) (document|file|pdf|paper|report) say)",
            RegexOptions.IgnoreCase);
        private static readonly Regex PictureQuestion = new Regex(
            @"\b(picture|image|photo|figure|fig\.|chart|graph|diagram|map|illustration|drawing|scan|logo|screenshot|visuali[sz]ation)s?\b",
            RegexOptions.IgnoreCase);
        private static readonly Regex PageReference = new Regex(@"\bpage\s+(\d+)\b", RegexOptions.IgnoreCase);
        // "Figure 2", "fig. 2", "chart 3"...: the picture is on the page whose text has that caption.
        private static readonly Regex FigureReference = new Regex(
            @"\b(fig(?:ure)?|chart|diagram|graph|photo|picture|image|illustration|map|plate)\.?\s*(\d+)\b", RegexOptions.IgnoreCase);

        private readonly HorizontalStackLayout _documentBar;
        private DocumentState _document;
        private EmbeddingGemma2TextVision _embedder;
        private Task<EmbeddingGemma2TextVision> _embedderLoad;
        // What the model's conversation already holds: passages and images given with earlier messages since the
        // conversation was last compacted.
        private readonly HashSet<int> _givenPassages = new HashSet<int>();
        private readonly HashSet<int> _givenImages = new HashSet<int>();

        private static bool IsToolStep(string step) => !step.StartsWith(DocumentStepPrefix, StringComparison.Ordinal);

        // ---------- Attaching ----------

        private void AttachDocument(byte[] bytes, string name)
        {
            RemoveDocument();
            UiStallWatchdog.Mark(string.Format("Attach document {0}: {1} bytes", name, bytes.Length));
            DocumentState state = new DocumentState { Name = name };
            BuildDocumentChip(state);
            _document = state;
            _documentBar.Children.Add(state.Chip);
            _documentBar.IsVisible = true;
            state.Ready = IndexDocumentAsync(state, bytes);
        }

        private void BuildDocumentChip(DocumentState state)
        {
            state.Detail = new Label
            {
                Text = "Reading...",
                FontFamily = Theme.BodyFont,
                FontSize = 12,
                TextColor = Theme.SecondaryText,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            state.Spinner = new ActivityIndicator { IsRunning = true, Color = Theme.Accent, WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center };
            var remove = new Label
            {
                Text = Theme.GlyphClose,
                FontFamily = Theme.IconFont,
                FontSize = 18,
                TextColor = Theme.SecondaryText,
                VerticalOptions = LayoutOptions.Center
            };
            remove.OnTap(() =>
            {
                if (!_busy && _document == state)
                {
                    RemoveDocument();
                    SetStatus("The document was removed. Earlier answers keep what they used of it.");
                }
            });
            SemanticProperties.SetDescription(remove, "Remove the document");
            state.Chip = new Border
            {
                BackgroundColor = Theme.TileBackground,
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                Padding = new Thickness(8, 6),
                Content = new HorizontalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        Theme.MakeIcon(Theme.GlyphText, Theme.Accent, 20),
                        new VerticalStackLayout
                        {
                            Spacing = 0,
                            VerticalOptions = LayoutOptions.Center,
                            MaximumWidthRequest = 240,
                            Children =
                            {
                                new Label
                                {
                                    Text = state.Name,
                                    FontFamily = Theme.TitleFont,
                                    FontSize = 13,
                                    TextColor = Theme.PrimaryText,
                                    LineBreakMode = LineBreakMode.MiddleTruncation
                                },
                                state.Detail
                            }
                        },
                        state.Spinner,
                        remove
                    }
                }
            };
        }

        // Read the document, load the embedding model (downloading it the first time) and index the document - all in
        // the background, with progress in the chip. Returns false if it failed or was cancelled.
        private async Task<bool> IndexDocumentAsync(DocumentState state, byte[] bytes)
        {
            CancellationToken cancel = state.Cancel.Token;
            Progress<string> stage = new Progress<string>(text =>
            {
                if (_document == state)
                    state.Detail.Text = text;
            });
            try
            {
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                // Pictures only for models that take images; a model chosen later gets the text only.
                int maxImages = SelectedOption.AcceptsImages ? MaxDocumentImages : 0;
                DocumentContent content = await DocumentReader.ReadAsync(bytes, state.Name, maxImages, stage, cancel);
                state.PageCount = content.PageCount;
                DocumentIndex index = new DocumentIndex();
                foreach (var text in content.Texts)
                    index.AddText(text.Text, text.Location);
                foreach (var image in content.Images)
                    index.AddImage(image.Jpeg, "image/jpeg", image.Location, image.IsPage);
                if (index.Passages.Count == 0 && index.Images.Count == 0)
                    throw new InvalidOperationException(content.PageCount > 0 && maxImages == 0
                        ? "it has no text (scanned pages need a model that understands images, e.g. Gemma 4)"
                        : "no text was found in it");
                UiStallWatchdog.Mark(string.Format("Document read in {0:F1}s: {1} passages, {2} images, {3} pages ({4} skipped), {5} images skipped",
                    watch.Elapsed.TotalSeconds, index.Passages.Count, index.Images.Count, content.PageCount, content.PagesSkipped, content.ImagesSkipped));

                EmbeddingGemma2TextVision embedder = await EnsureEmbedderAsync(stage, cancel);
                watch.Restart();
                Progress<DocumentIndexProgress> indexed = new Progress<DocumentIndexProgress>(p =>
                {
                    if (_document == state)
                        state.Detail.Text = p.PassagesDone < p.PassageCount
                            ? string.Format("Indexing the text: {0}%", 100 * p.PassagesDone / Math.Max(1, p.PassageCount))
                            : string.Format("Indexing pictures: {0} of {1}", p.ImagesDone, p.ImageCount);
                });
                await Task.Run(() => index.Build(embedder, indexed, cancel), cancel);
                UiStallWatchdog.Mark(string.Format("Document indexed in {0:F1}s", watch.Elapsed.TotalSeconds));

                state.Index = index;
                if (_document == state)
                {
                    state.Spinner.IsRunning = state.Spinner.IsVisible = false;
                    state.Detail.Text = DocumentSummary(state, content);
                    if (!_busy)
                        SetStatus(content.PagesSkipped > 0
                            ? string.Format("Only the first {0} pages were read. Ask about them.", DocumentReader.MaxPages)
                            : "Ask about the document.");
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                UiStallWatchdog.Mark("Document failed: " + ex.Message);
                if (_document == state)
                {
                    RemoveDocument();
                    SetStatus(string.Format("Could not read {0}: {1}", state.Name, ex.Message));
                }
                return false;
            }
        }

        private static string DocumentSummary(DocumentState state, DocumentContent content)
        {
            List<string> parts = new List<string>();
            if (content.PageCount > 0)
                parts.Add(content.PageCount == 1 ? "1 page" : content.PageCount + " pages");
            int pictures = content.Images.Count(i => !i.IsPage);
            int pageImages = content.Images.Count(i => i.IsPage);
            if (pictures > 0)
                parts.Add(pictures == 1 ? "1 picture" : pictures + " pictures");
            if (pageImages > 0)
                parts.Add(pageImages == 1 ? "1 page as image" : pageImages + " pages as images");
            if (parts.Count == 0)
                parts.Add(state.Index.Passages.Count == 1 ? "1 passage" : state.Index.Passages.Count + " passages");
            return string.Join(" · ", parts);
        }

        // The embedding model is loaded once (downloaded the first time) and kept while the page is open.
        private Task<EmbeddingGemma2TextVision> EnsureEmbedderAsync(IProgress<string> stage, CancellationToken cancel)
        {
            if (_embedderLoad == null || _embedderLoad.IsFaulted || _embedderLoad.IsCanceled)
                _embedderLoad = LoadEmbedderAsync(stage, cancel);
            return _embedderLoad;
        }

        private async Task<EmbeddingGemma2TextVision> LoadEmbedderAsync(IProgress<string> stage, CancellationToken cancel)
        {
            EmbeddingGemma2TextVision embedder = new EmbeddingGemma2TextVision();
            embedder.OnDownloadProgressChanged += (total, received, percent) =>
                stage?.Report(string.Format("Downloading the search model (388 MB): {0:F0}%", percent));
            stage?.Report(EmbeddingGemma2TextVision.ModelFile.IsLocalFileVerified ? "Loading the search model..." : "Downloading the search model (388 MB)...");
            try
            {
                await embedder.Init(cancellationToken: cancel);
            }
            catch (Exception)
            {
                embedder.Dispose();
                throw;
            }
            _embedder = embedder;
            return embedder;
        }

        private async Task ReleaseEmbedderAsync()
        {
            Task<EmbeddingGemma2TextVision> load = _embedderLoad;
            _embedderLoad = null;
            if (load == null)
                return;
            try
            {
                EmbeddingGemma2TextVision embedder = await load;
                _embedder = null;
                await Task.Run(() => embedder.Dispose());
            }
            catch (Exception)
            {
            }
        }

        private void RemoveDocument()
        {
            DocumentState state = _document;
            if (state == null)
                return;
            _document = null;
            state.Cancel.Cancel();
            _documentBar.Children.Clear();
            _documentBar.IsVisible = false;
            ResetDocumentContext();
        }

        private void ResetDocumentContext()
        {
            _givenPassages.Clear();
            _givenImages.Clear();
        }

        // ---------- Asking ----------

        // The excerpts (and picture) to send with a message, or null without a document. Waits for the document to be
        // indexed.
        private async Task<DocumentContext> GetDocumentContextAsync(string prompt)
        {
            DocumentState state = _document;
            if (state == null)
                return null;
            if (!state.Ready.IsCompleted)
                SetStatus("Waiting for the document to be indexed...");
            if (!await state.Ready || state.Index == null || _embedder == null)
                return null;
            SetStatus("Searching the document...");
            DocumentIndex index = state.Index;
            EmbeddingModel embedder = _embedder;
            DocumentSearchResult result = await Task.Run(() => index.Search(embedder, prompt));

            DocumentContext context = new DocumentContext();
            // About 400 tokens of excerpts with a 4k window, 800 with 8k.
            int budget = ContextWindow >= 8192 ? 3200 : 1600;
            Match pageMatch = PageReference.Match(prompt);
            string askedPage = pageMatch.Success ? "page " + pageMatch.Groups[1].Value : null;

            List<DocumentPassage> candidates;
            if (index.TextLength <= budget || OverviewQuestion.IsMatch(prompt))
            {
                // A short document whole, or the beginning of a long one for a summary.
                candidates = index.Passages.ToList();
            }
            else
            {
                // The best matches, and any close to the best; passages of a page the message names first.
                float best = result.Passages.Count > 0 ? result.Passages[0].Score : 0;
                candidates = result.Passages
                    .Where((m, i) => i == 0 || m.Score >= best - 0.1f || m.Item.Location == askedPage)
                    .OrderBy(m => m.Item.Location == askedPage ? 0 : 1)
                    .Select(m => m.Item)
                    .ToList();
            }
            List<DocumentPassage> referenced = new List<DocumentPassage>();
            int length = 0;
            foreach (DocumentPassage passage in candidates)
            {
                if (length > 0 && length + passage.Text.Length > budget)
                    break;
                length += passage.Text.Length;
                referenced.Add(passage);
                if (!_givenPassages.Contains(passage.Index))
                    context.Passages.Add(passage);
            }

            // A picture or page image: the one on a page the message names, or with the caption of a figure it names;
            // for a question about a picture, one on the page of the best-matching text; else the best match if it
            // matches well - always for a document that is all images (scans).
            if (SelectedOption.AcceptsImages && result.Images.Count > 0)
            {
                DocumentMatch<DocumentImage> image = askedPage != null
                    ? result.Images.Where(m => m.Item.Location == askedPage).OrderBy(m => m.Item.IsPage ? 0 : 1).FirstOrDefault()
                    : null;
                Match figure = FigureReference.Match(prompt);
                if (image == null && figure.Success)
                {
                    string word = figure.Groups[1].Value.ToLowerInvariant().StartsWith("fig") ? "fig(?:ure)?" : Regex.Escape(figure.Groups[1].Value);
                    Regex caption = new Regex(@"\b" + word + @"\.?\s*" + figure.Groups[2].Value + @"\b", RegexOptions.IgnoreCase);
                    HashSet<string> pages = new HashSet<string>(index.Passages.Where(p => p.Location != null && caption.IsMatch(p.Text)).Select(p => p.Location));
                    image = result.Images.FirstOrDefault(m => !m.Item.IsPage && pages.Contains(m.Item.Location))
                        ?? result.Images.FirstOrDefault(m => pages.Contains(m.Item.Location));
                }
                if (image == null && PictureQuestion.IsMatch(prompt) && result.Passages.Count > 0 && result.Passages[0].Item.Location != null)
                    image = result.Images.FirstOrDefault(m => !m.Item.IsPage && m.Item.Location == result.Passages[0].Item.Location);
                if (image == null)
                {
                    DocumentMatch<DocumentImage> best = result.Images[0];
                    bool asked = PictureQuestion.IsMatch(prompt);
                    float bestText = result.Passages.Count > 0 ? result.Passages[0].Score : 0;
                    if (index.Passages.Count == 0
                        || (best.Score >= (asked ? AskedImageMatchScore : ImageMatchScore)
                            && best.Score >= bestText - (asked ? AskedImageMargin : ImageMargin)))
                        image = best;
                }
                UiStallWatchdog.Mark(string.Format("Document search: best passage {0:F3}, best image {1} {2:F3}",
                    result.Passages.Count > 0 ? result.Passages[0].Score : 0, result.Images[0].Item.Location, result.Images[0].Score));
                if (image != null && !_givenImages.Contains(image.Item.Index))
                {
                    context.ImageItem = image.Item;
                    byte[] jpeg = image.Item.Data;
                    context.Image = new PendingAttachment
                    {
                        Attachment = ChatAttachment.Image(jpeg),
                        Image = jpeg,
                        Data = jpeg,
                        MediaType = "image/jpeg",
                        Name = state.Name + ", " + image.Item.Location
                    };
                }
            }

            context.Text = DocumentPrompt(state, context);
            context.Step = DocumentStep(state, referenced, context.ImageItem);
            SetStatus(null);
            return context;
        }

        // What the model is given before the message.
        private static string DocumentPrompt(DocumentState state, DocumentContext context)
        {
            if (context.Passages.Count == 0 && context.ImageItem == null)
                return null;
            StringBuilder text = new StringBuilder();
            text.AppendFormat("I attached the document \"{0}\"", state.Name);
            if (state.PageCount > 0)
                text.AppendFormat(" ({0} pages)", state.PageCount);
            text.Append(". ");
            if (context.Passages.Count > 0)
            {
                text.Append("Excerpts of it that may help:\n");
                // In document order, each with its page.
                foreach (DocumentPassage passage in context.Passages.OrderBy(p => p.Index))
                {
                    text.Append('\n');
                    if (passage.Location != null)
                        text.AppendFormat("[{0}]\n", passage.Location);
                    text.Append(passage.Text).Append('\n');
                }
                text.Append('\n');
            }
            if (context.ImageItem != null)
                text.AppendFormat("The attached image is {0} of the document.\n",
                    context.ImageItem.IsPage ? "an image of " + context.ImageItem.Location
                    : context.ImageItem.Location != null && context.ImageItem.Location.StartsWith("page ") ? "a picture on " + context.ImageItem.Location
                    : context.ImageItem.Location ?? "a picture");
            text.Append(state.PageCount > 0
                ? "Answer from the document where it helps and say which page the answer is on.\n\nMy message: "
                : "Answer from the document where it helps.\n\nMy message: ");
            return text.ToString();
        }

        // The grey line above the reply: which pages (or how many excerpts) were used, and the picture.
        private static string DocumentStep(DocumentState state, List<DocumentPassage> passages, DocumentImage image)
        {
            List<string> pages = passages.Select(p => p.Location).Where(l => l != null).Distinct()
                .OrderBy(l => int.TryParse(l.Replace("page ", ""), out int n) ? n : 0).ToList();
            string what;
            if (pages.Count > 0)
                what = (pages.Count == 1 ? "page " : "pages ") + JoinList(pages.Select(p => p.Replace("page ", "")).ToList());
            else if (passages.Count > 0)
                what = passages.Count == 1 ? "1 excerpt" : passages.Count + " excerpts";
            else
                what = null;
            if (image != null)
            {
                string imageText = image.IsPage ? "the image of " + image.Location
                    : image.Location != null && image.Location.StartsWith("page ") ? "the picture on " + image.Location
                    : image.Location ?? "a picture";
                what = what == null ? imageText : what + " and " + imageText;
            }
            return what == null ? null : DocumentStepPrefix + state.Name + ": " + what;
        }

        private static string JoinList(List<string> items)
        {
            if (items.Count <= 1)
                return string.Join("", items);
            return string.Join(", ", items.Take(items.Count - 1)) + " and " + items[items.Count - 1];
        }

        // After a reply: what was given is now in the model's conversation.
        private void MarkDocumentContextGiven(DocumentContext context)
        {
            if (context == null)
                return;
            foreach (DocumentPassage passage in context.Passages)
                _givenPassages.Add(passage.Index);
            if (context.ImageItem != null)
                _givenImages.Add(context.ImageItem.Index);
        }
    }
}

#endif
