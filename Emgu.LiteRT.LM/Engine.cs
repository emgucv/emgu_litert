//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// A LiteRT-LM engine (LiteRtLmEngine): a loaded language model, from which sessions and conversations are
    /// created.
    /// </summary>
    /// <remarks>Dispose the sessions and conversations created from an engine before disposing the engine.</remarks>
    public class Engine : Emgu.LiteRT.Util.UnmanagedObject
    {
        /// <summary>Explicit context capacity copied from the settings at creation; zero means the native default.</summary>
        public int ConfiguredContextTokens { get; private set; }

        /// <summary>
        /// Create an engine, loading the model described by the settings. This can take a while for large models.
        /// The settings are copied, so they can be disposed afterwards.
        /// </summary>
        /// <param name="settings">The engine settings</param>
        public Engine(EngineSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException("settings");
            if (String.Equals(settings.Backend, "gpu", StringComparison.OrdinalIgnoreCase)
                || String.Equals(settings.VisionBackend, "gpu", StringComparison.OrdinalIgnoreCase)
                || String.Equals(settings.AudioBackend, "gpu", StringComparison.OrdinalIgnoreCase))
                Emgu.LiteRT.Util.AcceleratorPlugins.Load();
            LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_engine_create(settings, out _ptr), "litert_lm_engine_create");
            ConfiguredContextTokens = settings.MaxNumTokens;
            SingleThreadedExecution = settings.SingleThreadedExecution;
        }

        /// <summary>Whether the engine was created with EngineSettings.SingleThreadedExecution.</summary>
        public bool SingleThreadedExecution { get; private set; }

        /// <summary>
        /// Create a session, for prompt-level generation.
        /// </summary>
        /// <param name="config">The session configuration, or null for the default configuration</param>
        /// <returns>The session</returns>
        public Session CreateSession(SessionConfig config = null)
        {
            return new Session(this, config);
        }

        /// <summary>
        /// Create a conversation, for chat with JSON messages.
        /// </summary>
        /// <param name="config">The conversation configuration, or null for the default configuration</param>
        /// <returns>The conversation</returns>
        public Conversation CreateConversation(ConversationConfig config = null)
        {
            return new Conversation(this, config);
        }

        /// <summary>
        /// Convert text to token ids with the model's tokenizer.
        /// </summary>
        /// <param name="text">The text</param>
        /// <returns>The token ids</returns>
        public int[] Tokenize(String text)
        {
            LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_engine_tokenize(_ptr, LiteRtLmInvoke.ToUtf8(text ?? String.Empty), out IntPtr result), "litert_lm_engine_tokenize");
            try
            {
                UIntPtr count;
                LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_tokenize_result_get_num_tokens(result, out count), "litert_lm_tokenize_result_get_num_tokens");
                int numTokens = checked((int)count.ToUInt64());
                int[] tokens = new int[numTokens];
                if (numTokens > 0)
                {
                    IntPtr ids;
                    LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_tokenize_result_get_tokens(result, out ids), "litert_lm_tokenize_result_get_tokens");
                    Marshal.Copy(ids, tokens, 0, numTokens);
                }
                return tokens;
            }
            finally
            {
                LiteRtLmInvoke.litert_lm_tokenize_result_delete(result);
            }
        }

        /// <summary>
        /// Convert token ids to text with the model's tokenizer.
        /// </summary>
        /// <param name="tokens">The token ids</param>
        /// <returns>The text</returns>
        public String Detokenize(int[] tokens)
        {
            int[] ids = tokens ?? new int[0];
            LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_engine_detokenize(_ptr, ids, new UIntPtr((uint)ids.Length), out IntPtr result), "litert_lm_engine_detokenize");
            try
            {
                IntPtr text;
                LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_detokenize_result_get_string(result, out text), "litert_lm_detokenize_result_get_string");
                return LiteRtLmInvoke.PtrToStringUtf8(text);
            }
            finally
            {
                LiteRtLmInvoke.litert_lm_detokenize_result_delete(result);
            }
        }

        /// <summary>
        /// The model's start token, or null if the model doesn't define one.
        /// </summary>
        public Token StartToken
        {
            get
            {
                // Ok with NULL when the model has none; the token union is the caller's.
                IntPtr tokenUnion;
                LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_engine_get_start_token(_ptr, out tokenUnion), "litert_lm_engine_get_start_token");
                if (tokenUnion == IntPtr.Zero)
                    return null;
                try
                {
                    return Token.FromTokenUnion(tokenUnion);
                }
                finally
                {
                    LiteRtLmInvoke.litert_lm_token_union_delete(tokenUnion);
                }
            }
        }

        /// <summary>
        /// The model's stop tokens (empty if the model doesn't define any).
        /// </summary>
        public Token[] StopTokens
        {
            get
            {
                IntPtr tokenUnions;
                LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_engine_get_stop_tokens(_ptr, out tokenUnions), "litert_lm_engine_get_stop_tokens");
                if (tokenUnions == IntPtr.Zero)
                    return new Token[0];
                try
                {
                    UIntPtr numTokens;
                    LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_token_unions_get_num_tokens(tokenUnions, out numTokens), "litert_lm_token_unions_get_num_tokens");
                    int count = checked((int)numTokens.ToUInt64());
                    Token[] tokens = new Token[count];
                    for (int i = 0; i < count; i++)
                    {
                        // Borrowed from tokenUnions (since C API 1.0.0): not deleted on its own.
                        IntPtr tokenUnion;
                        LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_token_unions_get_token_at(tokenUnions, new UIntPtr((uint)i), out tokenUnion), "litert_lm_token_unions_get_token_at");
                        tokens[i] = Token.FromTokenUnion(tokenUnion);
                    }
                    return tokens;
                }
                finally
                {
                    LiteRtLmInvoke.litert_lm_token_unions_delete(tokenUnions);
                }
            }
        }

        /// <summary>
        /// Release the unmanaged engine
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_engine_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// A special token of the model (LiteRtLmTokenUnion): given either as a string or as token ids.
    /// </summary>
    public class Token
    {
        /// <summary>
        /// The token as a string, or null if it is given as token ids
        /// </summary>
        public String Text { get; private set; }

        /// <summary>
        /// The token ids, or null if the token is given as a string
        /// </summary>
        public int[] Ids { get; private set; }

        /// <summary>
        /// Return the token as a string
        /// </summary>
        /// <returns>The token string, or its ids</returns>
        public override String ToString()
        {
            return Text ?? (Ids == null ? String.Empty : String.Format("[{0}]", String.Join(", ", Ids)));
        }

        /// <summary>
        /// Read a LiteRtLmTokenUnion into a Token. The token union isn't deleted: the caller owns it (a start token)
        /// or it is borrowed (a stop token).
        /// </summary>
        internal static Token FromTokenUnion(IntPtr tokenUnion)
        {
            if (tokenUnion == IntPtr.Zero)
                return null;
            Token token = new Token();
            int type;
            LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_token_union_get_type(tokenUnion, out type), "litert_lm_token_union_get_type");
            if (type == 0)
            {
                IntPtr text;
                LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_token_union_get_string(tokenUnion, out text), "litert_lm_token_union_get_string");
                token.Text = LiteRtLmInvoke.PtrToStringUtf8(text);
            }
            else
            {
                IntPtr ids;
                UIntPtr numIds;
                LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_token_union_get_ids(tokenUnion, out ids, out numIds), "litert_lm_token_union_get_ids");
                int count = checked((int)numIds.ToUInt64());
                token.Ids = new int[count];
                if (count > 0)
                    Marshal.Copy(ids, token.Ids, 0, count);
            }
            return token;
        }
    }

    public static partial class LiteRtLmInvoke
    {
        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_engine_create(IntPtr settings, out IntPtr outResult);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_delete(IntPtr engine);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_engine_tokenize(IntPtr engine, byte[] text, out IntPtr outResult);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_tokenize_result_delete(IntPtr result);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_tokenize_result_get_tokens(IntPtr result, out IntPtr outResult);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_tokenize_result_get_num_tokens(IntPtr result, out UIntPtr numTokens);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_engine_detokenize(IntPtr engine, int[] tokens, UIntPtr numTokens, out IntPtr outResult);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_detokenize_result_delete(IntPtr result);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_detokenize_result_get_string(IntPtr result, out IntPtr outResult);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_engine_get_start_token(IntPtr engine, out IntPtr outResult);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_engine_get_stop_tokens(IntPtr engine, out IntPtr outResult);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_token_union_delete(IntPtr tokenUnion);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_token_union_get_type(IntPtr tokenUnion, out int type);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_token_union_get_string(IntPtr tokenUnion, out IntPtr outResult);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_token_union_get_ids(IntPtr tokenUnion, out IntPtr outTokens, out UIntPtr outNumTokens);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_token_unions_delete(IntPtr tokenUnions);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_token_unions_get_num_tokens(IntPtr tokenUnions, out UIntPtr numTokens);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_token_unions_get_token_at(IntPtr tokenUnions, UIntPtr index, out IntPtr outResult);
    }
}
