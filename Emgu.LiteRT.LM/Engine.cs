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
        /// <summary>
        /// Create an engine, loading the model described by the settings. This can take a while for large models.
        /// The settings are copied, so they can be disposed afterwards.
        /// </summary>
        /// <param name="settings">The engine settings</param>
        public Engine(EngineSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException("settings");
            _ptr = LiteRtLmInvoke.CheckPtr(LiteRtLmInvoke.litert_lm_engine_create(settings), "litert_lm_engine_create");
        }

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
            IntPtr result = LiteRtLmInvoke.CheckPtr(
                LiteRtLmInvoke.litert_lm_engine_tokenize(_ptr, LiteRtLmInvoke.ToUtf8(text ?? String.Empty)),
                "litert_lm_engine_tokenize");
            try
            {
                int numTokens = (int)LiteRtLmInvoke.litert_lm_tokenize_result_get_num_tokens(result).ToUInt32();
                int[] tokens = new int[numTokens];
                if (numTokens > 0)
                    Marshal.Copy(LiteRtLmInvoke.litert_lm_tokenize_result_get_tokens(result), tokens, 0, numTokens);
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
            IntPtr result = LiteRtLmInvoke.CheckPtr(
                LiteRtLmInvoke.litert_lm_engine_detokenize(_ptr, ids, new UIntPtr((uint)ids.Length)),
                "litert_lm_engine_detokenize");
            try
            {
                return LiteRtLmInvoke.PtrToStringUtf8(LiteRtLmInvoke.litert_lm_detokenize_result_get_string(result));
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
                IntPtr tokenUnion = LiteRtLmInvoke.litert_lm_engine_get_start_token(_ptr);
                return tokenUnion == IntPtr.Zero ? null : Token.FromTokenUnion(tokenUnion);
            }
        }

        /// <summary>
        /// The model's stop tokens (empty if the model doesn't define any).
        /// </summary>
        public Token[] StopTokens
        {
            get
            {
                IntPtr tokenUnions = LiteRtLmInvoke.litert_lm_engine_get_stop_tokens(_ptr);
                if (tokenUnions == IntPtr.Zero)
                    return new Token[0];
                try
                {
                    int count = (int)LiteRtLmInvoke.litert_lm_token_unions_get_num_tokens(tokenUnions).ToUInt32();
                    Token[] tokens = new Token[count];
                    for (int i = 0; i < count; i++)
                        tokens[i] = Token.FromTokenUnion(
                            LiteRtLmInvoke.litert_lm_token_unions_get_token_at(tokenUnions, new UIntPtr((uint)i)));
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
        /// Read a LiteRtLmTokenUnion into a Token, and delete the token union.
        /// </summary>
        internal static Token FromTokenUnion(IntPtr tokenUnion)
        {
            if (tokenUnion == IntPtr.Zero)
                return null;
            try
            {
                Token token = new Token();
                if (LiteRtLmInvoke.litert_lm_token_union_get_type(tokenUnion) == 0)
                {
                    token.Text = LiteRtLmInvoke.PtrToStringUtf8(LiteRtLmInvoke.litert_lm_token_union_get_string(tokenUnion));
                }
                else
                {
                    IntPtr ids;
                    UIntPtr numIds;
                    if (LiteRtLmInvoke.litert_lm_token_union_get_ids(tokenUnion, out ids, out numIds) == 0)
                    {
                        int count = (int)numIds.ToUInt32();
                        token.Ids = new int[count];
                        if (count > 0)
                            Marshal.Copy(ids, token.Ids, 0, count);
                    }
                }
                return token;
            }
            finally
            {
                LiteRtLmInvoke.litert_lm_token_union_delete(tokenUnion);
            }
        }
    }

    public static partial class LiteRtLmInvoke
    {
        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_engine_create(IntPtr settings);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_delete(IntPtr engine);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_engine_tokenize(IntPtr engine, byte[] text);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_tokenize_result_delete(IntPtr result);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_tokenize_result_get_tokens(IntPtr result);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern UIntPtr litert_lm_tokenize_result_get_num_tokens(IntPtr result);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_engine_detokenize(IntPtr engine, int[] tokens, UIntPtr numTokens);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_detokenize_result_delete(IntPtr result);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_detokenize_result_get_string(IntPtr result);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_engine_get_start_token(IntPtr engine);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_engine_get_stop_tokens(IntPtr engine);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_token_union_delete(IntPtr tokenUnion);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_token_union_get_type(IntPtr tokenUnion);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_token_union_get_string(IntPtr tokenUnion);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_token_union_get_ids(IntPtr tokenUnion, out IntPtr outTokens, out UIntPtr outNumTokens);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_token_unions_delete(IntPtr tokenUnions);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern UIntPtr litert_lm_token_unions_get_num_tokens(IntPtr tokenUnions);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_token_unions_get_token_at(IntPtr tokenUnions, UIntPtr index);
    }
}
