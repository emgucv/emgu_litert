//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Text;
using System.Text.RegularExpressions;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Agents.Documents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Models;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Controls.Shapes;

namespace Emgu.LiteRT.LM.Maui.Documents
{
    /// <summary>What a message is sent with from the attached document.</summary>
    public sealed class ChatDocumentContext
    {
        /// <summary>Given to the model before the message (null if nothing new).</summary>
        public string Text { get; set; }

        /// <summary>A picture or page image to attach (null if none).</summary>
        public DataContent Image { get; set; }

        /// <summary>The line shown above the reply, e.g. "Looked in report.pdf: pages 2 and 5"; or null.</summary>
        public string Step { get; set; }

        /// <summary>The passages given.</summary>
        public List<DocumentPassage> Passages { get; } = new List<DocumentPassage>();

        /// <summary>The image given, or null.</summary>
        public DocumentImage ImageItem { get; set; }
    }

    /// <summary>
    /// Questions about a document: <see cref="AttachDocument"/> attaches a PDF, Word or text file to the chat (an app's
    /// file picker calls it). Its text is split into passages and indexed with the shared search model
    /// (<see cref="SearchServices"/>) together with its pictures and images of its pages without text (scans, charts
    /// drawn as shapes) - the latter only for models that take images. Each message is then sent with the passages most
    /// related to it (the model's context can't hold a whole document) and, for models that take images, the best-matching
    /// picture or page when it matches well (<see cref="ChatFeature.PrepareMessageAsync"/>); after compacting, the
    /// excerpts are searched again. The search_documents tool looks in every saved document index. The chip above the
    /// message box shows the document and its indexing. The index is kept in the chats' store
    /// (<see cref="IChatDocumentStore"/>), with the document each chat uses, restored when the chat is opened; a private
    /// chat's document is indexed in memory only.
    /// </summary>
    public sealed class DocumentsFeature : ChatFeature, IStorageParticipant
    {
        private sealed class DocumentState
        {
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

        private readonly IChatDocumentStore _store;
        private readonly SearchServices _search;
        private readonly HorizontalStackLayout _documentBar = new HorizontalStackLayout { Spacing = 8, IsVisible = false };
        private DocumentState _document;
        // What the model's conversation already holds: passages and images given with earlier messages since the
        // conversation was last compacted.
        private readonly HashSet<int> _givenPassages = new HashSet<int>();
        private readonly HashSet<int> _givenImages = new HashSet<int>();
        // The indexing of a private chat's documents, finished before the chat is left (nothing of them is saved).
        private readonly List<Task> _privateDocumentTasks = new List<Task>();
        // The excerpts given with the message being sent, marked as given when the reply is done.
        private ChatDocumentContext _pending;
        private int _pendingCompaction;
        private bool _cleaned;

        /// <summary>Create the feature.</summary>
        /// <param name="store">The chats' store (e.g. Emgu.LiteRT.LM.Chat.Sqlite's ChatHistoryStore)</param>
        /// <param name="search">The search model and when indexing may run</param>
        public DocumentsFeature(IChatDocumentStore store, SearchServices search)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _search = search ?? throw new ArgumentNullException(nameof(search));
        }

        /// <summary>At most this many pictures and page images are indexed (each takes seconds on a phone's CPU); 24 on a desktop, 10 elsewhere.</summary>
        public int MaxDocumentImages { get; set; } = DeviceInfo.Idiom == DeviceIdiom.Desktop ? 24 : 10;

        /// <inheritdoc/>
        public override string Name => ChatDocuments.GroupName;

        /// <inheritdoc/>
        public override string Title => "Documents";

        /// <summary>Always on (nothing to switch).</summary>
        public override bool PersistEnabled => false;

        /// <inheritdoc/>
        public override string ToolSummary => "saved documents";

        /// <summary>Whether a document is attached.</summary>
        public bool HasDocument => _document != null;

        /// <summary>The attached document's id in the store (null without one, or while it is read).</summary>
        public string DocumentId => _document?.Id;

        /// <summary>The attached document's indexing: true when it can be asked about; null without a document.</summary>
        public Task<bool> DocumentReady => _document?.Ready;

        /// <summary>The line shown above the reply for the message being sent ("Looked in report.pdf: pages 2 and 5"), or null.</summary>
        public string PendingStep => _pending?.Step;

        private SearchEmbedder Embedder => _search.Embedder;

        /// <inheritdoc/>
        public override void RegisterServices(ChatFeatureServices services)
        {
            services.Add(this);
            services.Add<IStorageParticipant>(this);
        }

        // ---------- IStorageParticipant ----------

        // Before storage deletes something: a document being read is let go of (it may share the search model's load
        // with the searches, so it is cancelled before any of them is waited for), as is the document itself or its
        // search model.
        /// <inheritdoc/>
        public async Task PrepareStorageDeleteAsync(StorageEntry entry)
        {
            Task<bool> ready = DocumentReady;
            bool pending = ready != null && !ready.IsCompleted;
            bool gone = (entry.Kind == StorageKind.SearchModel && entry.Key == _search.IndexModel?.Invoke())
                || (entry.Kind == StorageKind.Document && entry.Key == DocumentId);
            if (pending || gone)
                RemoveDocument();
            if (pending)
            {
                try { await ready; }
                catch (OperationCanceledException) { }
            }
        }

        /// <inheritdoc/>
        public void StorageChanged(StorageEntry entry)
        {
        }

        /// <inheritdoc/>
        public override View BuildComposerView() => _documentBar;

        /// <inheritdoc/>
        public override IEnumerable<ToolGroup> ToolGroups()
        {
            yield return new ToolGroup(ChatDocuments.GroupName, ChatDocuments.GroupSummary, ChatDocuments.GroupKeywords,
                () => Host.IsPrivate ? Array.Empty<AITool>() : new AITool[] { AgentTools.SearchDocuments(SearchDocumentsAsync) },
                () => ChatDocuments.Instructions);
        }

        // The page shown the first time: saved document indexes no chat uses (attached, then the chat was never saved) go.
        /// <inheritdoc/>
        public override void OnAppearing()
        {
            if (_cleaned)
                return;
            _cleaned = true;
            _ = _store.DeleteUnusedDocumentsAsync();
        }

        // A new chat (or another model) starts without the document; an opened chat gets its own back (OnChatOpened).
        /// <inheritdoc/>
        public override void OnNewConversation() => RemoveDocument();

        /// <inheritdoc/>
        public override async void OnChatOpened(int chatId)
        {
            try
            {
                string documentId = await _store.GetChatDocumentAsync(chatId);
                if (!string.IsNullOrEmpty(documentId))
                    await RestoreDocumentAsync(documentId);
            }
            catch (Exception)
            {
            }
        }

        // A chat saved for the first time (or another one) now uses the document.
        /// <inheritdoc/>
        public override async void OnChatSaved(int chatId)
        {
            try
            {
                DocumentState state = _document;
                if (state == null || state.IsPrivate || Host.IsPrivate || state.Ready?.IsCompletedSuccessfully != true || state.Id == null)
                    return;
                await _store.SetChatDocumentAsync(chatId, state.Id);
            }
            catch (Exception)
            {
            }
        }

        /// <inheritdoc/>
        public override void OnDetached()
        {
            RemoveDocument();
            base.OnDetached();
        }

        // ---------- Attaching ----------

        /// <summary>Attach a document to the chat: it is read and indexed in the background, with progress in the chip.</summary>
        /// <param name="bytes">The file</param>
        /// <param name="name">Its name, with the extension (.pdf, .docx, .txt, .md)</param>
        public void AttachDocument(byte[] bytes, string name)
        {
            if (Host == null)
                return;
            if (_search.IsDownloadRequired)
            {
                Host.SetStatus("The search model was deleted. Download it again in the settings before attaching documents.");
                return;
            }
            ShowDocument(name, state => IndexDocumentAsync(state, bytes, null));
        }

        /// <summary>Attach a saved chat's document again, from its saved index (no file needed).</summary>
        /// <param name="documentId">The document</param>
        public async Task RestoreDocumentAsync(string documentId)
        {
            if (Host == null || _search.IsDownloadRequired)
                return;
            ChatDocument saved = await _store.GetDocumentAsync(documentId);
            if (saved == null)
                return;
            ShowDocument(saved.Name, state => IndexDocumentAsync(state, null, documentId));
        }

        private void ShowDocument(string name, Func<DocumentState, Task<bool>> index)
        {
            RemoveDocument();
            DocumentState state = new DocumentState { Name = name, IsPrivate = Host.IsPrivate };
            BuildDocumentChip(state);
            _document = state;
            _documentBar.Children.Add(state.Chip);
            _documentBar.IsVisible = true;
            state.Ready = index(state);
            if (state.IsPrivate)
                _privateDocumentTasks.Add(state.Ready);
        }

        // What an index is built with: another value rebuilds a newly attached file's index (a saved chat's document
        // keeps the one it was given).
        private string IndexSettings(bool images)
        {
            DocumentIndex defaults = new DocumentIndex();
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "v1|{0}|passages {1}/{2}|images {3}|pages {4}",
                _search.IndexModel?.Invoke(), defaults.MaxPassageLength, defaults.PassageOverlap,
                images ? MaxDocumentImages : 0, DocumentReader.DefaultMaxPages);
        }

        private void BuildDocumentChip(DocumentState state)
        {
            state.Detail = new Label { Text = "Reading...", LineBreakMode = LineBreakMode.TailTruncation };
            ChatResources.ApplyStyle(state.Detail, "ChatListDetailStyle");
            state.Spinner = new ActivityIndicator { IsRunning = true, Color = ChatResources.FindColor("ChatAccent"), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center };
            Label remove = ChatPage.Link("✕", () =>
            {
                if (!Host.IsBusy && _document == state)
                {
                    RemoveDocument();
                    Host.SetStatus("The document was removed. Earlier answers keep what they used of it.");
                }
            });
            remove.VerticalOptions = LayoutOptions.Center;
            SemanticProperties.SetDescription(remove, "Remove the document");
            var title = new Label { Text = state.Name, LineBreakMode = LineBreakMode.MiddleTruncation };
            ChatResources.ApplyStyle(title, "ChatListTitleStyle");
            var icon = new Label { Text = "\U0001F4C4", FontSize = 20, VerticalOptions = LayoutOptions.Center };
            state.Chip = new Border
            {
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                Padding = new Thickness(8, 6),
                Content = new HorizontalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        icon,
                        new VerticalStackLayout
                        {
                            Spacing = 0,
                            VerticalOptions = LayoutOptions.Center,
                            MaximumWidthRequest = 240,
                            Children = { title, state.Detail }
                        },
                        state.Spinner,
                        remove
                    }
                }
            };
            ChatResources.SetThemeColor(state.Chip, VisualElement.BackgroundColorProperty, "ChatRowBorder");
            SemanticProperties.SetDescription(state.Chip, "The attached document " + state.Name);
        }

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
                // Pictures only for models that take images; a model chosen later gets the text only.
                bool supportsImages = _search.SupportsImages;
                int maxImages = Host.Model?.AcceptsImages == true && supportsImages ? MaxDocumentImages : 0;
                string settings = IndexSettings(maxImages > 0);
                state.Id = savedId ?? await Task.Run(() => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), cancel);
                ChatDocument saved = state.IsPrivate ? null : await _store.GetDocumentAsync(state.Id);
                DocumentIndex index;
                string summary;
                if (saved != null && (savedId != null || saved.Settings == settings))
                {
                    ((IProgress<string>)stage).Report("Loading the saved index...");
                    IReadOnlyList<ChatDocumentItem> items = await _store.GetDocumentItemsAsync(state.Id);
                    bool rebuild = saved.Settings?.Split('|').ElementAtOrDefault(1) != _search.IndexModel?.Invoke();
                    index = new DocumentIndex();
                    foreach (ChatDocumentItem item in items)
                    {
                        if (item.IsImage && supportsImages)
                            index.AddImage(item.Data, item.MediaType, item.Location, item.IsPage, rebuild ? null : ToFloats(item.Embedding));
                        else if (item.IsImage)
                            continue;
                        else
                            index.AddPassage(item.Text, item.Location, rebuild ? null : ToFloats(item.Embedding));
                    }
                    state.PageCount = saved.PageCount;
                    summary = saved.Summary;
                    if (!supportsImages && items.Any(i => i.IsImage))
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
                        if (supportsImages || !items.Any(i => i.IsImage))
                            await SaveDocumentIndexAsync(state, index, settings, summary);
                    }
                }
                else
                {
                    if (bytes == null)
                        throw new InvalidOperationException("its saved index is missing");
                    await _search.Workload.WaitAsync(reason => ((IProgress<string>)stage).Report(reason == null
                        ? "Reading the document..." : "Document indexing paused: " + reason), cancel);
                    DocumentContent content = await DocumentReader.ReadAsync(bytes, state.Name, new DocumentReadOptions
                    {
                        MaxImages = maxImages,
                        EncodeImage = EncodeDocumentImage,
                        OpenPdf = pdf => new PdfPageRenderer(pdf)
                    }, stage, cancel);
                    state.PageCount = content.PageCount;
                    index = new DocumentIndex();
                    foreach (DocumentText text in content.Texts)
                        index.AddText(text.Text, text.Location);
                    foreach (DocumentPicture image in content.Images)
                        index.AddImage(image.Jpeg, "image/jpeg", image.Location, image.IsPage);
                    if (index.Passages.Count == 0 && index.Images.Count == 0)
                        throw new InvalidOperationException(content.PageCount > 0 && maxImages == 0
                            ? "it has no text (scanned pages need a model that understands images, e.g. Gemma 4)"
                            : "no text was found in it");

                    EmbeddingModel embedder = await Embedder.EnsureAsync(stage, cancel);
                    Progress<DocumentIndexProgress> indexed = new Progress<DocumentIndexProgress>(p =>
                    {
                        if (_document == state)
                            state.Detail.Text = p.PassagesDone < p.PassageCount
                                ? string.Format("Indexing the text: {0}%", 100 * p.PassagesDone / Math.Max(1, p.PassageCount))
                                : string.Format("Indexing pictures: {0} of {1}", p.ImagesDone, p.ImageCount);
                    });
                    await BuildDocumentIndexAsync(index, embedder, state, indexed, cancel);
                    summary = DocumentSummary(content, index);
                    if (content.PagesSkipped > 0)
                        summary += string.Format(" (first {0} read)", DocumentReader.DefaultMaxPages);
                    await SaveDocumentIndexAsync(state, index, settings, summary);
                }

                state.Index = index;
                if (_document == state)
                {
                    state.Spinner.IsRunning = state.Spinner.IsVisible = false;
                    state.Detail.Text = summary;
                    if (savedId != null)
                        MarkGivenFromHistory(index);
                    else if (!Host.IsBusy)
                        Host.SetStatus("Ask about the document.");
                    // A chat already saved now uses this document.
                    if (!state.IsPrivate && !Host.IsPrivate && Host.CurrentChatId != null)
                        await _store.SetChatDocumentAsync(Host.CurrentChatId.Value, state.Id);
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                if (_document == state)
                {
                    RemoveDocument();
                    Host.SetStatus(string.Format("Could not read {0}: {1}", state.Name, ex.Message));
                }
                return false;
            }
        }

        private Task BuildDocumentIndexAsync(DocumentIndex index, EmbeddingModel model, DocumentState state,
            IProgress<DocumentIndexProgress> progress, CancellationToken cancel)
        {
            Action<string> paused = reason => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_document == state)
                    state.Detail.Text = reason == null ? "Indexing the document..." : "Document indexing paused: " + reason;
            });
            return index.BuildAsync(
                (texts, token) => _search.Workload.RunEmbeddingAsync(Embedder, () => model.EmbedDocuments(texts), paused, token),
                (image, token) => _search.Workload.RunEmbeddingAsync(Embedder, () => model.EmbedImage(image), paused, token), progress, cancel);
        }

        // Save the index in the chats' store, so the same file - or a chat that used it - needn't be indexed again.
        private async Task SaveDocumentIndexAsync(DocumentState state, DocumentIndex index, string settings, string summary)
        {
            if (state.IsPrivate || Host.IsPrivate)
                return;
            var items = new List<ChatDocumentItem>();
            foreach (DocumentPassage passage in index.Passages)
                items.Add(new ChatDocumentItem
                {
                    DocumentId = state.Id,
                    Position = passage.Index,
                    Text = passage.Text,
                    Location = passage.Location,
                    Embedding = ToBytes(passage.Embedding)
                });
            foreach (DocumentImage image in index.Images)
                items.Add(new ChatDocumentItem
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
                await _store.SaveDocumentAsync(new ChatDocument
                {
                    Id = state.Id,
                    Name = state.Name,
                    PageCount = state.PageCount,
                    Settings = settings,
                    Summary = summary,
                    Created = DateTime.UtcNow
                }, items);
            }
            catch (Exception)
            {
                // Still usable, just not saved.
            }
        }

        // A reopened chat already gave the model the excerpts in its messages: don't send them again.
        private void MarkGivenFromHistory(DocumentIndex index)
        {
            ChatConversation conversation = Host.Conversation;
            if (conversation == null)
                return;
            List<string> contexts = conversation.Messages.Skip(conversation.ContextStart)
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

        private static string DocumentSummary(DocumentContent content, DocumentIndex index)
        {
            var parts = new List<string>();
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

        /// <summary>Remove the attached document (its indexing is cancelled); earlier answers keep what they used of it.</summary>
        public void RemoveDocument()
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

        /// <summary>Nothing of the document counts as given to the model (a new conversation).</summary>
        public void ResetDocumentContext()
        {
            _givenPassages.Clear();
            _givenImages.Clear();
        }

        /// <summary>Wait for a private chat's documents to finish indexing before the chat is left (their tasks end without saving).</summary>
        public async Task FinishPrivateDocumentsAsync()
        {
            foreach (Task task in _privateDocumentTasks.ToArray())
                try { await task; } catch (OperationCanceledException) { }
            _privateDocumentTasks.Clear();
        }

        /// <summary>Forget the private documents whose indexing has ended.</summary>
        public void ForgetFinishedPrivateDocuments() => _privateDocumentTasks.RemoveAll(t => t.IsCompleted);

        // ---------- Asking ----------

        // search_documents: the best passages from every saved document index, with their document and page. Documents
        // attached to a chat stay as they are (their excerpts go with each message); this lets the model look in the others.
        private async Task<string> SearchDocumentsAsync(string request, string documentName, CancellationToken cancel)
        {
            if (Host.IsPrivate)
                return "Document search is unavailable in private chats.";
            if (string.IsNullOrWhiteSpace(request))
                return "Provide what to find in the documents.";
            if (_search.IsDownloadRequired)
                return "The search model was deleted, so saved documents can't be searched. The user can download it again in the settings.";
            // The saved passages of documents indexed with the chosen embedding model; the request is embedded only if there are any.
            IReadOnlyList<ChatDocumentPassage> saved = await _store.GetDocumentPassagesAsync(_search.IndexModel?.Invoke());
            IList<DocumentLibraryPassage> passages = DocumentLibrarySearch.Named(saved.Select(p => new DocumentLibraryPassage
            {
                DocumentId = p.DocumentId,
                DocumentName = p.DocumentName,
                Location = p.Location,
                Text = p.Text,
                Embedding = ToFloats(p.Embedding)
            }), documentName);
            cancel.ThrowIfCancellationRequested();
            var found = new List<DocumentLibraryMatch>();
            if (passages.Count > 0)
            {
                EmbeddingModel embedder = await Embedder.EnsureAsync(null, cancel);
                float[] query;
                await Embedder.Gate.WaitAsync(cancel);
                try { query = await Task.Run(() => embedder.EmbedQuery(request), cancel); }
                finally { Embedder.Gate.Release(); }
                found = DocumentLibrarySearch.Rank(passages, query, Host.ContextWindow >= 8192 ? 3200 : 1600);
            }
            if (found.Count == 0)
                return "No relevant passages were found in the user's saved documents" +
                    (string.IsNullOrWhiteSpace(documentName) ? "" : " named \"" + documentName + "\"") +
                    ". Do not invent document content.";
            return DocumentLibrarySearch.Format(found);
        }

        // A document's picture or page image for the index (DocumentReadOptions.EncodeImage): decoded by the platform,
        // scaled down and re-encoded as JPEG; null if it can't be decoded.
        private static byte[] EncodeDocumentImage(byte[] image, int maxSize)
        {
            DataContent prepared = ChatImages.PrepareAsync(image, maxSize).GetAwaiter().GetResult();
            return prepared != null && prepared.MediaType == "image/jpeg" ? prepared.Data.ToArray() : null;
        }

        /// <summary>The excerpts (and picture) to send with a message, or null without a document. Waits for the document to be indexed.</summary>
        /// <param name="prompt">The message</param>
        public async Task<ChatDocumentContext> GetDocumentContextAsync(string prompt)
        {
            DocumentState state = _document;
            if (state == null)
                return null;
            if (!state.Ready.IsCompleted)
                Host.SetStatus("Waiting for the document to be indexed...");
            if (!await state.Ready || state.Index == null || Embedder.Model == null)
                return null;
            Host.SetStatus("Searching the document...");
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

            var context = new ChatDocumentContext();
            // About 400 tokens of excerpts with a 4k window, 800 with 8k.
            int budget = Host.ContextWindow >= 8192 ? 3200 : 1600;
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
            var referenced = new List<DocumentPassage>();
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
            if (Host.Model?.AcceptsImages == true && result.Images.Count > 0 && !(overview && index.Passages.Count > 0))
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
                if (image != null && !_givenImages.Contains(image.Item.Index))
                {
                    context.ImageItem = image.Item;
                    context.Image = new DataContent(image.Item.Data, "image/jpeg") { Name = state.Name + ", " + image.Item.Location };
                }
            }

            context.Text = DocumentPrompt(state, context);
            context.Step = DocumentStep(state, referenced, context.ImageItem);
            Host.SetStatus(null);
            return context;
        }

        /// <summary>
        /// The message's excerpts were chosen before compacting the conversation, leaving out passages given earlier - which
        /// compacting may just have summarized or dropped. Search again, now that nothing counts as given; a picture already
        /// attached is kept.
        /// </summary>
        /// <param name="prompt">The message</param>
        /// <param name="document">What was chosen before</param>
        /// <param name="userItem">The message in the conversation (a new picture is added to it)</param>
        /// <param name="attachments">The message's attachments (a new picture is added), or null</param>
        public async Task<ChatDocumentContext> RefreshDocumentContextAsync(string prompt, ChatDocumentContext document,
            ChatMessageItem userItem, IList<DataContent> attachments)
        {
            ChatDocumentContext again = await GetDocumentContextAsync(prompt);
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
                attachments?.Add(again.Image);
                userItem.AddAttachment(again.Image);
            }
            return again;
        }

        // ---------- The page's send (the default path; a page with its own send calls the methods above) ----------

        /// <inheritdoc/>
        public override async Task<ChatMessageAddition> PrepareMessageAsync(ChatSendRequest request, CancellationToken cancellationToken)
        {
            _pending = null;
            ChatDocumentContext context = await GetDocumentContextAsync(request.Text);
            if (context == null)
                return null;
            _pending = context;
            _pendingCompaction = Host.Conversation?.CompactionCount ?? 0;
            return new ChatMessageAddition
            {
                Context = context.Text,
                Attachments = context.Image != null ? new[] { context.Image } : null,
                StepLine = context.Step
            };
        }

        /// <inheritdoc/>
        public override async Task<ChatMessageAddition> RefreshMessageAsync(ChatMessageItem message, CancellationToken cancellationToken)
        {
            ChatDocumentContext pending = _pending;
            if (pending == null || Host.Conversation == null || Host.Conversation.CompactionCount == _pendingCompaction)
                return null;
            _pendingCompaction = Host.Conversation.CompactionCount;
            ChatDocumentContext again = await RefreshDocumentContextAsync(message.Text, pending, message, null);
            _pending = again;
            return new ChatMessageAddition { Context = again.Text, StepLine = again.Step };
        }

        /// <inheritdoc/>
        public override void OnReplyCompleted(ChatExchangeEventArgs e)
        {
            MarkDocumentContextGiven(_pending);
            _pending = null;
        }

        // What the model is given before the message.
        private static string DocumentPrompt(DocumentState state, ChatDocumentContext context)
        {
            if (state == null || (context.Passages.Count == 0 && context.ImageItem == null))
                return null;
            var text = new StringBuilder();
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

        /// <summary>After a reply: what was given is now in the model's conversation.</summary>
        /// <param name="context">What was given, or null</param>
        public void MarkDocumentContextGiven(ChatDocumentContext context)
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
