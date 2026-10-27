//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>
    /// The create_document tool group (with record_meeting, whose notes are a document too): its name, summary and words,
    /// and the instructions that go with the tools.
    /// </summary>
    public static class ChatWrittenDocuments
    {
        /// <summary>The tool group's name.</summary>
        public const string GroupName = "write_document";

        /// <summary>The tool group's summary, for the planner.</summary>
        public const string GroupSummary = "write a document file (Word, PDF or Markdown) for the user to open or share, or record a meeting and write its notes";

        /// <summary>The words that bring the tool group.</summary>
        public static readonly string[] GroupKeywords =
        {
            "document", "word file", "word doc", "a doc", "docx", "pdf", "markdown", "write up", "write it up", "write that up",
            "write this up", "as a file", "into a file", "in a file", "save it as", "save that as", "save this as", "export",
            "printable", "print out", "handout", "report", "letter", "essay", "cv", "resume", "itinerary",
            "meeting", "the minutes", "transcri", "take notes", "taking notes", "note taking", "notes of", "notes for", "notes from",
            "record my", "record the", "record this", "record our", "record a", "recording", "lecture", "class notes", "interview",
            "dictat", "write down what"
        };

        /// <summary>The instructions that go with create_document.</summary>
        public const string Instructions = "When the user asks for a document or a file (Word, PDF, Markdown), write the whole " +
            "content into create_document's content in Markdown - not into your reply - then say in one short sentence that it is ready.";

        /// <summary>The instructions that go with record_meeting.</summary>
        public const string MeetingInstructions =
            "To record a meeting, lecture or conversation and take notes of it, call record_meeting: it records until the user presses Stop, " +
            "then returns the notes.";

        /// <summary>The kind of a reply's saved attachment that is a document it wrote (never given back to the model).</summary>
        public const string AttachmentKind = "document";
    }
}
