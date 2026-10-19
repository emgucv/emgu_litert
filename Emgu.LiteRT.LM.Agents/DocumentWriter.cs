//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// A file the create_document tool made: what the app saves and shows.
    /// </summary>
    public sealed class GeneratedDocument
    {
        /// <summary>
        /// Create a document
        /// </summary>
        public GeneratedDocument(string title, string fileName, string format, string mediaType, byte[] data, string note)
        {
            Title = title;
            FileName = fileName;
            Format = format;
            MediaType = mediaType;
            Data = data;
            Note = note;
        }

        /// <summary>The document's title</summary>
        public string Title { get; }

        /// <summary>A file name made from the title, e.g. "Packing list.docx"</summary>
        public string FileName { get; }

        /// <summary>"Word", "PDF" or "Markdown"</summary>
        public string Format { get; }

        /// <summary>The file's media type</summary>
        public string MediaType { get; }

        /// <summary>The file</summary>
        public byte[] Data { get; }

        /// <summary>Something to tell the model, e.g. that a PDF became a Word document; or null</summary>
        public string Note { get; }
    }

    /// <summary>
    /// Writes a title and Markdown content as a Word document (.docx: Office Open XML, written directly - headings,
    /// paragraphs, bullet and numbered lists, bold, italic, inline code, links, tables, code blocks, quotes), a PDF (a
    /// small writer with the standard Helvetica and Courier fonts: Western European text only) or Markdown.
    /// </summary>
    public static class DocumentWriter
    {
        /// <summary>The Word document media type</summary>
        public const string DocxMediaType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        /// <summary>The PDF media type</summary>
        public const string PdfMediaType = "application/pdf";

        /// <summary>The Markdown media type</summary>
        public const string MarkdownMediaType = "text/markdown";

        /// <summary>
        /// Make a document in a format: "word" / "docx", "pdf" or "markdown" / "md" (anything else is Word). A PDF of
        /// text the standard PDF fonts can't show (e.g. Chinese) becomes a Word document, with a note saying so.
        /// </summary>
        /// <param name="title">The title</param>
        /// <param name="markdown">The content in Markdown</param>
        /// <param name="format">The format</param>
        /// <returns>The document</returns>
        public static GeneratedDocument Create(string title, string markdown, string format)
        {
            title = string.IsNullOrWhiteSpace(title) ? "Document" : title.Trim();
            markdown = markdown ?? string.Empty;
            string f = (format ?? string.Empty).Trim().ToLowerInvariant().TrimStart('.');
            string note = null;
            if (f == "markdown" || f == "md" || f == "text" || f == "txt")
                return new GeneratedDocument(title, FileName(title, ".md"), "Markdown", MarkdownMediaType,
                    Encoding.UTF8.GetBytes(MarkdownText(title, markdown)), null);
            if (f == "pdf")
            {
                byte[] pdf = ToPdf(title, markdown);
                if (pdf != null)
                    return new GeneratedDocument(title, FileName(title, ".pdf"), "PDF", PdfMediaType, pdf, null);
                note = "A PDF can't hold this text's characters, so it was made as a Word document instead.";
            }
            return new GeneratedDocument(title, FileName(title, ".docx"), "Word", DocxMediaType, ToDocx(title, markdown), note);
        }

        /// <summary>
        /// The format for a document: the one the user's request names ("Word", "docx", "PDF", "print" - PDF -,
        /// "Markdown", "md"), else the model's - but Markdown only when the user asks for it: Gemma 4 E2B chose Markdown
        /// for "a moving-house checklist I can print", even told "pdf for something to print, markdown only if the user
        /// asks for it".
        /// </summary>
        /// <param name="modelFormat">The format the model asked for</param>
        /// <param name="userRequest">The user's message, or null</param>
        /// <returns>"word", "pdf" or "markdown"</returns>
        public static string ChooseFormat(string modelFormat, string userRequest)
        {
            string request = (userRequest ?? string.Empty).ToLowerInvariant();
            if (Regex.IsMatch(request, @"\bmarkdown\b|\.md\b|\bmd file"))
                return "markdown";
            if (Regex.IsMatch(request, @"\bpdf\b"))
                return "pdf";
            if (Regex.IsMatch(request, @"\bword\b|\bdocx?\b"))
                return "word";
            if (Regex.IsMatch(request, @"\bprint"))
                return "pdf";
            string model = (modelFormat ?? string.Empty).Trim().ToLowerInvariant().TrimStart('.');
            return model == "pdf" ? "pdf" : "word";
        }

        /// <summary>
        /// A file name from a title: characters file systems don't take removed, at most 60 characters.
        /// </summary>
        public static string FileName(string title, string extension)
        {
            string name = Regex.Replace(title ?? string.Empty, @"[\\/:*?""<>|\x00-\x1f]+", " ");
            name = Regex.Replace(name, @"\s+", " ").Trim().TrimEnd('.');
            if (name.Length > 60)
                name = name.Substring(0, 60).TrimEnd();
            return (name.Length == 0 ? "Document" : name) + extension;
        }

        private static string MarkdownText(string title, string markdown)
        {
            string body = markdown.Replace("\r\n", "\n").Trim();
            // The model usually starts with the title as a heading itself.
            if (body.StartsWith("# ", StringComparison.Ordinal))
                return body + "\n";
            return "# " + title + "\n\n" + body + "\n";
        }

        // ---------- Markdown ----------

        internal enum BlockKind { Heading, Paragraph, Bullet, Numbered, Code, Table, Quote }

        internal sealed class Run
        {
            public string Text;
            public bool Bold;
            public bool Italic;
            public bool Code;
        }

        internal sealed class Block
        {
            public BlockKind Kind;
            public int Level;            // heading 1-6; list indent 0-2
            public int ListId;           // numbered lists: which list (numbering restarts per list)
            public List<Run> Runs = new List<Run>();
            public List<string> Lines;   // code
            public List<List<List<Run>>> Rows; // table: rows of cells of runs; the first row is the header
        }

        private static readonly Regex ListItem = new Regex(@"^(\s*)([-*+]|\d+[.)])\s+(.*)$");

        internal static List<Block> Parse(string title, string markdown)
        {
            List<Block> blocks = new List<Block>();
            string[] lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            StringBuilder paragraph = new StringBuilder();
            BlockKind paragraphKind = BlockKind.Paragraph;
            int listId = 0;
            bool inNumbered = false;

            void Flush()
            {
                if (paragraph.Length > 0)
                    blocks.Add(new Block { Kind = paragraphKind, Runs = Inline(paragraph.ToString().Trim()) });
                paragraph.Clear();
                paragraphKind = BlockKind.Paragraph;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                {
                    Flush();
                    string fence = trimmed.Substring(0, 3);
                    List<string> code = new List<string>();
                    for (i++; i < lines.Length && !lines[i].Trim().StartsWith(fence, StringComparison.Ordinal); i++)
                        code.Add(lines[i].TrimEnd());
                    blocks.Add(new Block { Kind = BlockKind.Code, Lines = code });
                    inNumbered = false;
                    continue;
                }
                if (trimmed.Length == 0)
                {
                    Flush();
                    continue;
                }
                Match heading = Regex.Match(trimmed, @"^(#{1,6})\s+(.*?)\s*#*$");
                if (heading.Success)
                {
                    Flush();
                    int level = heading.Groups[1].Value.Length;
                    string text = heading.Groups[2].Value;
                    // A first heading that is the title itself isn't repeated under the title.
                    if (!(blocks.Count == 0 && level == 1 && string.Equals(StripMarks(text), title, StringComparison.OrdinalIgnoreCase)))
                        blocks.Add(new Block { Kind = BlockKind.Heading, Level = level, Runs = Inline(text) });
                    inNumbered = false;
                    continue;
                }
                if (Regex.IsMatch(trimmed, @"^([-*_])(\s*\1){2,}$"))
                {
                    Flush();
                    continue; // A rule: a paragraph break is enough.
                }
                if (trimmed.StartsWith("|", StringComparison.Ordinal))
                {
                    Flush();
                    List<List<List<Run>>> rows = new List<List<List<Run>>>();
                    for (; i < lines.Length && lines[i].Trim().StartsWith("|", StringComparison.Ordinal); i++)
                    {
                        string row = lines[i].Trim().Trim('|');
                        if (Regex.IsMatch(row, @"^[\s:|-]+$"))
                            continue; // The separator row.
                        rows.Add(row.Split('|').Select(cell => Inline(cell.Trim())).ToList());
                    }
                    i--;
                    if (rows.Count > 0)
                        blocks.Add(new Block { Kind = BlockKind.Table, Rows = rows });
                    inNumbered = false;
                    continue;
                }
                Match item = ListItem.Match(line);
                if (item.Success)
                {
                    Flush();
                    bool numbered = char.IsDigit(item.Groups[2].Value[0]);
                    int indent = Math.Min(2, item.Groups[1].Value.Replace("\t", "    ").Length / 2);
                    if (numbered && !inNumbered)
                        listId++;
                    if (indent == 0)
                        inNumbered = numbered;
                    blocks.Add(new Block
                    {
                        Kind = numbered ? BlockKind.Numbered : BlockKind.Bullet,
                        Level = indent,
                        ListId = listId,
                        Runs = Inline(item.Groups[3].Value.Trim())
                    });
                    continue;
                }
                if (trimmed.StartsWith(">", StringComparison.Ordinal))
                {
                    if (paragraphKind != BlockKind.Quote)
                        Flush();
                    paragraphKind = BlockKind.Quote;
                    paragraph.Append(trimmed.TrimStart('>').Trim()).Append(' ');
                    continue;
                }
                // A line under a list item with no blank line continues the item.
                if (paragraph.Length == 0 && blocks.Count > 0 && (blocks[blocks.Count - 1].Kind == BlockKind.Bullet
                    || blocks[blocks.Count - 1].Kind == BlockKind.Numbered) && i > 0 && lines[i - 1].Trim().Length > 0
                    && char.IsWhiteSpace(line.Length > 0 ? line[0] : 'x'))
                {
                    blocks[blocks.Count - 1].Runs.AddRange(Inline(" " + trimmed));
                    continue;
                }
                if (paragraphKind == BlockKind.Quote)
                    Flush();
                inNumbered = false;
                paragraph.Append(trimmed).Append(' ');
            }
            Flush();
            return blocks;
        }

        private static string StripMarks(string text) => Regex.Replace(text, @"[*_`]", string.Empty).Trim();

        private static readonly Regex InlinePattern = new Regex(
            @"(?<code>`[^`]+`)|(?<bold>\*\*(?=\S)(.+?)(?<=\S)\*\*|__(?=\S)(.+?)(?<=\S)__)|(?<italic>(?<![\w*])\*(?=\S)([^*]+?)(?<=\S)\*(?![\w*])|(?<![\w_])_(?=\S)([^_]+?)(?<=\S)_(?![\w_]))|(?<link>\[([^\]]+)\]\(([^)\s]+)\))");

        internal static List<Run> Inline(string text, bool bold = false, bool italic = false)
        {
            List<Run> runs = new List<Run>();
            int at = 0;
            foreach (Match m in InlinePattern.Matches(text))
            {
                if (m.Index > at)
                    runs.Add(new Run { Text = text.Substring(at, m.Index - at), Bold = bold, Italic = italic });
                if (m.Groups["code"].Success)
                    runs.Add(new Run { Text = m.Value.Trim('`'), Code = true, Bold = bold, Italic = italic });
                else if (m.Groups["bold"].Success)
                    runs.AddRange(Inline(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, true, italic));
                else if (m.Groups["italic"].Success)
                    runs.AddRange(Inline(m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value, bold, true));
                else
                {
                    string label = m.Groups[5].Value, url = m.Groups[6].Value;
                    runs.AddRange(Inline(label, bold, italic));
                    if (!string.Equals(label, url, StringComparison.OrdinalIgnoreCase))
                        runs.Add(new Run { Text = " (" + url + ")", Bold = bold, Italic = italic });
                }
                at = m.Index + m.Length;
            }
            if (at < text.Length)
                runs.Add(new Run { Text = text.Substring(at), Bold = bold, Italic = italic });
            return runs;
        }

        // ---------- Word (.docx) ----------

        /// <summary>
        /// A Word document of a title and Markdown content.
        /// </summary>
        public static byte[] ToDocx(string title, string markdown)
        {
            List<Block> blocks = Parse(title, markdown ?? string.Empty);
            int lists = blocks.Where(b => b.Kind == BlockKind.Numbered).Select(b => b.ListId).DefaultIfEmpty(0).Max();
            StringBuilder body = new StringBuilder();
            body.Append(Paragraph("Title", null, 0, Inline(title)));
            foreach (Block block in blocks)
            {
                switch (block.Kind)
                {
                    case BlockKind.Heading:
                        body.Append(Paragraph("Heading" + Math.Min(block.Level, 3), null, 0, block.Runs));
                        break;
                    case BlockKind.Bullet:
                        body.Append(Paragraph("ListParagraph", 1, block.Level, block.Runs));
                        break;
                    case BlockKind.Numbered:
                        body.Append(Paragraph("ListParagraph", 1 + block.ListId, block.Level, block.Runs));
                        break;
                    case BlockKind.Quote:
                        body.Append(Paragraph("Quote", null, 0, block.Runs));
                        break;
                    case BlockKind.Code:
                        foreach (string line in block.Lines.DefaultIfEmpty(string.Empty))
                            body.Append(Paragraph("Code", null, 0, new List<Run> { new Run { Text = line } }));
                        break;
                    case BlockKind.Table:
                        body.Append(Table(block.Rows));
                        break;
                    default:
                        body.Append(Paragraph(null, null, 0, block.Runs));
                        break;
                }
            }

            using (MemoryStream stream = new MemoryStream())
            {
                using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    Add(zip, "[Content_Types].xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                        "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                        "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
                        "<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>" +
                        "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
                        "</Types>");
                    Add(zip, "_rels/.rels",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                        "</Relationships>");
                    Add(zip, "docProps/core.xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                        "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
                        "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                        "<dc:title>" + Xml(title) + "</dc:title>" +
                        "<dcterms:created xsi:type=\"dcterms:W3CDTF\">" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + "</dcterms:created>" +
                        "</cp:coreProperties>");
                    Add(zip, "word/_rels/document.xml.rels",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/>" +
                        "</Relationships>");
                    Add(zip, "word/styles.xml", Styles);
                    Add(zip, "word/numbering.xml", Numbering(lists));
                    Add(zip, "word/document.xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<w:document xmlns:w=\"" + W + "\"><w:body>" + body +
                        "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
                        "<w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1134\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/></w:sectPr>" +
                        "</w:body></w:document>");
                }
                return stream.ToArray();
            }
        }

        private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        private static void Add(ZipArchive zip, string name, string content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream s = entry.Open())
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(content);
                s.Write(bytes, 0, bytes.Length);
            }
        }

        private static string Xml(string text)
        {
            // Characters XML 1.0 doesn't allow are dropped.
            string clean = new string((text ?? string.Empty).Where(c => c == '\t' || c == '\n' || c == '\r' || c >= 0x20 && c != 0xFFFE && c != 0xFFFF).ToArray());
            return SecurityElement.Escape(clean);
        }

        private static string Paragraph(string style, int? numId, int level, List<Run> runs)
        {
            StringBuilder p = new StringBuilder("<w:p>");
            if (style != null || numId != null)
            {
                p.Append("<w:pPr>");
                if (style != null)
                    p.Append("<w:pStyle w:val=\"").Append(style).Append("\"/>");
                if (numId != null)
                    p.Append("<w:numPr><w:ilvl w:val=\"").Append(level).Append("\"/><w:numId w:val=\"").Append(numId.Value).Append("\"/></w:numPr>");
                p.Append("</w:pPr>");
            }
            foreach (Run run in runs)
                p.Append(RunXml(run));
            return p.Append("</w:p>").ToString();
        }

        private static string RunXml(Run run)
        {
            StringBuilder r = new StringBuilder("<w:r>");
            if (run.Bold || run.Italic || run.Code)
            {
                r.Append("<w:rPr>");
                if (run.Code)
                    r.Append("<w:rFonts w:ascii=\"Courier New\" w:hAnsi=\"Courier New\" w:cs=\"Courier New\"/>");
                if (run.Bold)
                    r.Append("<w:b/>");
                if (run.Italic)
                    r.Append("<w:i/>");
                r.Append("</w:rPr>");
            }
            return r.Append("<w:t xml:space=\"preserve\">").Append(Xml(run.Text)).Append("</w:t></w:r>").ToString();
        }

        private static string Table(List<List<List<Run>>> rows)
        {
            int columns = rows.Max(r => r.Count);
            int width = 9638 / Math.Max(1, columns); // The page's text width in twentieths of a point.
            StringBuilder t = new StringBuilder("<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblLayout w:type=\"fixed\"/>" +
                "<w:tblBorders><w:top w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"999999\"/><w:left w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"999999\"/>" +
                "<w:bottom w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"999999\"/><w:right w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"999999\"/>" +
                "<w:insideH w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"999999\"/><w:insideV w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"999999\"/></w:tblBorders>" +
                "</w:tblPr><w:tblGrid>");
            for (int c = 0; c < columns; c++)
                t.Append("<w:gridCol w:w=\"").Append(width).Append("\"/>");
            t.Append("</w:tblGrid>");
            for (int r = 0; r < rows.Count; r++)
            {
                t.Append(r == 0 ? "<w:tr><w:trPr><w:tblHeader/></w:trPr>" : "<w:tr>");
                for (int c = 0; c < columns; c++)
                {
                    List<Run> cell = c < rows[r].Count ? rows[r][c] : new List<Run>();
                    if (r == 0)
                        cell = cell.Select(run => new Run { Text = run.Text, Bold = true, Italic = run.Italic, Code = run.Code }).ToList();
                    t.Append("<w:tc><w:tcPr><w:tcW w:w=\"").Append(width).Append("\" w:type=\"dxa\"/>");
                    if (r == 0)
                        t.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"EDEFF3\"/>");
                    t.Append("</w:tcPr>").Append(Paragraph("TableText", null, 0, cell)).Append("</w:tc>");
                }
                t.Append("</w:tr>");
            }
            // Word needs a paragraph between two tables, and after one at the end of the body.
            return t.Append("</w:tbl><w:p/>").ToString();
        }

        private static readonly string Styles =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:styles xmlns:w=\"" + W + "\">" +
            "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:eastAsia=\"Calibri\" w:cs=\"Calibri\"/>" +
            "<w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/><w:lang w:val=\"en-US\"/></w:rPr></w:rPrDefault>" +
            "<w:pPrDefault><w:pPr><w:spacing w:after=\"120\" w:line=\"264\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Title\"><w:name w:val=\"Title\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/>" +
            "<w:pPr><w:spacing w:after=\"240\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"44\"/><w:szCs w:val=\"44\"/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/>" +
            "<w:pPr><w:keepNext/><w:spacing w:before=\"240\" w:after=\"120\"/><w:outlineLvl w:val=\"0\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"32\"/><w:szCs w:val=\"32\"/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Heading2\"><w:name w:val=\"heading 2\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/>" +
            "<w:pPr><w:keepNext/><w:spacing w:before=\"200\" w:after=\"80\"/><w:outlineLvl w:val=\"1\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"28\"/><w:szCs w:val=\"28\"/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Heading3\"><w:name w:val=\"heading 3\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/>" +
            "<w:pPr><w:keepNext/><w:spacing w:before=\"160\" w:after=\"60\"/><w:outlineLvl w:val=\"2\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"24\"/><w:szCs w:val=\"24\"/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"ListParagraph\"><w:name w:val=\"List Paragraph\"/><w:basedOn w:val=\"Normal\"/><w:qFormat/>" +
            "<w:pPr><w:spacing w:after=\"40\"/><w:contextualSpacing/></w:pPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Quote\"><w:name w:val=\"Quote\"/><w:basedOn w:val=\"Normal\"/><w:qFormat/>" +
            "<w:pPr><w:ind w:left=\"567\"/></w:pPr><w:rPr><w:i/><w:color w:val=\"555555\"/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Code\"><w:name w:val=\"Code\"/><w:basedOn w:val=\"Normal\"/>" +
            "<w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"F3F4F6\"/></w:pPr>" +
            "<w:rPr><w:rFonts w:ascii=\"Courier New\" w:hAnsi=\"Courier New\" w:cs=\"Courier New\"/><w:sz w:val=\"19\"/><w:szCs w:val=\"19\"/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"TableText\"><w:name w:val=\"Table Text\"/><w:basedOn w:val=\"Normal\"/>" +
            "<w:pPr><w:spacing w:before=\"40\" w:after=\"40\"/></w:pPr></w:style>" +
            "<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:name w:val=\"Normal Table\"/>" +
            "<w:tblPr><w:tblInd w:w=\"0\" w:type=\"dxa\"/><w:tblCellMar><w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"108\" w:type=\"dxa\"/>" +
            "<w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"108\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>" +
            "<w:style w:type=\"table\" w:styleId=\"TableGrid\"><w:name w:val=\"Table Grid\"/><w:basedOn w:val=\"TableNormal\"/></w:style>" +
            "</w:styles>";

        // numId 1: bullets; numId 2...: one per numbered list, so each starts at 1.
        private static string Numbering(int numberedLists)
        {
            StringBuilder n = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:numbering xmlns:w=\"" + W + "\">");
            string[] bullets = { "•", "◦", "▪" };
            n.Append("<w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"hybridMultilevel\"/>");
            for (int level = 0; level < 3; level++)
                n.Append("<w:lvl w:ilvl=\"").Append(level).Append("\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"")
                    .Append(bullets[level]).Append("\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"").Append(720 + 360 * level)
                    .Append("\" w:hanging=\"360\"/></w:pPr></w:lvl>");
            n.Append("</w:abstractNum>");
            // A definition of its own for each numbered list: Word restarts a list for a startOverride on a shared one,
            // but macOS's readers (Quick Look, TextEdit) carried on counting.
            string[] formats = { "decimal", "lowerLetter", "lowerRoman" };
            for (int list = 1; list <= numberedLists; list++)
            {
                n.Append("<w:abstractNum w:abstractNumId=\"").Append(list).Append("\"><w:multiLevelType w:val=\"hybridMultilevel\"/>");
                for (int level = 0; level < 3; level++)
                    n.Append("<w:lvl w:ilvl=\"").Append(level).Append("\"><w:start w:val=\"1\"/><w:numFmt w:val=\"").Append(formats[level])
                        .Append("\"/><w:lvlText w:val=\"%").Append(level + 1).Append(".\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"")
                        .Append(720 + 360 * level).Append("\" w:hanging=\"360\"/></w:pPr></w:lvl>");
                n.Append("</w:abstractNum>");
            }
            n.Append("<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>");
            for (int list = 1; list <= numberedLists; list++)
                n.Append("<w:num w:numId=\"").Append(1 + list).Append("\"><w:abstractNumId w:val=\"").Append(list).Append("\"/></w:num>");
            return n.Append("</w:numbering>").ToString();
        }

        // ---------- PDF ----------

        // Helvetica's widths (1/1000 em) for characters 32-126, from Adobe's AFM; other characters use 556.
        private static readonly int[] HelveticaWidths =
        {
            278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556,
            556, 556, 278, 278, 584, 584, 584, 556, 1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556, 333, 556, 556, 500, 556, 556, 278, 556,
            556, 222, 222, 500, 222, 833, 556, 556, 556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584
        };

        // Helvetica-Bold's widths for characters 32-126.
        private static readonly int[] HelveticaBoldWidths =
        {
            278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556,
            556, 556, 333, 333, 584, 584, 584, 611, 975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556, 333, 556, 611, 556, 611, 556, 333, 611,
            611, 278, 278, 556, 278, 889, 611, 611, 611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584
        };

        // Windows-1252's characters 0x80-0x9F (WinAnsiEncoding), the rest of 0xA0-0xFF being Latin-1.
        private static readonly Dictionary<char, byte> WinAnsiExtra = new Dictionary<char, byte>
        {
            { '€', 0x80 }, { '‚', 0x82 }, { 'ƒ', 0x83 }, { '„', 0x84 }, { '…', 0x85 }, { '†', 0x86 },
            { '‡', 0x87 }, { 'ˆ', 0x88 }, { '‰', 0x89 }, { 'Š', 0x8A }, { '‹', 0x8B }, { 'Œ', 0x8C },
            { 'Ž', 0x8E }, { '‘', 0x91 }, { '’', 0x92 }, { '“', 0x93 }, { '”', 0x94 }, { '•', 0x95 },
            { '–', 0x96 }, { '—', 0x97 }, { '˜', 0x98 }, { '™', 0x99 }, { 'š', 0x9A }, { '›', 0x9B },
            { 'œ', 0x9C }, { 'ž', 0x9E }, { 'Ÿ', 0x9F }
        };

        // Characters the PDF fonts lack, written in ones they have.
        private static readonly Dictionary<char, string> PdfSubstitutes = new Dictionary<char, string>
        {
            { '→', "->" }, { '←', "<-" }, { '≤', "<=" }, { '≥', ">=" }, { '≠', "!=" }, { '−', "-" },
            { '✓', "v" }, { '✔', "v" }, { '✗', "x" }, { '◦', "-" }, { '▪', "-" }, { ' ', " " },
            { '‑', "-" }, { '‐', "-" }, { '′', "'" }, { '″', "\"" }, { '​', "" }, { '️', "" }
        };

        private static bool TryWinAnsi(string text, out byte[] bytes)
        {
            List<byte> result = new List<byte>(text.Length);
            foreach (char c in text)
            {
                if (c >= 0x20 && c < 0x7F || c >= 0xA0 && c <= 0xFF)
                    result.Add((byte)c);
                else if (WinAnsiExtra.TryGetValue(c, out byte b))
                    result.Add(b);
                else if (c == '\t')
                    result.Add((byte)' ');
                else
                {
                    bytes = null;
                    return false;
                }
            }
            bytes = result.ToArray();
            return true;
        }

        private static string PdfText(string text)
        {
            StringBuilder s = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (PdfSubstitutes.TryGetValue(c, out string substitute))
                    s.Append(substitute);
                else
                    s.Append(c);
            }
            return s.ToString();
        }

        private sealed class PdfFont
        {
            public string Name;      // /F1...
            public int[] Widths;     // null: monospaced 600
            public double Width(char c) =>
                Widths == null ? 600 : c >= 32 && c <= 126 ? Widths[c - 32] : 556;
        }

        private static readonly PdfFont Regular = new PdfFont { Name = "F1", Widths = HelveticaWidths };
        private static readonly PdfFont Bold = new PdfFont { Name = "F2", Widths = HelveticaBoldWidths };
        private static readonly PdfFont Oblique = new PdfFont { Name = "F3", Widths = HelveticaWidths };
        private static readonly PdfFont BoldOblique = new PdfFont { Name = "F4", Widths = HelveticaBoldWidths };
        private static readonly PdfFont Mono = new PdfFont { Name = "F5", Widths = null };

        private static PdfFont FontOf(Run run, bool bold)
        {
            if (run.Code)
                return Mono;
            bool b = bold || run.Bold;
            return b && run.Italic ? BoldOblique : b ? Bold : run.Italic ? Oblique : Regular;
        }

        private sealed class PdfPages
        {
            private const double PageWidth = 595.28, PageHeight = 841.89, Margin = 56.7;
            public readonly List<StringBuilder> Pages = new List<StringBuilder>();
            private StringBuilder _page;
            public double Y;
            public double Left => Margin;
            public double TextWidth => PageWidth - 2 * Margin;

            public PdfPages() => NewPage();

            public void NewPage()
            {
                _page = new StringBuilder();
                Pages.Add(_page);
                Y = PageHeight - Margin;
            }

            public void Space(double points)
            {
                Y -= points;
            }

            // Room for a line of this height, or a new page.
            public void Need(double height)
            {
                if (Y - height < Margin)
                    NewPage();
            }

            public void Text(PdfFont font, double size, double x, double y, string text)
            {
                if (text.Length == 0)
                    return;
                TryWinAnsi(text, out byte[] bytes);
                _page.Append("BT /").Append(font.Name).Append(' ').Append(Num(size)).Append(" Tf ")
                    .Append(Num(x)).Append(' ').Append(Num(y)).Append(" Td (");
                foreach (byte b in bytes)
                {
                    if (b == '(' || b == ')' || b == '\\')
                        _page.Append('\\').Append((char)b);
                    else if (b < 0x20 || b > 0x7E)
                        _page.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                    else
                        _page.Append((char)b);
                }
                _page.Append(") Tj ET\n");
            }

            public void Rect(double x, double y, double width, double height, bool fill)
            {
                _page.Append(fill ? "0.95 0.955 0.965 rg " : "0.6 0.6 0.6 RG 0.5 w ").Append(Num(x)).Append(' ').Append(Num(y)).Append(' ')
                    .Append(Num(width)).Append(' ').Append(Num(height)).Append(fill ? " re f 0 0 0 rg\n" : " re S\n");
            }

            public static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // Words with their fonts, for wrapping.
        private sealed class Word
        {
            public string Text;
            public PdfFont Font;
            public bool SpaceBefore;
        }

        private static List<Word> Words(List<Run> runs, bool bold)
        {
            List<Word> words = new List<Word>();
            bool space = false;
            foreach (Run run in runs)
            {
                string text = PdfText(run.Text);
                PdfFont font = FontOf(run, bold);
                int i = 0;
                while (i < text.Length)
                {
                    if (char.IsWhiteSpace(text[i]))
                    {
                        space = true;
                        i++;
                        continue;
                    }
                    int start = i;
                    while (i < text.Length && !char.IsWhiteSpace(text[i]))
                        i++;
                    words.Add(new Word { Text = text.Substring(start, i - start), Font = font, SpaceBefore = space && words.Count > 0 });
                    space = false;
                }
            }
            return words;
        }

        private static double Width(string text, PdfFont font, double size) => text.Sum(font.Width) * size / 1000;

        // Lines of words that fit a width (a word wider than the line is cut).
        private static List<List<Word>> Wrap(List<Word> words, double width, double size)
        {
            List<List<Word>> lines = new List<List<Word>> { new List<Word>() };
            double x = 0;
            foreach (Word w in words)
            {
                double space = w.SpaceBefore && lines[lines.Count - 1].Count > 0 ? Width(" ", w.Font, size) : 0;
                double ww = Width(w.Text, w.Font, size);
                if (lines[lines.Count - 1].Count > 0 && x + space + ww > width)
                {
                    lines.Add(new List<Word>());
                    x = 0;
                    space = 0;
                }
                string text = w.Text;
                while (Width(text, w.Font, size) > width && text.Length > 1)
                {
                    int fit = 1;
                    while (fit < text.Length && Width(text.Substring(0, fit + 1), w.Font, size) <= width)
                        fit++;
                    lines[lines.Count - 1].Add(new Word { Text = text.Substring(0, fit), Font = w.Font });
                    lines.Add(new List<Word>());
                    text = text.Substring(fit);
                }
                lines[lines.Count - 1].Add(new Word { Text = text, Font = w.Font, SpaceBefore = space > 0 });
                x += space + Width(text, w.Font, size);
            }
            return lines;
        }

        // Words in the same font go out as one string, so viewers copy and search the text with its spaces.
        private static void DrawLine(PdfPages pages, List<Word> line, double x, double y, double size)
        {
            int i = 0;
            while (i < line.Count)
            {
                if (line[i].SpaceBefore)
                    x += Width(" ", line[i].Font, size);
                StringBuilder text = new StringBuilder(line[i].Text);
                PdfFont font = line[i].Font;
                for (i++; i < line.Count && line[i].Font == font; i++)
                    text.Append(line[i].SpaceBefore ? " " : "").Append(line[i].Text);
                pages.Text(font, size, x, y, text.ToString());
                x += Width(text.ToString(), font, size);
            }
        }

        private static void Flow(PdfPages pages, List<Run> runs, bool bold, double size, double indent, double after, string marker = null)
        {
            double leading = size * 1.3;
            double x = pages.Left + indent;
            List<List<Word>> lines = Wrap(Words(runs, bold), pages.TextWidth - indent, size);
            for (int i = 0; i < lines.Count; i++)
            {
                pages.Need(leading);
                pages.Y -= leading;
                if (i == 0 && marker != null)
                    pages.Text(Regular, size, x - Width(marker, Regular, size) - 6, pages.Y, marker);
                DrawLine(pages, lines[i], x, pages.Y, size);
            }
            pages.Space(after);
        }

        /// <summary>
        /// A PDF of a title and Markdown content (A4), or null if the text has characters the standard PDF fonts can't
        /// show (WinAnsi: Western European; arrows and the like are written as "->").
        /// </summary>
        public static byte[] ToPdf(string title, string markdown)
        {
            List<Block> blocks = Parse(title, markdown ?? string.Empty);
            string all = title + string.Concat(blocks.SelectMany(b =>
                (b.Runs ?? new List<Run>()).Select(r => r.Text)
                .Concat(b.Lines ?? new List<string>())
                .Concat(b.Rows?.SelectMany(r => r.SelectMany(c => c.Select(x => x.Text))) ?? Enumerable.Empty<string>())));
            if (!TryWinAnsi(PdfText(all).Replace("\n", " "), out _))
                return null;

            PdfPages pages = new PdfPages();
            Flow(pages, Inline(title), true, 20, 0, 10);
            int number = 0;
            int lastList = -1;
            foreach (Block block in blocks)
            {
                switch (block.Kind)
                {
                    case BlockKind.Heading:
                        double size = block.Level <= 1 ? 16 : block.Level == 2 ? 13.5 : 12;
                        pages.Space(size * 0.6);
                        pages.Need(size * 3); // Not alone at the bottom of a page.
                        Flow(pages, block.Runs, true, size, 0, 3);
                        break;
                    case BlockKind.Bullet:
                        Flow(pages, block.Runs, false, 11, 18 + 14 * block.Level, 2, block.Level == 0 ? "•" : "-");
                        break;
                    case BlockKind.Numbered:
                        if (block.ListId != lastList)
                            number = 0;
                        lastList = block.ListId;
                        if (block.Level == 0)
                            number++;
                        Flow(pages, block.Runs, false, 11, 18 + 14 * block.Level, 2,
                            block.Level == 0 ? number.ToString(CultureInfo.InvariantCulture) + "." : "-");
                        break;
                    case BlockKind.Quote:
                        Flow(pages, block.Runs.Select(r => new Run { Text = r.Text, Bold = r.Bold, Italic = true, Code = r.Code }).ToList(), false, 11, 18, 6);
                        break;
                    case BlockKind.Code:
                        pages.Space(2);
                        foreach (string line in block.Lines)
                        {
                            List<Run> runs = new List<Run> { new Run { Text = line.Length == 0 ? " " : line, Code = true } };
                            foreach (List<Word> wrapped in Wrap(Words(runs, false), pages.TextWidth - 12, 9.5))
                            {
                                pages.Need(12.5);
                                pages.Y -= 12.5;
                                pages.Rect(pages.Left, pages.Y - 3.5, pages.TextWidth, 12.5, true);
                                DrawLine(pages, wrapped, pages.Left + 6, pages.Y, 9.5);
                            }
                        }
                        pages.Space(8);
                        break;
                    case BlockKind.Table:
                        Table(pages, block.Rows);
                        break;
                    default:
                        Flow(pages, block.Runs, false, 11, 0, 6);
                        break;
                }
            }
            return PdfFile(pages.Pages);
        }

        private static void Table(PdfPages pages, List<List<List<Run>>> rows)
        {
            const double size = 10, pad = 4;
            int columns = rows.Max(r => r.Count);
            double column = pages.TextWidth / columns;
            pages.Space(4);
            for (int r = 0; r < rows.Count; r++)
            {
                List<List<List<Word>>> cells = new List<List<List<Word>>>();
                for (int c = 0; c < columns; c++)
                    cells.Add(Wrap(Words(c < rows[r].Count ? rows[r][c] : new List<Run>(), r == 0), column - 2 * pad, size));
                double height = cells.Max(lines => lines.Count) * size * 1.3 + 2 * pad;
                pages.Need(height);
                double top = pages.Y;
                if (r == 0)
                    pages.Rect(pages.Left, top - height, pages.TextWidth, height, true);
                for (int c = 0; c < columns; c++)
                {
                    double y = top - pad;
                    foreach (List<Word> line in cells[c])
                    {
                        y -= size * 1.3;
                        DrawLine(pages, line, pages.Left + c * column + pad, y + 3, size);
                    }
                    pages.Rect(pages.Left + c * column, top - height, column, height, false);
                }
                pages.Y = top - height;
            }
            pages.Space(10);
        }

        private static byte[] PdfFile(List<StringBuilder> pages)
        {
            // Objects: 1 catalog, 2 pages, 3-7 fonts, then a page and its content stream for each page.
            List<string> objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [" + string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => (8 + 2 * i) + " 0 R")) +
                    "] /Count " + pages.Count + " >>",
                Font("Helvetica"), Font("Helvetica-Bold"), Font("Helvetica-Oblique"), Font("Helvetica-BoldOblique"), Font("Courier")
            };
            for (int i = 0; i < pages.Count; i++)
            {
                objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.89] /Resources << /Font << /F1 3 0 R /F2 4 0 R " +
                    "/F3 5 0 R /F4 6 0 R /F5 7 0 R >> >> /Contents " + (9 + 2 * i) + " 0 R >>");
                string content = pages[i].ToString();
                objects.Add("<< /Length " + Encoding.ASCII.GetByteCount(content) + " >>\nstream\n" + content + "endstream");
            }
            using (MemoryStream stream = new MemoryStream())
            {
                void Write(string s)
                {
                    byte[] bytes = Encoding.ASCII.GetBytes(s);
                    stream.Write(bytes, 0, bytes.Length);
                }
                Write("%PDF-1.4\n");
                stream.Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' }, 0, 6);
                List<long> offsets = new List<long>();
                for (int i = 0; i < objects.Count; i++)
                {
                    offsets.Add(stream.Position);
                    Write((i + 1) + " 0 obj\n" + objects[i] + "\nendobj\n");
                }
                long xref = stream.Position;
                Write("xref\n0 " + (objects.Count + 1) + "\n0000000000 65535 f \n");
                foreach (long offset in offsets)
                    Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
                Write("trailer\n<< /Size " + (objects.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
                return stream.ToArray();
            }
        }

        private static string Font(string name) =>
            "<< /Type /Font /Subtype /Type1 /BaseFont /" + name + " /Encoding /WinAnsiEncoding >>";
    }

    public static partial class AgentTools
    {
        /// <summary>
        /// create_document: the model writes a document's content in Markdown (with a title and a format - Word, PDF or
        /// Markdown), DocumentWriter makes the file, and the app's save shows it to the user (e.g. a card with Open and
        /// Share) and returns DocumentCreatedResult. The model doesn't see the file.
        /// </summary>
        /// <param name="save">Saves and shows the document; returns the result for the model</param>
        /// <param name="userRequest">The user's message being answered, if known: a format it names wins over the
        /// model's (DocumentWriter.ChooseFormat)</param>
        /// <returns>The tool</returns>
        public static AIFunction CreateDocument(Func<GeneratedDocument, CancellationToken, Task<string>> save, Func<string> userRequest = null)
        {
            if (save == null)
                throw new ArgumentNullException(nameof(save));
            return AIFunctionFactory.Create(
                ([Description("The document's title, e.g. \"Packing list for Iceland\"")] string title,
                 [Description("The whole document in Markdown: # headings, paragraphs, - lists, 1. numbered lists, **bold**, *italic*, | tables |")] string content,
                 [Description("word, pdf or markdown: pdf for something to print, markdown only if the user asks for it, else word")] string format = "word",
                 CancellationToken cancellationToken = default) =>
                {
                    if (string.IsNullOrWhiteSpace(content))
                        return Task.FromResult("No content: call create_document again with the whole document in content.");
                    return save(DocumentWriter.Create(title, content, DocumentWriter.ChooseFormat(format, userRequest?.Invoke())), cancellationToken);
                },
                "create_document",
                "Creates a document file - Word, PDF or Markdown - from content you write in Markdown, and gives it to the user to open or share. Use it when the user asks for a document, a file, or something written up as Word, PDF or Markdown.");
        }

        /// <summary>
        /// The result for the model once the app has saved and shown a document.
        /// </summary>
        public static string DocumentCreatedResult(GeneratedDocument document)
        {
            return "Created \"" + document.FileName + "\" (" + document.Format + ", " + SizeText(document.Data.Length) + ") and gave it to the user to open or share." +
                (document.Note == null ? "" : " " + document.Note) +
                " Tell the user in one short sentence; don't repeat the document's content.";
        }

        private static string SizeText(long bytes) =>
            bytes < 1024 ? bytes.ToString(CultureInfo.InvariantCulture) + " bytes" :
            bytes < 1024 * 1024 ? (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB" :
            (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
    }
}
