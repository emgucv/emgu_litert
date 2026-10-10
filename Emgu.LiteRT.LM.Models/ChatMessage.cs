//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// One message of a chat history.
    /// </summary>
    public class ChatMessage
    {
        /// <summary>
        /// The role of a message written by the user
        /// </summary>
        public const String UserRole = "user";

        /// <summary>
        /// The role of a message written by the model
        /// </summary>
        public const String AssistantRole = "assistant";

        private static readonly ChatAttachment[] NoAttachments = new ChatAttachment[0];

        /// <summary>
        /// Create a chat message.
        /// </summary>
        /// <param name="role">The role, e.g. UserRole or AssistantRole</param>
        /// <param name="text">The message text</param>
        /// <param name="attachments">The images and audio of the message, or null for none</param>
        public ChatMessage(String role, String text, IEnumerable<ChatAttachment> attachments = null)
        {
            Role = role;
            Text = text;
            List<ChatAttachment> list = new List<ChatAttachment>();
            if (attachments != null)
            {
                foreach (ChatAttachment attachment in attachments)
                {
                    if (attachment == null)
                        throw new ArgumentNullException("attachments", "An attachment is null");
                    list.Add(attachment);
                }
            }
            Attachments = list.Count == 0 ? NoAttachments : list.ToArray();
        }

        /// <summary>
        /// The role, e.g. "user" or "assistant"
        /// </summary>
        public String Role { get; }

        /// <summary>
        /// The message text
        /// </summary>
        public String Text { get; }

        /// <summary>
        /// The images and audio of the message (empty if none)
        /// </summary>
        public IReadOnlyList<ChatAttachment> Attachments { get; }

        /// <summary>
        /// Return the message as "role: text"
        /// </summary>
        /// <returns>The message as "role: text"</returns>
        public override String ToString()
        {
            if (Attachments.Count == 0)
                return String.Format("{0}: {1}", Role, Text);
            return String.Format("{0}: {1} [{2}]", Role, Text, String.Join(", ", Attachments));
        }

        /// <summary>
        /// Write the message in LiteRT-LM's JSON message format:
        /// {"role": ..., "content": [attachments..., {"type": "text", "text": ...}]}
        /// </summary>
        internal void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("role", Role);
            writer.WritePropertyName("content");
            writer.WriteStartArray();
            foreach (ChatAttachment attachment in Attachments)
                attachment.WriteJson(writer);
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", Text ?? String.Empty);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        /// <summary>
        /// Convert the message to LiteRT-LM's JSON message format.
        /// </summary>
        /// <returns>The message as JSON</returns>
        public String ToJson()
        {
            return BuildJson(writer => WriteJson(writer));
        }

        /// <summary>
        /// Convert messages to a JSON array in LiteRT-LM's message format.
        /// </summary>
        /// <param name="messages">The messages</param>
        /// <returns>The messages as a JSON array</returns>
        public static String ToJson(IEnumerable<ChatMessage> messages)
        {
            return BuildJson(writer =>
            {
                writer.WriteStartArray();
                foreach (ChatMessage message in messages)
                    message.WriteJson(writer);
                writer.WriteEndArray();
            });
        }

        private static String BuildJson(Action<Utf8JsonWriter> write)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
                    write(writer);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>
        /// Get the model's reasoning of a message in LiteRT-LM's JSON message format: its "reasoning_content" string,
        /// reported by models that think in a separate channel (e.g. Gemma 4).
        /// </summary>
        /// <param name="messageJson">The message as JSON</param>
        /// <returns>The reasoning, or an empty string if the message has none</returns>
        public static String GetReasoning(String messageJson)
        {
            if (String.IsNullOrEmpty(messageJson))
                return String.Empty;
            using (JsonDocument document = JsonDocument.Parse(messageJson))
            {
                JsonElement reasoning;
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("reasoning_content", out reasoning)
                    && reasoning.ValueKind == JsonValueKind.String)
                    return reasoning.GetString();
                return String.Empty;
            }
        }

        /// <summary>
        /// Get the text of a message in LiteRT-LM's JSON message format: its "content" string, or the concatenated
        /// "text" of its content parts of type "text".
        /// </summary>
        /// <param name="messageJson">The message as JSON</param>
        /// <returns>The message text, or an empty string if it has none</returns>
        public static String GetText(String messageJson)
        {
            if (String.IsNullOrEmpty(messageJson))
                return String.Empty;
            using (JsonDocument document = JsonDocument.Parse(messageJson))
            {
                JsonElement content;
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("content", out content))
                    return String.Empty;
                if (content.ValueKind == JsonValueKind.String)
                    return content.GetString();
                if (content.ValueKind != JsonValueKind.Array)
                    return String.Empty;
                StringBuilder text = new StringBuilder();
                foreach (JsonElement part in content.EnumerateArray())
                {
                    JsonElement type, partText;
                    if (part.ValueKind == JsonValueKind.Object
                        && part.TryGetProperty("type", out type) && type.ValueKind == JsonValueKind.String && type.GetString() == "text"
                        && part.TryGetProperty("text", out partText) && partText.ValueKind == JsonValueKind.String)
                        text.Append(partText.GetString());
                }
                return text.ToString();
            }
        }
    }

    /// <summary>
    /// Builds the generated text of a reply from the messages (or streamed chunks) LiteRT-LM returns. Models whose
    /// chat template uses a "thought" channel (e.g. Gemma 4) report their thinking in a separate "reasoning_content"
    /// field instead of in the text; it is written here as a leading &lt;think&gt;...&lt;/think&gt; block, the same
    /// way models like Qwen3 emit it, so ChatReply and streaming consumers treat both alike.
    /// </summary>
    internal class ReplyTextBuilder
    {
        private readonly StringBuilder _text = new StringBuilder();
        private bool _thinkingOpen;

        /// <summary>
        /// The generated text so far
        /// </summary>
        public String Text
        {
            get { return _text.ToString(); }
        }

        /// <summary>
        /// Add a reply message or streamed chunk.
        /// </summary>
        /// <param name="messageJson">The message or chunk as JSON</param>
        /// <returns>The text this adds to the generated text</returns>
        public String Add(String messageJson)
        {
            String reasoning = ChatMessage.GetReasoning(messageJson);
            String text = ChatMessage.GetText(messageJson);
            StringBuilder piece = new StringBuilder();
            if (reasoning.Length > 0)
            {
                if (!_thinkingOpen)
                {
                    piece.Append("<think>");
                    _thinkingOpen = true;
                }
                piece.Append(reasoning);
            }
            if (text.Length > 0)
            {
                if (_thinkingOpen)
                {
                    piece.Append("</think>");
                    _thinkingOpen = false;
                }
                piece.Append(text);
            }
            _text.Append(piece);
            return piece.ToString();
        }
    }

    /// <summary>
    /// The model's reply to a chat message.
    /// </summary>
    public class ChatReply
    {
        private const String ThinkStartTag = "<think>";
        private const String ThinkEndTag = "</think>";

        /// <summary>
        /// Create a chat reply from the generated text, splitting off a leading &lt;think&gt;...&lt;/think&gt; block.
        /// </summary>
        /// <param name="fullText">The generated text</param>
        /// <param name="json">The reply message as JSON, or null</param>
        public ChatReply(String fullText, String json = null)
        {
            FullText = fullText ?? String.Empty;
            Json = json;
            String trimmed = FullText.TrimStart();
            if (trimmed.StartsWith(ThinkStartTag, StringComparison.Ordinal))
            {
                int end = trimmed.IndexOf(ThinkEndTag, StringComparison.Ordinal);
                if (end < 0)
                {
                    // The output ended (e.g. at the token limit) while still thinking.
                    Thinking = trimmed.Substring(ThinkStartTag.Length).Trim();
                    Text = String.Empty;
                }
                else
                {
                    Thinking = trimmed.Substring(ThinkStartTag.Length, end - ThinkStartTag.Length).Trim();
                    Text = trimmed.Substring(end + ThinkEndTag.Length).Trim();
                }
            }
            else
            {
                Thinking = String.Empty;
                Text = FullText.Trim();
            }
        }

        /// <summary>
        /// The answer, without the model's thinking
        /// </summary>
        public String Text { get; }

        /// <summary>
        /// The model's thinking (the content of a leading &lt;think&gt;...&lt;/think&gt; block), or an empty string
        /// </summary>
        public String Thinking { get; }

        /// <summary>
        /// The whole generated text, including any thinking
        /// </summary>
        public String FullText { get; }

        /// <summary>
        /// The reply message as JSON in LiteRT-LM's message format, or null for a streamed reply
        /// </summary>
        public String Json { get; }

        /// <summary>
        /// Return the answer text
        /// </summary>
        /// <returns>The answer text</returns>
        public override String ToString()
        {
            return Text;
        }
    }
}
