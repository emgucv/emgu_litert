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
        /// None: unsets the provider
        /// </summary>
        None = 0,
        /// <summary>
        /// llguidance
        /// </summary>
        LlGuidance = 1,
    }

    /// <summary>
    /// The status of a LiteRT-LM C API call (LiteRtLmStatusCode, the same values as absl::StatusCode)
    /// </summary>
    public enum LiteRtLmStatusCode
    {
        /// <summary>Success</summary>
        Ok = 0,
        /// <summary>The operation was cancelled</summary>
        Cancelled = 1,
        /// <summary>An unknown error</summary>
        Unknown = 2,
        /// <summary>An invalid argument</summary>
        InvalidArgument = 3,
        /// <summary>A deadline passed</summary>
        DeadlineExceeded = 4,
        /// <summary>Something (a file, a value) wasn't found</summary>
        NotFound = 5,
        /// <summary>It already exists</summary>
        AlreadyExists = 6,
        /// <summary>Permission denied</summary>
        PermissionDenied = 7,
        /// <summary>A resource (e.g. memory) ran out</summary>
        ResourceExhausted = 8,
        /// <summary>The system isn't in a state for the operation</summary>
        FailedPrecondition = 9,
        /// <summary>The operation was aborted</summary>
        Aborted = 10,
        /// <summary>Out of range</summary>
        OutOfRange = 11,
        /// <summary>Not implemented</summary>
        Unimplemented = 12,
        /// <summary>An internal error</summary>
        Internal = 13,
        /// <summary>Unavailable</summary>
        Unavailable = 14,
        /// <summary>Data was lost or corrupted</summary>
        DataLoss = 15,
        /// <summary>Not authenticated</summary>
        Unauthenticated = 16,
    }

    /// <summary>
    /// How speculative decoding (e.g. MTP) is used during decode (LiteRtLmSpeculativeDecodingMode)
    /// </summary>
    public enum SpeculativeDecodingMode
    {
        /// <summary>Never speculate</summary>
        Off = 0,
        /// <summary>Speculate on every decode step</summary>
        On = 1,
        /// <summary>Speculate only while it is measured to be faster than plain decoding</summary>
        Adaptive = 2,
    }

    /// <summary>
    /// A backend a model can run on (LiteRtLmBackendType)
    /// </summary>
    public enum BackendType
    {
        /// <summary>CPU</summary>
        Cpu = 1,
        /// <summary>GPU</summary>
        Gpu = 2,
        /// <summary>NPU</summary>
        Npu = 3,
    }

    /// <summary>
    /// The NPU a model file was compiled for (LiteRtLmNpuBrand)
    /// </summary>
    public enum NpuBrand
    {
        /// <summary>Unknown, or not compiled for an NPU</summary>
        Unknown = 0,
        /// <summary>Qualcomm</summary>
        Qualcomm = 1,
        /// <summary>Google Tensor</summary>
        GoogleTensor = 2,
        /// <summary>MediaTek</summary>
        MediaTek = 3,
        /// <summary>Intel</summary>
        Intel = 4,
        /// <summary>Samsung</summary>
        Samsung = 5,
    }

    /// <summary>
    /// What kind of model a .litertlm file holds (LiteRtLmModelType)
    /// </summary>
    public enum ModelType
    {
        /// <summary>Unknown</summary>
        Unknown = 0,
        /// <summary>A language model</summary>
        Llm = 1,
        /// <summary>An embedding model</summary>
        Embedding = 2,
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
