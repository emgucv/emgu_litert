//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls.Shapes;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.LM.Chat;

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
            // The SHA-256 of the file: its saved index in the chat database (DocumentRecord).
            public string Id;
            public bool IsPrivate;
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

        private HorizontalStackLayout _documentBar;
        private DocumentState _document;
        // What the model's conversation already holds: passages and images given with earlier messages since the
        // conversation was last compacted.
        private readonly HashSet<int> _givenPassages = new HashSet<int>();
        private readonly HashSet<int> _givenImages = new HashSet<int>();

        // ---------- Attaching ----------

        private void AttachDocument(byte[] bytes, string name)
        {
            if (SearchDownloadRequired) { SetStatus("The search model was deleted. Download it again in Settings > Storage before attaching documents."); return; }
            UiStallWatchdog.Mark(string.Format("Attach document {0}: {1} bytes", name, bytes.Length));
            ShowDocument(name, state => IndexDocumentAsync(state, bytes, null));
        }

        // A saved chat's document, from its saved index (no file needed).
        private async Task RestoreDocumentAsync(string documentId)
        {
            if (SearchDownloadRequired) return;
            DocumentRecord saved = await _store.GetDocumentAsync(documentId);
            if (saved == null)
                return;
            UiStallWatchdog.Mark("Restore document " + saved.Name);
            ShowDocument(saved.Name, state => IndexDocumentAsync(state, null, documentId));
        }

        private void ShowDocument(string name, Func<DocumentState, Task<bool>> index)
        {
            RemoveDocument();
            DocumentState state = new DocumentState { Name = name, IsPrivate = IsPrivateChat };
            BuildDocumentChip(state);
            _document = state;
            _documentBar.Children.Add(state.Chip);
            _documentBar.IsVisible = true;
            state.Ready = index(state);
            if (state.IsPrivate) _privateDocumentTasks.Add(state.Ready);
        }

        // What an index is built with: another value rebuilds a newly attached file's index (a saved chat's document
        // keeps the one it was given).
        private string IndexSettings(bool images)
        {
            DocumentIndex defaults = new DocumentIndex();
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "v1|{0}|passages {1}/{2}|images {3}|pages {4}",
                SelectedEmbeddingFile, defaults.MaxPassageLength, defaults.PassageOverlap,
                images ? MaxDocumentImages : 0, DocumentReader.MaxPages);
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
        // Read the document, load the embedding model (downloading it the first time) and index the document - all in
        // the background, with progress in the chip - or load its saved index: the same file indexed with the same
        // settings before, or (savedId) a saved chat's document. Returns false if it failed or was cancelled.
        private async Task<bool> IndexDocumentAsync(DocumentState state, byte[] bytes, string savedId)
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
                int maxImages = SelectedOption.AcceptsImages && EmbeddingSupportsImages ? MaxDocumentImages : 0;
                string settings = IndexSettings(maxImages > 0);
                state.Id = savedId ?? await Task.Run(() => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), cancel);
                DocumentRecord saved = state.IsPrivate ? null : await _store.GetDocumentAsync(state.Id);
                DocumentIndex index;
                string summary;
                if (saved != null && (savedId != null || saved.Settings == settings))
                {
                    ((IProgress<string>)stage).Report("Loading the saved index...");
                    List<DocumentItemRecord> items = await _store.GetDocumentItemsAsync(state.Id);
                    bool rebuild = saved.Settings?.Split('|').ElementAtOrDefault(1) != SelectedEmbeddingFile;
                    index = new DocumentIndex();
                    foreach (DocumentItemRecord item in items)
                    {
                        if (item.IsImage && EmbeddingSupportsImages)
                            index.AddImage(item.Data, item.MediaType, item.Location, item.IsPage, rebuild ? null : ToFloats(item.Embedding));
                        else if (item.IsImage)
                            continue;
                        else
                            index.AddPassage(item.Text, item.Location, rebuild ? null : ToFloats(item.Embedding));
                    }
                    state.PageCount = saved.PageCount;
                    summary = saved.Summary;
                    if (!EmbeddingSupportsImages && items.Any(i => i.IsImage))
                        summary = index.Passages.Count + " passages · text-only embedding model";
                    if (index.Passages.Count == 0 && index.Images.Count == 0)
                        throw new InvalidOperationException("this document needs an embedding model that supports images");
                    // The model embeds the questions.
                    EmbeddingModel savedEmbedder = await Embedder.EnsureAsync(stage, cancel);
                    if (rebuild)
                    {
                        ((IProgress<string>)stage).Report("Rebuilding the document embeddings...");
                        await BuildDocumentIndexAsync(index, savedEmbedder, state, null, cancel);
                        // Keep saved pictures for a later switch back to a vision model.
                        if (EmbeddingSupportsImages || !items.Any(i => i.IsImage))
                            await SaveDocumentIndexAsync(state, index, settings, summary);
                    }
                    UiStallWatchdog.Mark(string.Format("Document index loaded in {0:F1}s: {1} passages, {2} images",
                        watch.Elapsed.TotalSeconds, index.Passages.Count, index.Images.Count));
                }
                else
                {
                    if (bytes == null)
                        throw new InvalidOperationException("its saved index is missing");
                    await WaitForWorkAsync(false, reason => ((IProgress<string>)stage).Report(reason == null
                        ? "Reading the document..." : "Document indexing paused: " + reason), cancel);
                    DocumentContent content = await DocumentReader.ReadAsync(bytes, state.Name, maxImages, stage, cancel);
                    state.PageCount = content.PageCount;
                    index = new DocumentIndex();
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

                    EmbeddingModel embedder = await Embedder.EnsureAsync(stage, cancel);
                    watch.Restart();
                    Progress<DocumentIndexProgress> indexed = new Progress<DocumentIndexProgress>(p =>
                    {
                        if (_document == state)
                            state.Detail.Text = p.PassagesDone < p.PassageCount
                                ? string.Format("Indexing the text: {0}%", 100 * p.PassagesDone / Math.Max(1, p.PassageCount))
                                : string.Format("Indexing pictures: {0} of {1}", p.ImagesDone, p.ImageCount);
                    });
                    await BuildDocumentIndexAsync(index, embedder, state, indexed, cancel);
                    UiStallWatchdog.Mark(string.Format("Document indexed in {0:F1}s", watch.Elapsed.TotalSeconds));
                    summary = DocumentSummary(state, content, index);
                    if (content.PagesSkipped > 0)
                        summary += string.Format(" (first {0} read)", DocumentReader.MaxPages);
                    await SaveDocumentIndexAsync(state, index, settings, summary);
                }

                state.Index = index;
                if (_document == state)
                {
                    state.Spinner.IsRunning = state.Spinner.IsVisible = false;
                    state.Detail.Text = summary;
                    if (savedId != null)
                        MarkGivenFromHistory(index);
                    else if (!_busy)
                        SetStatus("Ask about the document.");
                    // A chat already saved now uses this document.
                    if (!state.IsPrivate && !IsPrivateChat && _conversationId != null)
                        await _store.SetConversationDocumentAsync(_conversationId.Value, state.Id);
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

        private Task BuildDocumentIndexAsync(DocumentIndex index, EmbeddingModel model, DocumentState state,
            IProgress<DocumentIndexProgress> progress, CancellationToken cancel)
        {
            Action<string> paused = reason => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_document == state) state.Detail.Text = reason == null
                    ? "Indexing the document..." : "Document indexing paused: " + reason;
            });
            return index.BuildAsync(
                (texts, token) => RunIndexEmbeddingAsync(() => model.EmbedDocuments(texts), paused, token),
                (image, token) => RunIndexEmbeddingAsync(() => model.EmbedImage(image), paused, token), progress, cancel);
        }

        // Save the index in the chat database, so the same file - or a chat that used it - needn't be indexed again.
        private async Task SaveDocumentIndexAsync(DocumentState state, DocumentIndex index, string settings, string summary)
        {
            if (state.IsPrivate || IsPrivateChat) return;
            List<DocumentItemRecord> items = new List<DocumentItemRecord>();
            foreach (DocumentPassage passage in index.Passages)
                items.Add(new DocumentItemRecord
                {
                    DocumentId = state.Id,
                    Position = passage.Index,
                    Text = passage.Text,
                    Location = passage.Location,
                    Embedding = ToBytes(passage.Embedding)
                });
            foreach (DocumentImage image in index.Images)
                items.Add(new DocumentItemRecord
                {
                    DocumentId = state.Id,
                    Position = image.Index,
                    IsImage = true,
                    Location = image.Location,
                    IsPage = image.IsPage,
                    MediaType = image.MediaType,
                    Data = image.Data,
                    Embedding = ToBytes(image.Embedding)
                });
            try
            {
                await _store.SaveDocumentAsync(new DocumentRecord
                {
                    Id = state.Id,
                    Name = state.Name,
                    PageCount = state.PageCount,
                    Settings = settings,
                    Summary = summary,
                    Created = DateTime.UtcNow
                }, items);
            }
            catch (Exception ex)
            {
                // Still usable, just not saved.
                UiStallWatchdog.Mark("Saving the document index failed: " + ex.Message);
            }
        }

        // A reopened chat already gave the model the excerpts in its messages: don't send them again.
        private void MarkGivenFromHistory(DocumentIndex index)
        {
            List<string> contexts = _conversation.Messages.Skip(_conversation.ContextStart)
                .Where(m => m.Role == ChatMessageRole.User).Select(m => m.Context).Where(c => !string.IsNullOrEmpty(c)).ToList();
            if (contexts.Count == 0)
                return;
            foreach (DocumentPassage passage in index.Passages)
                if (contexts.Any(c => c.Contains(passage.Text)))
                    _givenPassages.Add(passage.Index);
        }

        private static byte[] ToBytes(float[] values)
        {
            if (values == null)
                return null;
            byte[] bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static float[] ToFloats(byte[] bytes)
        {
            if (bytes == null)
                return null;
            float[] values = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, values, 0, values.Length * sizeof(float));
            return values;
        }

        private static string DocumentSummary(DocumentState state, DocumentContent content, DocumentIndex index)
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
                parts.Add(index.Passages.Count == 1 ? "1 passage" : index.Passages.Count + " passages");
            return string.Join(" · ", parts);
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

        // search_documents: the best passages from every saved document index, with their document and page. Documents
        // attached to a chat stay as they are (their excerpts go with each message); this lets the model look in the others.
        private async Task<string> SearchDocumentsAsync(string request, string documentName, CancellationToken cancel)
        {
            if (IsPrivateChat) return "Document search is unavailable in private chats.";
            if (string.IsNullOrWhiteSpace(request))
                return "Provide what to find in the documents.";
            if (SearchDownloadRequired)
                return "The search model was deleted, so saved documents can't be searched. The user can download it again in Settings > Storage.";
            var found = await new DocumentLibrarySearch(_store).SearchAsync(SelectedEmbeddingFile, request, documentName,
                async (text, token) =>
                {
                    var embedder = await Embedder.EnsureAsync(null, token);
                    await Embedder.Gate.WaitAsync(token);
                    try { return await Task.Run(() => embedder.EmbedQuery(text), token); }
                    finally { Embedder.Gate.Release(); }
                }, ContextWindow >= 8192 ? 3200 : 1600, cancel);
            if (found.Count == 0)
                return "No relevant passages were found in the user's saved documents" +
                    (string.IsNullOrWhiteSpace(documentName) ? "" : " named \"" + documentName + "\"") +
                    ". Do not invent document content.";
            return DocumentLibrarySearch.Format(found);
        }

        // The excerpts (and picture) to send with a message, or null without a document. Waits for the document to be
        // indexed.
        private async Task<DocumentContext> GetDocumentContextAsync(string prompt)
        {
            DocumentState state = _document;
            if (state == null)
                return null;
            if (!state.Ready.IsCompleted)
                SetStatus("Waiting for the document to be indexed...");
            if (!await state.Ready || state.Index == null || Embedder.Model == null)
                return null;
            SetStatus("Searching the document...");
            DocumentIndex index = state.Index;
            EmbeddingModel embedder = Embedder.Model;
            DocumentSearchResult result;
            await Embedder.Gate.WaitAsync();
            try
            {
                result = await Task.Run(() => index.Search(embedder, prompt));
            }
            finally
            {
                Embedder.Gate.Release();
            }

            DocumentContext context = new DocumentContext();
            // About 400 tokens of excerpts with a 4k window, 800 with 8k.
            int budget = ContextWindow >= 8192 ? 3200 : 1600;
            Match pageMatch = PageReference.Match(prompt);
            string askedPage = pageMatch.Success ? "page " + pageMatch.Groups[1].Value : null;

            // A summary covers the document, not just what this message adds: its passages are sent even if given before
            // (otherwise a summary asked right after other questions only covered the newly attached page image).
            bool overview = OverviewQuestion.IsMatch(prompt);
            List<DocumentPassage> candidates;
            if (index.TextLength <= budget || overview)
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
                if (overview || !_givenPassages.Contains(passage.Index))
                    context.Passages.Add(passage);
            }

            // A picture or page image: the one on a page the message names, or with the caption of a figure it names;
            // for a question about a picture, one on the page of the best-matching text; else the best match if it
            // matches well - always for a document that is all images (scans). Not for a summary of a document with
            // text: given one page's image with "Summarize this document", Gemma 4 E2B summarized just that page.
            if (SelectedOption.AcceptsImages && result.Images.Count > 0 && !(overview && index.Passages.Count > 0))
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

        // The message's excerpts were chosen before compacting the conversation, leaving out passages given earlier - which
        // compacting may just have summarized or dropped (a question about one then got no excerpt and was answered from
        // the summary alone). Search again, now that nothing counts as given; a picture already attached is kept.
        private async Task<DocumentContext> RefreshDocumentContextAsync(string prompt, DocumentContext document,
            ChatMessageItem userItem, List<PendingAttachment> attachments)
        {
            DocumentContext again = await GetDocumentContextAsync(prompt);
            if (again == null)
                return document;
            if (document.ImageItem != null)
            {
                // Already shown with the message and in its attachments.
                again.ImageItem = document.ImageItem;
                again.Image = document.Image;
                again.Text = DocumentPrompt(_document, again);
                again.Step = document.Step;
            }
            else if (again.Image != null)
            {
                attachments.Add(again.Image);
                userItem.AddAttachment(new Microsoft.Extensions.AI.DataContent(again.Image.Data, again.Image.MediaType));
            }
            return again;
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
