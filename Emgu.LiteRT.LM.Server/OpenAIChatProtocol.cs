//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Server
{
    /// <summary>A request the server can't take: its HTTP status and OpenAI's error code.</summary>
    public sealed class ChatProtocolException : Exception
    {
        /// <summary>A bad request.</summary>
        /// <param name="message">What is wrong</param>
        /// <param name="status">The HTTP status (400)</param>
        /// <param name="code">OpenAI's error code, or null</param>
        public ChatProtocolException(string message, int status = 400, string code = null) : base(message)
        {
            Status = status;
            Code = code;
        }

        /// <summary>The HTTP status.</summary>
        public int Status { get; }

        /// <summary>OpenAI's error code, or null.</summary>
        public string Code { get; }
    }

    /// <summary>A chat completion request read: the conversation, the options, and whether to stream.</summary>
    public sealed class ChatCompletionRequest
    {
        /// <summary>The conversation.</summary>
        public IList<ChatMessage> Messages { get; } = new List<ChatMessage>();

        /// <summary>The options: instructions, tools, thinking, the reply's length.</summary>
        public ChatOptions Options { get; } = new ChatOptions();

        /// <summary>The model asked for.</summary>
        public string Model { get; set; }

        /// <summary>Whether the reply streams (server-sent events).</summary>
        public bool Stream { get; set; }
    }

    /// <summary>An embeddings request read: the texts, the options, and how the vectors are written.</summary>
    public sealed class EmbeddingRequest
    {
        /// <summary>The texts.</summary>
        public IList<string> Inputs { get; } = new List<string>();

        /// <summary>The options: the vectors' length, and "input_type" ("query" or "document") as an additional property.</summary>
        public EmbeddingGenerationOptions Options { get; } = new EmbeddingGenerationOptions();

        /// <summary>The model asked for.</summary>
        public string Model { get; set; }

        /// <summary>Whether the vectors are written as base64 (little-endian floats) rather than JSON numbers.</summary>
        public bool Base64 { get; set; }
    }

    /// <summary>
    /// OpenAI's chat completions wire format and Microsoft.Extensions.AI's types, both ways, as <see cref="ChatServer"/>
    /// serves them. The request becomes the conversation as the client keeps it, so a model that keeps its open conversation
    /// when a request only adds to it (Emgu.LiteRT.LM's LiteRtLmChatClient) does: an assistant message's text comes before its
    /// tool calls, with their ids, and the tool messages of one round are one message.
    /// </summary>
    public static class OpenAIChatProtocol
    {
        /// <summary>Read a POST /v1/chat/completions body.</summary>
        /// <param name="json">The body</param>
        /// <exception cref="ChatProtocolException">It isn't a chat completion request this server takes</exception>
        public static ChatCompletionRequest ParseRequest(string json)
        {
            JsonDocument document;
            try { document = JsonDocument.Parse(json); }
            catch (JsonException ex) { throw new ChatProtocolException("The body isn't JSON: " + ex.Message); }
            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new ChatProtocolException("The body must be a JSON object.");
                var request = new ChatCompletionRequest
                {
                    Model = String(root, "model"),
                    Stream = root.TryGetProperty("stream", out JsonElement stream) && stream.ValueKind == JsonValueKind.True
                };
                if (!root.TryGetProperty("messages", out JsonElement messages) || messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() == 0)
                    throw new ChatProtocolException("'messages' must be a non-empty array.");
                var instructions = new List<string>();
                foreach (JsonElement message in messages.EnumerateArray())
                    AddMessage(request, instructions, message);
                if (instructions.Count > 0)
                    request.Options.Instructions = string.Join("\n\n", instructions);
                ReadOptions(root, request.Options);
                return request;
            }
        }

        private static void AddMessage(ChatCompletionRequest request, List<string> instructions, JsonElement message)
        {
            string role = String(message, "role");
            switch (role)
            {
                case "system":
                case "developer":
                    string text = Text(message);
                    if (!string.IsNullOrEmpty(text))
                        instructions.Add(text);
                    return;
                case "user":
                    request.Messages.Add(new ChatMessage(ChatRole.User, Contents(message)));
                    return;
                case "assistant":
                    var contents = new List<AIContent>();
                    string reply = Text(message);
                    if (!string.IsNullOrEmpty(reply))
                        contents.Add(new TextContent(reply));
                    if (message.TryGetProperty("tool_calls", out JsonElement calls) && calls.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement call in calls.EnumerateArray())
                            contents.Add(ToolCall(call));
                    request.Messages.Add(new ChatMessage(ChatRole.Assistant, contents));
                    return;
                case "tool":
                    string callId = String(message, "tool_call_id");
                    if (string.IsNullOrEmpty(callId))
                        throw new ChatProtocolException("A tool message needs 'tool_call_id'.");
                    var result = new FunctionResultContent(callId, ToolResult(Text(message)));
                    // A round's results are one message, as the client that called the tools has them.
                    ChatMessage last = request.Messages.LastOrDefault();
                    if (last != null && last.Role == ChatRole.Tool)
                        last.Contents.Add(result);
                    else
                        request.Messages.Add(new ChatMessage(ChatRole.Tool, new List<AIContent> { result }));
                    return;
                default:
                    throw new ChatProtocolException("Unknown message role '" + role + "'.");
            }
        }

        private static FunctionCallContent ToolCall(JsonElement call)
        {
            string id = String(call, "id");
            if (!call.TryGetProperty("function", out JsonElement function) || string.IsNullOrEmpty(id))
                throw new ChatProtocolException("A tool call needs 'id' and 'function'.");
            string name = String(function, "name");
            string arguments = String(function, "arguments");
            var values = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                try
                {
                    using (JsonDocument parsed = JsonDocument.Parse(arguments))
                    {
                        if (parsed.RootElement.ValueKind == JsonValueKind.Object)
                            foreach (JsonProperty property in parsed.RootElement.EnumerateObject())
                                values[property.Name] = property.Value.Clone();
                    }
                }
                catch (JsonException) { throw new ChatProtocolException("The arguments of tool call '" + id + "' aren't JSON."); }
            }
            return new FunctionCallContent(id, name, values);
        }

        // A message's content: a string, or parts (text, image_url data URIs, input_audio).
        private static List<AIContent> Contents(JsonElement message)
        {
            var contents = new List<AIContent>();
            if (!message.TryGetProperty("content", out JsonElement content) || content.ValueKind == JsonValueKind.Null)
                return contents;
            if (content.ValueKind == JsonValueKind.String)
            {
                contents.Add(new TextContent(content.GetString()));
                return contents;
            }
            if (content.ValueKind != JsonValueKind.Array)
                throw new ChatProtocolException("'content' must be a string or an array of parts.");
            foreach (JsonElement part in content.EnumerateArray())
            {
                switch (String(part, "type"))
                {
                    case "text":
                        contents.Add(new TextContent(String(part, "text") ?? string.Empty));
                        break;
                    case "image_url":
                        string url = part.TryGetProperty("image_url", out JsonElement image)
                            ? image.ValueKind == JsonValueKind.String ? image.GetString() : String(image, "url") : null;
                        if (url == null || !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                            throw new ChatProtocolException("Only images sent as data URIs are taken (this server doesn't fetch URLs).");
                        try { contents.Add(new DataContent(url)); }
                        catch (Exception ex) when (ex is FormatException || ex is ArgumentException) { throw new ChatProtocolException("A bad image data URI: " + ex.Message); }
                        break;
                    case "input_audio":
                        if (!part.TryGetProperty("input_audio", out JsonElement audio))
                            throw new ChatProtocolException("'input_audio' is missing.");
                        string format = String(audio, "format") ?? "wav";
                        byte[] data;
                        try { data = Convert.FromBase64String(String(audio, "data") ?? string.Empty); }
                        catch (FormatException) { throw new ChatProtocolException("The audio isn't base64."); }
                        contents.Add(new DataContent(data, format == "mp3" ? "audio/mpeg" : "audio/" + format));
                        break;
                    default:
                        throw new ChatProtocolException("Unknown content part '" + String(part, "type") + "'.");
                }
            }
            return contents;
        }

        // A tool's result as the client's function returned it: the client sends it serialized ("7391" with its quotes,
        // {"temp": 12}), so JSON is read back as a JsonElement, as an AIFunction's result is when the model is called
        // directly; other text (an error message, "12:00") stays text.
        private static object ToolResult(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;
            try
            {
                using JsonDocument document = JsonDocument.Parse(text);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return text;
            }
        }

        // A message's text: the string, or its text parts joined.
        private static string Text(JsonElement message)
        {
            if (!message.TryGetProperty("content", out JsonElement content) || content.ValueKind == JsonValueKind.Null)
                return null;
            if (content.ValueKind == JsonValueKind.String)
                return content.GetString();
            if (content.ValueKind == JsonValueKind.Array)
                return string.Concat(content.EnumerateArray().Where(p => String(p, "type") == "text").Select(p => String(p, "text")));
            return content.GetRawText();
        }

        private static void ReadOptions(JsonElement root, ChatOptions options)
        {
            if (root.TryGetProperty("tools", out JsonElement tools) && tools.ValueKind == JsonValueKind.Array)
            {
                var list = new List<AITool>();
                foreach (JsonElement tool in tools.EnumerateArray())
                {
                    if (String(tool, "type") != "function" || !tool.TryGetProperty("function", out JsonElement function))
                        throw new ChatProtocolException("Only function tools are taken.");
                    string name = String(function, "name");
                    if (string.IsNullOrEmpty(name))
                        throw new ChatProtocolException("A tool needs a name.");
                    JsonElement schema = function.TryGetProperty("parameters", out JsonElement parameters) && parameters.ValueKind == JsonValueKind.Object
                        ? parameters.Clone()
                        : JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}").RootElement.Clone();
                    list.Add(AIFunctionFactory.CreateDeclaration(name, String(function, "description"), schema));
                }
                if (list.Count > 0)
                    options.Tools = list;
            }
            if (root.TryGetProperty("tool_choice", out JsonElement choice))
            {
                string value = choice.ValueKind == JsonValueKind.String ? choice.GetString() : null;
                options.ToolMode = value == "none" ? ChatToolMode.None : value == "required" ? ChatToolMode.RequireAny : ChatToolMode.Auto;
            }
            string effort = String(root, "reasoning_effort");
            if (effort != null)
                options.Reasoning = new ReasoningOptions
                {
                    Effort = effort == "none" || effort == "minimal" ? ReasoningEffort.None
                        : effort == "low" ? ReasoningEffort.Low : effort == "high" ? ReasoningEffort.High : ReasoningEffort.Medium
                };
            int? max = Int(root, "max_completion_tokens") ?? Int(root, "max_tokens");
            if (max > 0)
                options.MaxOutputTokens = max;
        }

        private static string String(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;

        private static int? Int(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)
                ? number : (int?)null;

        // ---------- Replies ----------

        /// <summary>
        /// The chunk for a streamed update - its text (content), thinking (reasoning_content) and tool calls (each whole, with
        /// its index among the reply's calls) - or null when it has none of them.
        /// </summary>
        /// <param name="update">The update</param>
        /// <param name="id">The reply's id ("chatcmpl-...")</param>
        /// <param name="model">The model's id</param>
        /// <param name="created">When the reply started (Unix seconds)</param>
        /// <param name="first">Whether it is the first chunk (it carries the role)</param>
        /// <param name="toolIndex">The reply's tool calls so far; counted up</param>
        public static string Chunk(ChatResponseUpdate update, string id, string model, long created, bool first, ref int toolIndex)
        {
            string text = string.Concat(update.Contents.OfType<TextContent>().Select(c => c.Text));
            string reasoning = string.Concat(update.Contents.OfType<TextReasoningContent>().Select(c => c.Text));
            List<FunctionCallContent> calls = update.Contents.OfType<FunctionCallContent>().ToList();
            if (text.Length == 0 && reasoning.Length == 0 && calls.Count == 0 && !first)
                return null;
            int index = toolIndex;
            string json = Write(w =>
            {
                StartChunk(w, id, model, created);
                w.WriteStartObject("delta");
                if (first)
                    w.WriteString("role", "assistant");
                if (reasoning.Length > 0)
                    w.WriteString("reasoning_content", reasoning);
                if (text.Length > 0)
                    w.WriteString("content", text);
                if (calls.Count > 0)
                {
                    w.WriteStartArray("tool_calls");
                    foreach (FunctionCallContent call in calls)
                        WriteToolCall(w, call, index++);
                    w.WriteEndArray();
                }
                w.WriteEndObject();
                w.WriteNull("finish_reason");
                w.WriteEndObject();
                w.WriteEndArray();
                w.WriteEndObject();
            });
            toolIndex = index;
            return json;
        }

        /// <summary>The last chunk of a streamed reply: its finish reason.</summary>
        /// <param name="id">The reply's id</param>
        /// <param name="model">The model's id</param>
        /// <param name="created">When the reply started (Unix seconds)</param>
        /// <param name="finish">Why it ended</param>
        public static string FinalChunk(string id, string model, long created, ChatFinishReason? finish) => Write(w =>
        {
            StartChunk(w, id, model, created);
            w.WriteStartObject("delta");
            w.WriteEndObject();
            w.WriteString("finish_reason", FinishReason(finish));
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        });

        private static void StartChunk(Utf8JsonWriter w, string id, string model, long created)
        {
            w.WriteStartObject();
            w.WriteString("id", id);
            w.WriteString("object", "chat.completion.chunk");
            w.WriteNumber("created", created);
            w.WriteString("model", model);
            w.WriteStartArray("choices");
            w.WriteStartObject();
            w.WriteNumber("index", 0);
        }

        /// <summary>A whole (non-streamed) reply from its updates.</summary>
        /// <param name="updates">The reply's updates</param>
        /// <param name="id">The reply's id</param>
        /// <param name="model">The model's id</param>
        /// <param name="created">When the reply started (Unix seconds)</param>
        public static string Completion(IEnumerable<ChatResponseUpdate> updates, string id, string model, long created)
        {
            List<ChatResponseUpdate> list = updates.ToList();
            string text = string.Concat(list.SelectMany(u => u.Contents).OfType<TextContent>().Select(c => c.Text));
            string reasoning = string.Concat(list.SelectMany(u => u.Contents).OfType<TextReasoningContent>().Select(c => c.Text));
            List<FunctionCallContent> calls = list.SelectMany(u => u.Contents).OfType<FunctionCallContent>().ToList();
            ChatFinishReason? finish = list.Select(u => u.FinishReason).LastOrDefault(f => f != null);
            return Write(w =>
            {
                w.WriteStartObject();
                w.WriteString("id", id);
                w.WriteString("object", "chat.completion");
                w.WriteNumber("created", created);
                w.WriteString("model", model);
                w.WriteStartArray("choices");
                w.WriteStartObject();
                w.WriteNumber("index", 0);
                w.WriteStartObject("message");
                w.WriteString("role", "assistant");
                if (calls.Count > 0 && text.Length == 0)
                    w.WriteNull("content");
                else
                    w.WriteString("content", text);
                if (reasoning.Length > 0)
                    w.WriteString("reasoning_content", reasoning);
                if (calls.Count > 0)
                {
                    w.WriteStartArray("tool_calls");
                    int index = 0;
                    foreach (FunctionCallContent call in calls)
                        WriteToolCall(w, call, index++, withIndex: false);
                    w.WriteEndArray();
                }
                w.WriteEndObject();
                w.WriteString("finish_reason", FinishReason(finish ?? (calls.Count > 0 ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop)));
                w.WriteEndObject();
                w.WriteEndArray();
                w.WriteEndObject();
            });
        }

        private static void WriteToolCall(Utf8JsonWriter w, FunctionCallContent call, int index, bool withIndex = true)
        {
            w.WriteStartObject();
            if (withIndex)
                w.WriteNumber("index", index);
            w.WriteString("id", call.CallId);
            w.WriteString("type", "function");
            w.WriteStartObject("function");
            w.WriteString("name", call.Name);
            w.WriteString("arguments", JsonSerializer.Serialize(call.Arguments ?? new Dictionary<string, object>()));
            w.WriteEndObject();
            w.WriteEndObject();
        }

        private static string FinishReason(ChatFinishReason? finish) =>
            finish == ChatFinishReason.ToolCalls ? "tool_calls" : finish == ChatFinishReason.Length ? "length"
            : finish == ChatFinishReason.ContentFilter ? "content_filter" : "stop";

        /// <summary>
        /// GET /v1/models: the model served, with OpenAI's fields and this server's context window and capabilities, then the
        /// search model ("type": "embeddings", its "dimensions") if one is served.
        /// </summary>
        /// <param name="model">The model, or null for none</param>
        /// <param name="created">When the server started (Unix seconds)</param>
        /// <param name="embeddings">The search model, or null for none</param>
        public static string Models(ServedModel model, long created, ServedModel embeddings = null) => Write(w =>
        {
            w.WriteStartObject();
            w.WriteString("object", "list");
            w.WriteStartArray("data");
            if (model != null)
            {
                w.WriteStartObject();
                w.WriteString("id", model.Id);
                w.WriteString("object", "model");
                w.WriteNumber("created", created);
                w.WriteString("owned_by", "emgu-litert");
                w.WriteString("display_name", model.DisplayName);
                if (model.ContextLength > 0)
                    w.WriteNumber("context_length", model.ContextLength);
                w.WriteStartObject("capabilities");
                w.WriteBoolean("vision", model.Vision);
                w.WriteBoolean("audio", model.Audio);
                w.WriteBoolean("tools", model.Tools);
                w.WriteBoolean("thinking", model.Thinking);
                w.WriteEndObject();
                w.WriteEndObject();
            }
            if (embeddings != null)
            {
                w.WriteStartObject();
                w.WriteString("id", embeddings.Id);
                w.WriteString("object", "model");
                w.WriteNumber("created", created);
                w.WriteString("owned_by", "emgu-litert");
                w.WriteString("display_name", embeddings.DisplayName);
                w.WriteString("type", "embeddings");
                if (embeddings.Dimensions > 0)
                    w.WriteNumber("dimensions", embeddings.Dimensions);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });

        /// <summary>LM Studio's GET /api/v0/models for the model served, so clients written for LM Studio read it too.</summary>
        /// <param name="model">The model, or null for none</param>
        /// <param name="embeddings">The search model, or null for none</param>
        public static string LmStudioModels(ServedModel model, ServedModel embeddings = null) => Write(w =>
        {
            w.WriteStartObject();
            w.WriteString("object", "list");
            w.WriteStartArray("data");
            if (model != null)
            {
                w.WriteStartObject();
                w.WriteString("id", model.Id);
                w.WriteString("object", "model");
                w.WriteString("type", model.Vision ? "vlm" : "llm");
                w.WriteString("publisher", "emgu-litert");
                w.WriteString("state", "loaded");
                if (model.ContextLength > 0)
                {
                    w.WriteNumber("max_context_length", model.ContextLength);
                    w.WriteNumber("loaded_context_length", model.ContextLength);
                }
                w.WriteEndObject();
            }
            if (embeddings != null)
            {
                w.WriteStartObject();
                w.WriteString("id", embeddings.Id);
                w.WriteString("object", "model");
                w.WriteString("type", "embeddings");
                w.WriteString("publisher", "emgu-litert");
                w.WriteString("state", "loaded");
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });

        /// <summary>The most texts one embeddings request takes.</summary>
        public const int MaxEmbeddingInputs = 256;

        /// <summary>
        /// Read a POST /v1/embeddings body: "input" (a text or an array of texts), "dimensions", "encoding_format" ("float" or
        /// "base64"), and "input_type" ("query" or "document", as Cohere and Voyage name it; also "search_query" /
        /// "search_document"), which OpenAI's API doesn't have: a search model such as EmbeddingGemma embeds a query and a
        /// document with different prefixes.
        /// </summary>
        /// <param name="json">The body</param>
        /// <exception cref="ChatProtocolException">It isn't an embeddings request this server takes</exception>
        public static EmbeddingRequest ParseEmbeddingRequest(string json)
        {
            JsonDocument document;
            try { document = JsonDocument.Parse(json); }
            catch (JsonException ex) { throw new ChatProtocolException("The body isn't JSON: " + ex.Message); }
            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new ChatProtocolException("The body must be a JSON object.");
                var request = new EmbeddingRequest { Model = String(root, "model") };
                if (!root.TryGetProperty("input", out JsonElement input))
                    throw new ChatProtocolException("\"input\" is missing: a text or an array of texts.");
                if (input.ValueKind == JsonValueKind.String)
                    request.Inputs.Add(input.GetString());
                else if (input.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement item in input.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String)
                            throw new ChatProtocolException("Only texts are embedded here (not token arrays).");
                        request.Inputs.Add(item.GetString());
                    }
                else
                    throw new ChatProtocolException("\"input\" must be a text or an array of texts.");
                if (request.Inputs.Count == 0)
                    throw new ChatProtocolException("\"input\" has no texts.");
                if (request.Inputs.Count > MaxEmbeddingInputs)
                    throw new ChatProtocolException("At most " + MaxEmbeddingInputs + " texts in one request.");
                if (root.TryGetProperty("dimensions", out JsonElement dimensions) && dimensions.ValueKind != JsonValueKind.Null)
                {
                    if (!dimensions.TryGetInt32(out int count) || count <= 0)
                        throw new ChatProtocolException("\"dimensions\" must be a positive number.");
                    request.Options.Dimensions = count;
                }
                string format = String(root, "encoding_format");
                if (format != null && format != "float" && format != "base64")
                    throw new ChatProtocolException("\"encoding_format\" is \"float\" or \"base64\".");
                request.Base64 = format == "base64";
                string type = String(root, "input_type");
                if (type != null)
                {
                    type = type == "search_query" ? "query" : type == "search_document" ? "document" : type;
                    if (type != "query" && type != "document")
                        throw new ChatProtocolException("\"input_type\" is \"query\" or \"document\".");
                    request.Options.AdditionalProperties = new AdditionalPropertiesDictionary { ["input_type"] = type };
                }
                return request;
            }
        }

        /// <summary>The reply to POST /v1/embeddings, in OpenAI's shape.</summary>
        /// <param name="embeddings">The vectors, in the order of the texts</param>
        /// <param name="model">The model's id</param>
        /// <param name="base64">Whether to write the vectors as base64 (little-endian floats)</param>
        public static string Embeddings(IReadOnlyList<Embedding<float>> embeddings, string model, bool base64) => Write(w =>
        {
            w.WriteStartObject();
            w.WriteString("object", "list");
            w.WriteStartArray("data");
            for (int i = 0; i < embeddings.Count; i++)
            {
                w.WriteStartObject();
                w.WriteString("object", "embedding");
                w.WriteNumber("index", i);
                ReadOnlySpan<float> vector = embeddings[i].Vector.Span;
                if (base64)
                {
                    byte[] bytes = new byte[vector.Length * sizeof(float)];
                    for (int j = 0; j < vector.Length; j++)
                    {
                        byte[] value = BitConverter.GetBytes(vector[j]);
                        if (!BitConverter.IsLittleEndian)
                            Array.Reverse(value);
                        Buffer.BlockCopy(value, 0, bytes, j * sizeof(float), sizeof(float));
                    }
                    w.WriteString("embedding", Convert.ToBase64String(bytes));
                }
                else
                {
                    w.WriteStartArray("embedding");
                    foreach (float value in vector)
                        w.WriteNumberValue(value);
                    w.WriteEndArray();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("model", model);
            w.WriteStartObject("usage");
            w.WriteNumber("prompt_tokens", 0);
            w.WriteNumber("total_tokens", 0);
            w.WriteEndObject();
            w.WriteEndObject();
        });

        /// <summary>An error in OpenAI's shape.</summary>
        /// <param name="message">What went wrong</param>
        /// <param name="type">The error's type, e.g. "invalid_request_error"</param>
        /// <param name="code">Its code, or null</param>
        public static string Error(string message, string type, string code = null) => Write(w =>
        {
            w.WriteStartObject();
            w.WriteStartObject("error");
            w.WriteString("message", message);
            w.WriteString("type", type);
            if (code == null)
                w.WriteNull("code");
            else
                w.WriteString("code", code);
            w.WriteEndObject();
            w.WriteEndObject();
        });

        internal static string Write(Action<Utf8JsonWriter> write)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                    write(writer);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
