using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The create_document tool's files (Emgu.LiteRT.LM.Agents' DocumentWriter): Word, PDF and Markdown from Markdown;
// no model or native library. Set EMGU_DOCUMENT_OUT to a folder to keep the files for a look.
[TestFixture]
public class UnitTestDocumentWriter
{
    private const string Sample =
        "# Packing list for Iceland\n\n" +
        "A week in **October**: expect *rain*, wind and `0-8 °C`. See [the forecast](https://vedur.is).\n\n" +
        "## Clothes\n\n" +
        "- Waterproof jacket\n" +
        "- Two fleece layers\n" +
        "  - one light, one warm\n" +
        "- Hiking boots\n\n" +
        "## Before leaving\n\n" +
        "1. Check the road conditions\n" +
        "2. Book the Blue Lagoon\n" +
        "3. Download offline maps\n\n" +
        "| Item | Quantity | Note |\n" +
        "|------|---------:|------|\n" +
        "| Socks | 7 | wool |\n" +
        "| Gloves | 2 | one waterproof pair |\n\n" +
        "> Weather changes fast — pack layers.\n\n" +
        "```\nchecklist --print\n```\n\n" +
        "Then:\n\n" +
        "1. Lock the door\n" +
        "2. Water the plants\n";

    private static string Out(string name, byte[] data)
    {
        string folder = Environment.GetEnvironmentVariable("EMGU_DOCUMENT_OUT");
        if (string.IsNullOrEmpty(folder))
            return null;
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    [Test]
    public void WritesWordDocuments()
    {
        GeneratedDocument document = DocumentWriter.Create("Packing list for Iceland", Sample, "word");
        Assert.That(document.FileName, Is.EqualTo("Packing list for Iceland.docx"));
        Assert.That(document.MediaType, Is.EqualTo(DocumentWriter.DocxMediaType));
        Out(document.FileName, document.Data);
        using ZipArchive zip = new ZipArchive(new MemoryStream(document.Data));
        Assert.That(zip.Entries.Select(e => e.FullName), Is.SupersetOf(new[]
        {
            "[Content_Types].xml", "_rels/.rels", "word/document.xml", "word/styles.xml", "word/numbering.xml", "word/_rels/document.xml.rels"
        }));
        string xml = new StreamReader(zip.GetEntry("word/document.xml").Open()).ReadToEnd();
        // Well-formed.
        System.Xml.Linq.XDocument.Parse(xml);
        System.Xml.Linq.XDocument.Parse(new StreamReader(zip.GetEntry("word/numbering.xml").Open()).ReadToEnd());
        Assert.That(Regex.Matches(xml, "w:val=\"Title\"").Count, Is.EqualTo(1), "The title once - the Markdown's own # title isn't repeated.");
        Assert.That(xml, Does.Contain("w:val=\"Heading1\"").Or.Contain("w:val=\"Heading2\""));
        Assert.That(xml, Does.Contain("<w:b/></w:rPr><w:t xml:space=\"preserve\">October</w:t>"));
        Assert.That(xml, Does.Contain("<w:i/></w:rPr><w:t xml:space=\"preserve\">rain</w:t>"));
        Assert.That(xml, Does.Contain("Courier New"));
        Assert.That(xml, Does.Contain("the forecast</w:t>").And.Contain("(https://vedur.is)"));
        Assert.That(xml, Does.Contain("<w:tbl>").And.Contain("Gloves").And.Contain("one waterproof pair"));
        Assert.That(xml, Does.Contain("w:val=\"Quote\"").And.Contain("w:val=\"Code\""));
        // Bullets (numId 1, the sub-item at level 1) and two numbered lists that each start at 1 (numIds 2 and 3).
        Assert.That(xml, Does.Contain("<w:ilvl w:val=\"1\"/><w:numId w:val=\"1\"/>"));
        Assert.That(Regex.Matches(xml, "<w:numId w:val=\"2\"/>").Count, Is.EqualTo(3));
        Assert.That(Regex.Matches(xml, "<w:numId w:val=\"3\"/>").Count, Is.EqualTo(2));
        Assert.That(xml, Does.Not.Contain("**").And.Not.Contain("`"));
    }

    [Test]
    public void WritesPdfs()
    {
        GeneratedDocument document = DocumentWriter.Create("Packing list for Iceland", Sample, "pdf");
        Assert.That(document.Format, Is.EqualTo("PDF"));
        Assert.That(document.Note, Is.Null);
        Out(document.FileName, document.Data);
        string pdf = Encoding.Latin1.GetString(document.Data);
        Assert.That(pdf, Does.StartWith("%PDF-1.4").And.EndWith("%%EOF\n"));
        // The cross-reference table points at each object.
        int startxref = int.Parse(Regex.Match(pdf, @"startxref\n(\d+)").Groups[1].Value);
        Assert.That(pdf.Substring(startxref), Does.StartWith("xref"));
        foreach (Match entry in Regex.Matches(pdf.Substring(startxref), @"(\d{10}) 00000 n"))
            Assert.That(pdf.Substring(int.Parse(entry.Groups[1].Value)), Does.Match(@"^\d+ 0 obj"));
        Assert.That(pdf, Does.Contain("(Waterproof jacket) Tj").And.Contain("(Gloves) Tj").And.Contain("(Clothes) Tj"));
        // "—" is WinAnsi 0x97, written in octal.
        Assert.That(pdf, Does.Contain("\\227"));

        // A long document runs onto more pages.
        string longText = string.Join("\n\n", Enumerable.Range(1, 80).Select(i => "Paragraph " + i + ": " + string.Concat(Enumerable.Repeat("word ", 40))));
        string many = Encoding.Latin1.GetString(DocumentWriter.ToPdf("Long", longText));
        Assert.That(int.Parse(Regex.Match(many, @"/Count (\d+)").Groups[1].Value), Is.GreaterThan(3));
        Out("Long.pdf", Encoding.Latin1.GetBytes(many));

        // Text the PDF fonts can't show becomes a Word document.
        GeneratedDocument chinese = DocumentWriter.Create("购物清单", "- 牛奶\n- 面包", "pdf");
        Assert.That(chinese.Format, Is.EqualTo("Word"));
        Assert.That(chinese.FileName, Is.EqualTo("购物清单.docx"));
        Assert.That(chinese.Note, Does.Contain("Word"));
        Out(chinese.FileName, chinese.Data);
    }

    [Test]
    public void TheUsersWordsChooseTheFormat()
    {
        Assert.That(DocumentWriter.ChooseFormat("markdown", "Put together a checklist I can print"), Is.EqualTo("pdf"));
        Assert.That(DocumentWriter.ChooseFormat("markdown", "Make me a packing list"), Is.EqualTo("word"));
        Assert.That(DocumentWriter.ChooseFormat("word", "Export this as Markdown"), Is.EqualTo("markdown"));
        Assert.That(DocumentWriter.ChooseFormat("word", "Save it as a PDF"), Is.EqualTo("pdf"));
        Assert.That(DocumentWriter.ChooseFormat("pdf", "write that up as a Word doc"), Is.EqualTo("word"));
        Assert.That(DocumentWriter.ChooseFormat("pdf", null), Is.EqualTo("pdf"));
        Assert.That(DocumentWriter.ChooseFormat(null, null), Is.EqualTo("word"));
    }

    [Test]
    public async Task ToolHandsTheFileToTheApp()
    {
        GeneratedDocument saved = null;
        AIFunction tool = AgentTools.CreateDocument((document, token) =>
        {
            saved = document;
            return Task.FromResult(AgentTools.DocumentCreatedResult(document));
        }, () => "Write my notes up as Markdown");
        AIFunctionArguments args = new AIFunctionArguments { ["title"] = "Notes: plan/ideas?", ["content"] = "- one\n- two", ["format"] = "markdown" };
        string result = (await tool.InvokeAsync(args))?.ToString();
        Assert.That(saved.FileName, Is.EqualTo("Notes plan ideas.md"));
        Assert.That(Encoding.UTF8.GetString(saved.Data), Is.EqualTo("# Notes: plan/ideas?\n\n- one\n- two\n"));
        Assert.That(result, Does.Contain("Created \"Notes plan ideas.md\" (Markdown, 34 bytes)"));
        Assert.That(AgentTools.Describe(new FunctionCallContent("1", "create_document", new Dictionary<string, object> { ["title"] = "Plan" })),
            Is.EqualTo("Writing “Plan”"));
        args = new AIFunctionArguments { ["title"] = "Empty", ["content"] = " " };
        Assert.That((await tool.InvokeAsync(args))?.ToString(), Does.Contain("No content"));
    }
}
