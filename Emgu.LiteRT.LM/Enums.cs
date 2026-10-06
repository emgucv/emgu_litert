//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// The sampler type (LiteRtLmSamplerType)
    /// </summary>
    public enum SamplerType
    {
        /// <summary>
        /// Unspecified, use the model's default
        /// </summary>
        Unspecified = 0,
        /// <summary>
        /// Top-k sampling
        /// </summary>
        TopK = 1,
        /// <summary>
        /// Top-p (nucleus) sampling
        /// </summary>
        TopP = 2,
        /// <summary>
        /// Greedy decoding
        /// </summary>
        Greedy = 3,
    }

    /// <summary>
    /// The native log severity (LiteRtLmLogSeverity)
    /// </summary>
    public enum LogSeverity
    {
        /// <summary>
        /// Verbose
        /// </summary>
        Verbose = 0,
        /// <summary>
        /// Debug
        /// </summary>
        Debug = 1,
        /// <summary>
        /// Info
        /// </summary>
        Info = 2,
        /// <summary>
        /// Warning
        /// </summary>
        Warning = 3,
        /// <summary>
        /// Error
        /// </summary>
        Error = 4,
        /// <summary>
        /// Fatal
        /// </summary>
        Fatal = 5,
        /// <summary>
        /// No logging
        /// </summary>
        Silent = 1000,
    }

    /// <summary>
    /// The type of an input (LiteRtLmInputDataType)
    /// </summary>
    public enum InputDataType
    {
        /// <summary>
        /// UTF-8 text
        /// </summary>
        Text = 0,
        /// <summary>
        /// Encoded image bytes
        /// </summary>
        Image = 1,
        /// <summary>
        /// Marks the end of the image inputs
        /// </summary>
        ImageEnd = 2,
        /// <summary>
        /// Encoded audio bytes
        /// </summary>
        Audio = 3,
        /// <summary>
        /// Marks the end of the audio inputs
        /// </summary>
        AudioEnd = 4,
    }

    /// <summary>
    /// The activation data type (LiteRtLmActivationDataType)
    /// </summary>
    public enum ActivationDataType
    {
        /// <summary>
        /// 32-bit float
        /// </summary>
        Float32 = 0,
        /// <summary>
        /// 16-bit float
        /// </summary>
        Float16 = 1,
        /// <summary>
        /// 16-bit integer
        /// </summary>
        Int16 = 2,
        /// <summary>
        /// 8-bit integer
        /// </summary>
        Int8 = 3,
    }

    /// <summary>
    /// The type of constraint for constrained decoding (LiteRtLmConstraintType)
    /// </summary>
    public enum ConstraintType
    {
        /// <summary>
        /// No constraint
        /// </summary>
        None = 0,
        /// <summary>
        /// A regular expression
        /// </summary>
        Regex = 1,
        /// <summary>
        /// A JSON schema
        /// </summary>
        JsonSchema = 2,
    }

    /// <summary>
    /// The constraint provider (LiteRtLmConstraintProviderType)
    /// </summary>
    public enum ConstraintProviderType
    {
        /// <summary>
        /// llguidance
        /// </summary>
        LlGuidance = 1,
    }

    /// <summary>
    /// An input modality (LiteRtLmModality)
    /// </summary>
    public enum Modality
    {
        /// <summary>
        /// Text
        /// </summary>
        Text = 0,
        /// <summary>
        /// Vision
        /// </summary>
        Vision = 1,
        /// <summary>
        /// Audio
        /// </summary>
        Audio = 2,
        /// <summary>
        /// Video
        /// </summary>
        Video = 3,
    }
}
