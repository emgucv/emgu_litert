//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// The "thinking" (reasoning) settings of a conversation (LiteRtLmThinkingConfig), for models that support it.
    /// </summary>
    /// <remarks>The C API has no getters, so the properties return the values last set through this object
    /// (the type's default value before that, not LiteRT-LM's own default).</remarks>
    public class ThinkingConfig : Emgu.LiteRT.Util.UnmanagedObject
    {
        private bool _enableThinking;
        private int _thinkingTokenBudget;

        /// <summary>
        /// Create a thinking configuration.
        /// </summary>
        public ThinkingConfig()
        {
            _ptr = LiteRtLmInvoke.CheckPtr(LiteRtLmInvoke.litert_lm_thinking_config_create(), "litert_lm_thinking_config_create");
        }

        /// <summary>
        /// Whether the model thinks before answering
        /// </summary>
        public bool EnableThinking
        {
            get { return _enableThinking; }
            set { LiteRtLmInvoke.litert_lm_thinking_config_set_enable_thinking(_ptr, value); _enableThinking = value; }
        }

        /// <summary>
        /// The maximum number of tokens spent thinking
        /// </summary>
        public int ThinkingTokenBudget
        {
            get { return _thinkingTokenBudget; }
            set { LiteRtLmInvoke.litert_lm_thinking_config_set_thinking_token_budget(_ptr, value); _thinkingTokenBudget = value; }
        }

        /// <summary>
        /// Release the unmanaged configuration
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_thinking_config_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// The configuration of a conversation (LiteRtLmConversationConfig). The initial messages, tools and extra
    /// context are given as JSON strings in LiteRT-LM's message format, e.g. [{"role": "user", "content": "Hello"}].
    /// Invalid JSON is logged natively and ignored.
    /// </summary>
    /// <remarks>The values are copied when set. The C API has no getters, so the properties return the values last
    /// set through this object (the type's default value before that, not LiteRT-LM's own default).</remarks>
    public class ConversationConfig : Emgu.LiteRT.Util.UnmanagedObject
    {
        private SessionConfig _sessionConfig;
        private String _systemMessage;
        private String _toolsJson;
        private String _messagesJson;
        private String _extraContextJson;
        private String _promptTemplate;
        private bool _enableConstrainedDecoding;
        private ConstraintProviderType? _constraintProvider;
        private bool _filterChannelContentFromKvCache;
        private ThinkingConfig _thinkingConfig;

        /// <summary>
        /// Create a conversation configuration.
        /// </summary>
        public ConversationConfig()
        {
            _ptr = LiteRtLmInvoke.CheckPtr(LiteRtLmInvoke.litert_lm_conversation_config_create(), "litert_lm_conversation_config_create");
        }

        /// <summary>
        /// The configuration of the conversation's underlying session
        /// </summary>
        public SessionConfig SessionConfig
        {
            get { return _sessionConfig; }
            set { LiteRtLmInvoke.litert_lm_conversation_config_set_session_config(_ptr, value); _sessionConfig = value; }
        }

        /// <summary>
        /// The content of the system message: plain text, e.g. "You are a helpful assistant.", or message content as
        /// JSON (a JSON string or an array of content parts). LiteRT-LM adds the "role": "system" itself.
        /// </summary>
        public String SystemMessage
        {
            get { return _systemMessage; }
            set { LiteRtLmInvoke.litert_lm_conversation_config_set_system_message(_ptr, LiteRtLmInvoke.ToUtf8(value)); _systemMessage = value; }
        }

        /// <summary>
        /// The tools (functions) the model may call, as a JSON array
        /// </summary>
        public String ToolsJson
        {
            get { return _toolsJson; }
            set { LiteRtLmInvoke.litert_lm_conversation_config_set_tools(_ptr, LiteRtLmInvoke.ToUtf8(value)); _toolsJson = value; }
        }

        /// <summary>
        /// The initial message history, as a JSON array of messages
        /// </summary>
        public String MessagesJson
        {
            get { return _messagesJson; }
            set { LiteRtLmInvoke.litert_lm_conversation_config_set_messages(_ptr, LiteRtLmInvoke.ToUtf8(value)); _messagesJson = value; }
        }

        /// <summary>
        /// Extra context passed to the prompt template, as a JSON object
        /// </summary>
        public String ExtraContextJson
        {
            get { return _extraContextJson; }
            set { LiteRtLmInvoke.litert_lm_conversation_config_set_extra_context(_ptr, LiteRtLmInvoke.ToUtf8(value)); _extraContextJson = value; }
        }

        /// <summary>
        /// A prompt template overriding the model's own
        /// </summary>
        public String PromptTemplate
        {
            get { return _promptTemplate; }
            set { LiteRtLmInvoke.litert_lm_conversation_config_set_prompt_template(_ptr, LiteRtLmInvoke.ToUtf8(value)); _promptTemplate = value; }
        }

        /// <summary>
        /// Whether to constrain the generated text (e.g. to valid tool calls)
        /// </summary>
        public bool EnableConstrainedDecoding
        {
            get { return _enableConstrainedDecoding; }
            set { LiteRtLmInvoke.litert_lm_conversation_config_set_enable_constrained_decoding(_ptr, value); _enableConstrainedDecoding = value; }
        }

        /// <summary>
        /// The constraint provider used for constrained decoding, or null for the default
        /// </summary>
        public ConstraintProviderType? ConstraintProvider
        {
            get { return _constraintProvider; }
            set
            {
                if (value.HasValue)
                {
                    int provider = (int)value.Value;
                    LiteRtLmInvoke.litert_lm_conversation_config_set_constraint_provider(_ptr, ref provider);
                }
                else
                {
                    LiteRtLmInvoke.litert_lm_conversation_config_set_constraint_provider(_ptr, IntPtr.Zero);
                }
                _constraintProvider = value;
            }
        }

        /// <summary>
        /// Whether to keep channel content (e.g. thinking) out of the KV cache
        /// </summary>
        public bool FilterChannelContentFromKvCache
        {
            get { return _filterChannelContentFromKvCache; }
            set { LiteRtLmInvoke.litert_lm_conversation_config_set_filter_channel_content_from_kv_cache(_ptr, value); _filterChannelContentFromKvCache = value; }
        }

        /// <summary>
        /// Stream tool calls as they are generated.
        /// </summary>
        /// <param name="streamToolCalls">Whether to stream tool calls</param>
        /// <param name="channelName">The channel the tool calls are streamed on, or null</param>
        public void SetStreamToolCalls(bool streamToolCalls, String channelName = null)
        {
            LiteRtLmInvoke.litert_lm_conversation_config_set_stream_tool_calls(_ptr, streamToolCalls, LiteRtLmInvoke.ToUtf8(channelName));
        }

        /// <summary>
        /// The thinking settings, or null to use the model's default
        /// </summary>
        public ThinkingConfig ThinkingConfig
        {
            get { return _thinkingConfig; }
            set { LiteRtLmInvoke.litert_lm_conversation_config_set_thinking_config(_ptr, value); _thinkingConfig = value; }
        }

        /// <summary>
        /// Release the unmanaged configuration
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_conversation_config_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// Per-message options of Conversation.SendMessage (LiteRtLmConversationOptionalArgs).
    /// </summary>
    /// <remarks>The values are copied when set. The C API has no getters, so the properties return the values last
    /// set through this object (the type's default value before that, not LiteRT-LM's own default).</remarks>
    public class ConversationOptionalArgs : Emgu.LiteRT.Util.UnmanagedObject
    {
        private RepetitionPenaltyConfig _repetitionPenaltyConfig;
        private NoRepeatNgramConfig _noRepeatNgramConfig;
        private SuppressTokensConfig _suppressTokensConfig;
        private int _visualTokenBudget;
        private int _maxOutputTokens;
        private ThinkingConfig _thinkingConfig;

        /// <summary>
        /// Create the optional arguments.
        /// </summary>
        public ConversationOptionalArgs()
        {
            _ptr = LiteRtLmInvoke.CheckPtr(LiteRtLmInvoke.litert_lm_conversation_optional_args_create(), "litert_lm_conversation_optional_args_create");
        }

        /// <summary>
        /// The repetition penalties, or null for none
        /// </summary>
        public RepetitionPenaltyConfig RepetitionPenaltyConfig
        {
            get { return _repetitionPenaltyConfig; }
            set { LiteRtLmInvoke.litert_lm_conversation_optional_args_set_repetition_penalty_config(_ptr, value); _repetitionPenaltyConfig = value; }
        }

        /// <summary>
        /// The no-repeat n-gram settings, or null for none
        /// </summary>
        public NoRepeatNgramConfig NoRepeatNgramConfig
        {
            get { return _noRepeatNgramConfig; }
            set { LiteRtLmInvoke.litert_lm_conversation_optional_args_set_no_repeat_ngram_config(_ptr, value); _noRepeatNgramConfig = value; }
        }

        /// <summary>
        /// The suppressed tokens, or null for none
        /// </summary>
        public SuppressTokensConfig SuppressTokensConfig
        {
            get { return _suppressTokensConfig; }
            set { LiteRtLmInvoke.litert_lm_conversation_optional_args_set_suppress_tokens_config(_ptr, value); _suppressTokensConfig = value; }
        }

        /// <summary>
        /// The maximum number of vision tokens for the message's images
        /// </summary>
        public int VisualTokenBudget
        {
            get { return _visualTokenBudget; }
            set { LiteRtLmInvoke.litert_lm_conversation_optional_args_set_visual_token_budget(_ptr, value); _visualTokenBudget = value; }
        }

        /// <summary>
        /// The maximum number of tokens to generate for the message
        /// </summary>
        public int MaxOutputTokens
        {
            get { return _maxOutputTokens; }
            set { LiteRtLmInvoke.litert_lm_conversation_optional_args_set_max_output_tokens(_ptr, value); _maxOutputTokens = value; }
        }

        /// <summary>
        /// The thinking settings for the message, or null to use the conversation's
        /// </summary>
        public ThinkingConfig ThinkingConfig
        {
            get { return _thinkingConfig; }
            set { LiteRtLmInvoke.litert_lm_conversation_optional_args_set_thinking_config(_ptr, value); _thinkingConfig = value; }
        }

        /// <summary>
        /// Constrain the generated text to a regular expression or JSON schema.
        /// </summary>
        /// <param name="constraintType">The constraint type</param>
        /// <param name="constraint">The regular expression or JSON schema</param>
        public void SetConstraint(ConstraintType constraintType, String constraint)
        {
            LiteRtLmInvoke.litert_lm_conversation_optional_args_set_constraint(_ptr, constraintType, LiteRtLmInvoke.ToUtf8(constraint));
        }

        /// <summary>
        /// Release the unmanaged optional arguments
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_conversation_optional_args_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    public static partial class LiteRtLmInvoke
    {
        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_thinking_config_create();

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_thinking_config_delete(IntPtr config);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_thinking_config_set_enable_thinking(
            IntPtr config,
            [MarshalAs(BoolMarshalType)] bool enableThinking);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_thinking_config_set_thinking_token_budget(IntPtr config, int thinkingTokenBudget);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_conversation_config_create();

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_config_delete(IntPtr config);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_config_set_session_config(IntPtr config, IntPtr sessionConfig);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_config_set_system_message(IntPtr config, byte[] systemMessageJson);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_config_set_tools(IntPtr config, byte[] toolsJson);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_config_set_messages(IntPtr config, byte[] messagesJson);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_config_set_extra_context(IntPtr config, byte[] extraContextJson);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_config_set_prompt_template(IntPtr config, byte[] promptTemplate);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_config_set_enable_constrained_decoding(
            IntPtr config,
            [MarshalAs(BoolMarshalType)] bool enableConstrainedDecoding);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_config_set_constraint_provider(IntPtr config, ref int providerType);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_config_set_constraint_provider(IntPtr config, IntPtr providerType);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_config_set_filter_channel_content_from_kv_cache(
            IntPtr config,
            [MarshalAs(BoolMarshalType)] bool filterChannelContentFromKvCache);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_config_set_stream_tool_calls(
            IntPtr config,
            [MarshalAs(BoolMarshalType)] bool streamToolCalls,
            byte[] channelName);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_config_set_thinking_config(IntPtr config, IntPtr thinkingConfig);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_conversation_optional_args_create();

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_optional_args_delete(IntPtr optionalArgs);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_optional_args_set_repetition_penalty_config(IntPtr optionalArgs, IntPtr config);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_optional_args_set_no_repeat_ngram_config(IntPtr optionalArgs, IntPtr config);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_optional_args_set_suppress_tokens_config(IntPtr optionalArgs, IntPtr config);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_optional_args_set_visual_token_budget(IntPtr optionalArgs, int visualTokenBudget);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_optional_args_set_max_output_tokens(IntPtr optionalArgs, int maxOutputTokens);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_optional_args_set_thinking_config(IntPtr optionalArgs, IntPtr thinkingConfig);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_conversation_optional_args_set_constraint(
            IntPtr optionalArgs,
            ConstraintType constraintType,
            byte[] constraintString);
    }
}
