//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// A passage of a document's text, e.g. a few paragraphs of one page.
    /// </summary>
    public class DocumentPassage
    {
        /// <summary>
        /// The passage's position in the document (0, 1, ...)
        /// </summary>
        public int Index { get; internal set; }

        /// <summary>
        /// The text
        /// </summary>
        public String Text { get; internal set; }

        /// <summary>
        /// Where it is in the document, e.g. "page 3"; or null
        /// </summary>
        public String Location { get; internal set; }

        /// <summary>
        /// The embedding, or null until the index is built
        /// </summary>
        public float[] Embedding { get; internal set; }
    }

    /// <summary>
    /// A picture in a document, or an image of a page (e.g. a scanned one).
    /// </summary>
    public class DocumentImage
    {
        /// <summary>
        /// The image's position among the document's images (0, 1, ...)
        /// </summary>
        public int Index { get; internal set; }

        /// <summary>
        /// The encoded image (JPEG, PNG, ...)
        /// </summary>
        public byte[] Data { get; internal set; }

        /// <summary>
        /// The image's media type, e.g. "image/jpeg"
        /// </summary>
        public String MediaType { get; internal set; }

        /// <summary>
        /// Where it is in the document, e.g. "page 3"; or null
        /// </summary>
        public String Location { get; internal set; }

        /// <summary>
        /// True if the image is a whole page (e.g. a scanned page without text), false for a picture on a page
        /// </summary>
        public bool IsPage { get; internal set; }

        /// <summary>
        /// The embedding, or null until the index is built
        /// </summary>
        public float[] Embedding { get; internal set; }
    }

    /// <summary>
    /// A passage or image found by DocumentIndex.Search, with its cosine similarity to the query.
    /// </summary>
    /// <typeparam name="T">DocumentPassage or DocumentImage</typeparam>
    public class DocumentMatch<T>
    {
        /// <summary>
        /// Create a match
        /// </summary>
        public DocumentMatch(T item, float score)
        {
            Item = item;
            Score = score;
        }

        /// <summary>
        /// The passage or image
        /// </summary>
        public T Item { get; }

        /// <summary>
        /// The cosine similarity of its embedding and the query's
        /// </summary>
        public float Score { get; }
    }

    /// <summary>
    /// What DocumentIndex.Search found: every passage and image, the best match first.
    /// </summary>
    public class DocumentSearchResult
    {
        internal DocumentSearchResult(List<DocumentMatch<DocumentPassage>> passages, List<DocumentMatch<DocumentImage>> images)
        {
            Passages = passages;
            Images = images;
        }

        /// <summary>
        /// The passages, the best match first
        /// </summary>
        public IReadOnlyList<DocumentMatch<DocumentPassage>> Passages { get; }

        /// <summary>
        /// The images, the best match first
        /// </summary>
        public IReadOnlyList<DocumentMatch<DocumentImage>> Images { get; }
    }

    /// <summary>
    /// Progress of DocumentIndex.Build.
    /// </summary>
    public struct DocumentIndexProgress
    {
        /// <summary>
        /// Passages embedded so far, and in all
        /// </summary>
        public int PassagesDone, PassageCount;

        /// <summary>
        /// Images embedded so far, and in all
        /// </summary>
        public int ImagesDone, ImageCount;
    }

    /// <summary>
    /// A searchable document for question answering with a small language model ("retrieval-augmented generation"):
    /// its text is split into passages, which an embedding model turns into vectors together with its pictures, so
    /// Search finds the passages and images most related to a question - and only those are given to the language
    /// model, whose context window can't hold a whole document. Add the text (AddText, e.g. once per page) and images
    /// (AddImage), call Build, then Search. Images need a model that embeds them (EmbeddingModel.SupportsImages, e.g.
    /// EmbeddingGemma2TextVision), which puts them in the same space as text, so a question finds them too.
    /// </summary>
    public class DocumentIndex
    {
        private readonly List<DocumentPassage> _passages = new List<DocumentPassage>();
        private readonly List<DocumentImage> _images = new List<DocumentImage>();

        /// <summary>
        /// The longest passage, in characters. About 150-200 tokens of English text: short enough for a few to fit a
        /// small model's context next to the conversation, long enough to keep a thought together.
        /// </summary>
        public int MaxPassageLength { get; set; } = 700;

        /// <summary>
        /// How much of the end of a passage the next one repeats, in characters (whole sentences), so a fact split
        /// between two passages is still found in one of them.
        /// </summary>
        public int PassageOverlap { get; set; } = 120;

        /// <summary>
        /// The passages, in document order
        /// </summary>
        public IReadOnlyList<DocumentPassage> Passages
        {
            get { return _passages; }
        }

        /// <summary>
        /// The images, in the order they were added
        /// </summary>
        public IReadOnlyList<DocumentImage> Images
        {
            get { return _images; }
        }

        /// <summary>
        /// The number of characters of all passages' text (the overlaps counted once per passage)
        /// </summary>
        public int TextLength
        {
            get { return _passages.Sum(p => p.Text.Length); }
        }

        /// <summary>
        /// True once Build has embedded every passage and image
        /// </summary>
        public bool IsBuilt
        {
            get { return _passages.All(p => p.Embedding != null) && _images.All(i => i.Embedding != null); }
        }

        /// <summary>
        /// Add text, split into passages of at most MaxPassageLength characters.
        /// </summary>
        /// <param name="text">The text, e.g. a page</param>
        /// <param name="location">Where it is in the document, e.g. "page 3"; or null</param>
        /// <returns>The number of passages added</returns>
        public int AddText(String text, String location = null)
        {
            int count = 0;
            foreach (String passage in SplitText(text, MaxPassageLength, PassageOverlap))
            {
                _passages.Add(new DocumentPassage { Index = _passages.Count, Text = passage, Location = location });
                count++;
            }
            return count;
        }

        /// <summary>
        /// Add one passage as it is (not split), e.g. one of a saved index, with its embedding if it has one.
        /// </summary>
        /// <param name="text">The passage</param>
        /// <param name="location">Where it is in the document, e.g. "page 3"; or null</param>
        /// <param name="embedding">Its embedding, or null to compute it in Build</param>
        /// <returns>The passage</returns>
        public DocumentPassage AddPassage(String text, String location = null, float[] embedding = null)
        {
            DocumentPassage passage = new DocumentPassage { Index = _passages.Count, Text = text ?? String.Empty, Location = location, Embedding = embedding };
            _passages.Add(passage);
            return passage;
        }

        /// <summary>
        /// Add an image.
        /// </summary>
        /// <param name="encodedImage">The encoded image: JPEG, PNG, BMP, ...</param>
        /// <param name="mediaType">Its media type, e.g. "image/jpeg"</param>
        /// <param name="location">Where it is in the document, e.g. "page 3"; or null</param>
        /// <param name="isPage">True if it is a whole page (e.g. a scan), false for a picture</param>
        /// <param name="embedding">Its embedding (e.g. of a saved index), or null to compute it in Build</param>
        /// <returns>The image</returns>
        public DocumentImage AddImage(byte[] encodedImage, String mediaType, String location = null, bool isPage = false, float[] embedding = null)
        {
            if (encodedImage == null)
                throw new ArgumentNullException("encodedImage");
            DocumentImage image = new DocumentImage
            {
                Index = _images.Count,
                Data = encodedImage,
                MediaType = mediaType,
                Location = location,
                IsPage = isPage,
                Embedding = embedding
            };
            _images.Add(image);
            return image;
        }

        /// <summary>
        /// Embed the passages and images that aren't yet. Runs on the calling thread and takes a while (each passage
        /// and image is a model run): call it from a background thread.
        /// </summary>
        /// <param name="model">An initialized embedding model; one that SupportsImages if images were added</param>
        /// <param name="progress">Reports progress after each batch, or null</param>
        /// <param name="cancellationToken">Stops between batches</param>
        public void Build(EmbeddingModel model, IProgress<DocumentIndexProgress> progress = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (model == null)
                throw new ArgumentNullException("model");
            if (_images.Any(i => i.Embedding == null) && !model.SupportsImages)
                throw new NotSupportedException(String.Format("{0} can't embed images", model.GetType().Name));

            DocumentIndexProgress state = new DocumentIndexProgress
            {
                PassageCount = _passages.Count,
                PassagesDone = _passages.Count(p => p.Embedding != null),
                ImageCount = _images.Count,
                ImagesDone = _images.Count(i => i.Embedding != null)
            };
            // Passages in small batches (one model call each), so progress and cancellation stay responsive.
            const int BatchSize = 8;
            List<DocumentPassage> pending = _passages.Where(p => p.Embedding == null).ToList();
            for (int start = 0; start < pending.Count; start += BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<DocumentPassage> batch = pending.Skip(start).Take(BatchSize).ToList();
                float[][] embeddings = model.EmbedDocuments(batch.Select(p => p.Text).ToArray());
                for (int i = 0; i < batch.Count; i++)
                    batch[i].Embedding = embeddings[i];
                state.PassagesDone += batch.Count;
                if (progress != null)
                    progress.Report(state);
            }
            foreach (DocumentImage image in _images.Where(i => i.Embedding == null))
            {
                cancellationToken.ThrowIfCancellationRequested();
                image.Embedding = model.EmbedImage(image.Data);
                state.ImagesDone++;
                if (progress != null)
                    progress.Report(state);
            }
        }

        /// <summary>
        /// Rank the passages and images by how related they are to a query. Embeds the query (one model run).
        /// </summary>
        /// <param name="model">The embedding model the index was built with</param>
        /// <param name="query">The question</param>
        /// <returns>Every embedded passage and image with its score, the best match first</returns>
        public DocumentSearchResult Search(EmbeddingModel model, String query)
        {
            if (model == null)
                throw new ArgumentNullException("model");
            float[] q = model.EmbedQuery(query ?? String.Empty);
            List<DocumentMatch<DocumentPassage>> passages = _passages
                .Where(p => p.Embedding != null)
                .Select(p => new DocumentMatch<DocumentPassage>(p, EmbeddingModel.CosineSimilarity(q, p.Embedding)))
                .OrderByDescending(m => m.Score)
                .ToList();
            List<DocumentMatch<DocumentImage>> images = _images
                .Where(i => i.Embedding != null)
                .Select(i => new DocumentMatch<DocumentImage>(i, EmbeddingModel.CosineSimilarity(q, i.Embedding)))
                .OrderByDescending(m => m.Score)
                .ToList();
            return new DocumentSearchResult(passages, images);
        }

        /// <summary>
        /// Split text into passages of at most maxLength characters, at paragraph and sentence ends where possible
        /// (otherwise at spaces, or anywhere), each repeating up to overlap characters of whole sentences from the end
        /// of the one before. Runs of white space within a line become one space; empty lines separate paragraphs.
        /// </summary>
        /// <param name="text">The text</param>
        /// <param name="maxLength">The longest passage, in characters</param>
        /// <param name="overlap">How much of the previous passage to repeat, in characters</param>
        /// <returns>The passages</returns>
        public static List<String> SplitText(String text, int maxLength = 700, int overlap = 120)
        {
            if (maxLength < 50)
                throw new ArgumentOutOfRangeException("maxLength");
            overlap = Math.Max(0, Math.Min(overlap, maxLength / 2));
            List<String> passages = new List<String>();
            if (String.IsNullOrWhiteSpace(text))
                return passages;

            // Sentences (or pieces of over-long ones), each marked with whether a paragraph starts with it.
            List<KeyValuePair<String, bool>> sentences = new List<KeyValuePair<String, bool>>();
            foreach (String paragraph in SplitParagraphs(text))
            {
                bool first = true;
                foreach (String sentence in SplitSentences(paragraph))
                    foreach (String piece in SplitLong(sentence, maxLength))
                    {
                        sentences.Add(new KeyValuePair<String, bool>(piece, first));
                        first = false;
                    }
            }

            List<String> current = new List<String>();
            List<bool> starts = new List<bool>();
            int length = 0;
            int newInCurrent = 0;
            for (int i = 0; i < sentences.Count; i++)
            {
                String sentence = sentences[i].Key;
                int added = (current.Count > 0 ? 1 : 0) + sentence.Length;
                if (current.Count > 0 && length + added > maxLength)
                {
                    passages.Add(Join(current, starts));
                    // Start the next passage with the last sentences that fit the overlap (never all of them, so the
                    // text always moves on).
                    List<String> keep = new List<String>();
                    List<bool> keepStarts = new List<bool>();
                    int keptLength = 0;
                    for (int j = current.Count - 1; j > 0; j--)
                    {
                        int l = current[j].Length + (keep.Count > 0 ? 1 : 0);
                        if (keptLength + l > overlap || keptLength + l + 1 + sentence.Length > maxLength)
                            break;
                        keep.Insert(0, current[j]);
                        keepStarts.Insert(0, starts[j]);
                        keptLength += l;
                    }
                    current = keep;
                    starts = keepStarts;
                    length = keptLength;
                    newInCurrent = 0;
                    added = (current.Count > 0 ? 1 : 0) + sentence.Length;
                }
                current.Add(sentence);
                starts.Add(sentences[i].Value);
                length += added;
                newInCurrent++;
            }
            if (current.Count > 0 && newInCurrent > 0)
                passages.Add(Join(current, starts));
            return passages;
        }

        // Paragraphs: separated by empty lines; within one, runs of white space become one space. Text extracted from PDFs
        // often lacks empty lines, so a short line without closing punctuation followed by one starting with a capital
        // letter, or a line starting with a list marker, begins a new paragraph (a heading or list item); body text
        // wraps at long lines.
        private const int MaxHeadingLength = 60;

        private static IEnumerable<String> SplitParagraphs(String text)
        {
            String[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            StringBuilder paragraph = new StringBuilder();
            int lastLineLength = 0;
            foreach (String rawLine in lines)
            {
                String line = CollapseSpaces(rawLine);
                if (line.Length == 0)
                {
                    if (paragraph.Length > 0)
                    {
                        yield return paragraph.ToString();
                        paragraph.Clear();
                    }
                    continue;
                }
                if (paragraph.Length > 0)
                {
                    char last = paragraph[paragraph.Length - 1];
                    bool heading = lastLineLength <= MaxHeadingLength && ".:;!?,\"”".IndexOf(last) < 0;
                    if ((heading && Char.IsUpper(line[0])) || IsListMarker(line))
                    {
                        yield return paragraph.ToString();
                        paragraph.Clear();
                    }
                    else if (last == '-' && paragraph.Length > 1 && Char.IsLetter(paragraph[paragraph.Length - 2]) && Char.IsLower(line[0]))
                    {
                        // A word hyphenated at the end of a line.
                        paragraph.Length--;
                    }
                    else
                    {
                        paragraph.Append(' ');
                    }
                }
                paragraph.Append(line);
                lastLineLength = line.Length;
            }
            if (paragraph.Length > 0)
                yield return paragraph.ToString();
        }

        private static bool IsListMarker(String line)
        {
            return line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("•")
                || (line.Length > 2 && Char.IsDigit(line[0]) && (line[1] == '.' || line[1] == ')'));
        }

        private static String CollapseSpaces(String line)
        {
            StringBuilder sb = new StringBuilder(line.Length);
            bool space = false;
            foreach (char c in line)
            {
                if (Char.IsWhiteSpace(c) || Char.IsControl(c))
                {
                    space = sb.Length > 0;
                    continue;
                }
                if (space)
                    sb.Append(' ');
                space = false;
                sb.Append(c);
            }
            return sb.ToString();
        }

        // Sentences end with . ! ? followed by a space (but not the dot of an abbreviation), or with the full stops of
        // Chinese and Japanese text.
        private static IEnumerable<String> SplitSentences(String paragraph)
        {
            int start = 0;
            for (int i = 0; i < paragraph.Length; i++)
            {
                char c = paragraph[i];
                bool end = false;
                if (c == '。' || c == '！' || c == '？')
                    end = true;
                else if ((c == '.' || c == '!' || c == '?') && i + 1 < paragraph.Length && paragraph[i + 1] == ' ')
                {
                    int wordStart = paragraph.LastIndexOf(' ', i) + 1;
                    // Not after an abbreviation: a short word ("Dr.", "No.") or one with dots inside ("e.g.", "U.S.").
                    end = c != '.'
                        || ((i - wordStart > 2 || (i - wordStart == 2 && Char.IsDigit(paragraph[i - 1])))
                            && paragraph.IndexOf('.', wordStart, i - wordStart) < 0);
                }
                if (end)
                {
                    yield return paragraph.Substring(start, i + 1 - start).Trim();
                    start = i + 1;
                }
            }
            if (start < paragraph.Length)
            {
                String rest = paragraph.Substring(start).Trim();
                if (rest.Length > 0)
                    yield return rest;
            }
        }

        // A sentence longer than a passage (e.g. a table read as one line) is cut at spaces, or anywhere.
        private static IEnumerable<String> SplitLong(String sentence, int maxLength)
        {
            while (sentence.Length > maxLength)
            {
                int cut = sentence.LastIndexOf(' ', maxLength);
                if (cut < maxLength / 2)
                    cut = maxLength;
                yield return sentence.Substring(0, cut).Trim();
                sentence = sentence.Substring(cut).Trim();
            }
            if (sentence.Length > 0)
                yield return sentence;
        }

        // Sentences of one paragraph are joined with a space, paragraphs with a line break.
        private static String Join(List<String> sentences, List<bool> paragraphStarts)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < sentences.Count; i++)
            {
                if (i > 0)
                    sb.Append(paragraphStarts[i] ? '\n' : ' ');
                sb.Append(sentences[i]);
            }
            return sb.ToString();
        }
    }
}
