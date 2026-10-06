//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// A LiteRT-LM conversation (LiteRtLmConversation): chat on an Engine, with messages exchanged as JSON in
    /// LiteRT-LM's message format, e.g. {"role": "user", "content": [{"type": "text", "text": "Hello"}]}. The
    /// conversation keeps the message history, applies the model's prompt template, and handles tool calls and
    /// thinking.
    /// </summary>
    /// <remarks>Wait for (or cancel) a streaming call to complete before disposing the conversation.</remarks>
    public class Conversation : Emgu.LiteRT.Util.UnmanagedObject
    {
        // Keeps the engine alive while this conversation exists.
        private readonly Engine _engine;

        internal Conversation(Engine engine, ConversationConfig config)
        {
            _engine = engine;
            _ptr = LiteRtLmInvoke.CheckPtr(
                LiteRtLmInvoke.litert_lm_conversation_create(engine, config),
                "litert_lm_conversation_create");
        }

        private Conversation(Engine engine, IntPtr ptr)
        {
            _engine = engine;
            _ptr = ptr;
        }

        /// <summary>
        /// The engine this conversation was created from
        /// </summary>
        public Engine Engine
        {
            get { return _engine; }
        }

        /// <summary>
        /// Create a JSON user message with a single text part, e.g.
        /// {"role": "user", "content": [{"type": "text", "text": "Hello"}]}.
        /// </summary>
        /// <param name="text">The message text</param>
        /// <param name="role">The message role</param>
        /// <returns>The message as JSON</returns>
        public static String CreateTextMessage(String text, String role = "user")
        {
            return String.Format(
                "{{\"role\": {0}, \"content\": [{{\"type\": \"text\", \"text\": {1}}}]}}",
                ToJsonString(role),
                ToJsonString(text ?? String.Empty));
        }

        /// <summary>
        /// Send a message and wait for the model's reply.
        /// </summary>
        /// <param name="messageJson">The message, as JSON (see CreateTextMessage)</param>
        /// <param name="optionalArgs">Per-message options, or null</param>
        /// <param name="extraContextJson">Extra context for the prompt template, as a JSON object, or null</param>
        /// <returns>The reply message, as JSON</returns>
        public String SendMessage(String messageJson, ConversationOptionalArgs optionalArgs = null, String extraContextJson = null)
        {
            if (messageJson == null)
                throw new ArgumentNullException("messageJson");
            IntPtr response = LiteRtLmInvoke.CheckPtr(
                LiteRtLmInvoke.litert_lm_conversation_send_message(
                    _ptr, LiteRtLmInvoke.ToUtf8(messageJson), LiteRtLmInvoke.ToUtf8(extraContextJson), optionalArgs),
                "litert_lm_conversation_send_message");
            try
            {
                return LiteRtLmInvoke.PtrToStringUtf8(LiteRtLmInvoke.litert_lm_json_response_get_string(response));
            }
            finally
            {
                LiteRtLmInvoke.litert_lm_json_response_delete(response);
            }
        }

        /// <summary>
        /// Send a message and stream the model's reply as it is generated. The arguments are copied, so the
        /// optional arguments can be disposed once this method returns.
        /// </summary>
        /// <param name="messageJson">The message, as JSON (see CreateTextMessage)</param>
        /// <param name="onChunk">Called with each piece of the reply - a partial message, as JSON - on a LiteRT-LM
        /// background thread. If it throws, generation is cancelled and the task faults with that exception. May be
        /// null.</param>
        /// <param name="optionalArgs">Per-message options, or null</param>
        /// <param name="extraContextJson">Extra context for the prompt template, as a JSON object, or null</param>
        /// <param name="cancellationToken">Cancels the generation; the task is then cancelled (see Cancel)</param>
        /// <returns>A task that completes with all the partial messages, as JSON, in order</returns>
        public Task<String[]> SendMessageStreamAsync(
            String messageJson,
            Action<String> onChunk = null,
            ConversationOptionalArgs optionalArgs = null,
            String extraContextJson = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (messageJson == null)
                throw new ArgumentNullException("messageJson");
            byte[] message = LiteRtLmInvoke.ToUtf8(messageJson);
            byte[] extraContext = LiteRtLmInvoke.ToUtf8(extraContextJson);
            Task<String[]> task = StreamOperation.Start(
                this,
                (callback, callbackData) =>
                    LiteRtLmInvoke.litert_lm_conversation_send_message_stream(
                        _ptr, message, extraContext, optionalArgs, callback, callbackData),
                "litert_lm_conversation_send_message_stream",
                onChunk,
                Cancel,
                cancellationToken);
            GC.KeepAlive(optionalArgs);
            return task;
        }

        /// <summary>
        /// Create a copy of the conversation, including its message history.
        /// </summary>
        /// <returns>The copy</returns>
        public Conversation Clone()
        {
            return new Conversation(
                _engine,
                LiteRtLmInvoke.CheckPtr(LiteRtLmInvoke.litert_lm_conversation_clone(_ptr), "litert_lm_conversation_clone"));
        }

        /// <summary>
        /// Render a message with the prompt template, as the model would see it. Not needed to send messages.
        /// </summary>
        /// <param name="messageJson">The message, as JSON</param>
        /// <returns>The rendered prompt text</returns>
        public String RenderMessageToString(String messageJson)
        {
            return LiteRtLmInvoke.PtrToStringUtf8(LiteRtLmInvoke.CheckPtr(
                LiteRtLmInvoke.litert_lm_conversation_render_message_to_string(_ptr, LiteRtLmInvoke.ToUtf8(messageJson)),
                "litert_lm_conversation_render_message_to_string"));
        }

        /// <summary>
        /// Render the conversation's preface (system message, tools and initial messages) with the prompt template.
        /// </summary>
        /// <returns>The rendered preface text</returns>
        public String RenderPrefaceToString()
        {
            return LiteRtLmInvoke.PtrToStringUtf8(LiteRtLmInvoke.CheckPtr(
                LiteRtLmInvoke.litert_lm_conversation_render_preface_to_string(_ptr),
                "litert_lm_conversation_render_preface_to_string"));
        }

        /// <summary>
        /// Cancel the generation in progress. LiteRT-LM doesn't support reusing a conversation after cancelling: create a new
        /// one for further generation.
        /// </summary>
        public void Cancel()
        {
            if (_ptr != IntPtr.Zero)
                LiteRtLmInvoke.litert_lm_conversation_cancel_process(_ptr);
        }

        /// <summary>
        /// The number of tokens in the conversation so far
        /// </summary>
        public int TokenCount
        {
            get { return LiteRtLmInvoke.litert_lm_conversation_get_token_count(_ptr); }
        }

        /// <summary>
        /// Get the benchmark timings of the conversation. Requires EngineSettings.EnableBenchmark.
        /// </summary>
        /// <returns>The benchmark timings, or null if not available</returns>
        public BenchmarkInfo GetBenchmarkInfo()
        {
            IntPtr info = LiteRtLmInvoke.litert_lm_conversation_get_benchmark_info(_ptr);
            return info == IntPtr.Zero ? null : BenchmarkInfo.FromNative(info);
        }

        /// <summary>
        /// Release the unmanaged conversation
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_conversation_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Quote and escape a string as a JSON string literal.
        /// </summary>
        internal static String ToJsonString(String value)
        {
            StringBuilder builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }
    }

    public static partial class LiteRtLmInvoke
    {
        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_conversation_create(IntPtr engine, IntPtr config);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_delete(IntPtr conversation);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_conversation_clone(IntPtr conversation);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_conversation_send_message(
            IntPtr conversation,
            byte[] messageJson,
            byte[] extraContext,
            IntPtr optionalArgs);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_json_response_delete(IntPtr response);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_json_response_get_string(IntPtr response);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_conversation_send_message_stream(
            IntPtr conversation,
            byte[] messageJson,
            byte[] extraContext,
            IntPtr optionalArgs,
            LiteRtLmStreamCallback callback,
            IntPtr callbackData);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_conversation_render_message_to_string(IntPtr conversation, byte[] messageJson);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_conversation_render_preface_to_string(IntPtr conversation);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_cancel_process(IntPtr conversation);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_conversation_get_benchmark_info(IntPtr conversation);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_conversation_get_token_count(IntPtr conversation);
    }
}
