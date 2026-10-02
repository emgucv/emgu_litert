//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;

namespace Emgu.TF.Lite.LiteRt
{
    /// <summary>
    /// LiteRT status code (LiteRtStatus)
    /// </summary>
    public enum Status
    {
        /// <summary>Success</summary>
        Ok = 0,
        /// <summary>Invalid argument</summary>
        ErrorInvalidArgument = 1,
        /// <summary>Memory allocation failure</summary>
        ErrorMemoryAllocationFailure = 2,
        /// <summary>Runtime failure</summary>
        ErrorRuntimeFailure = 3,
        /// <summary>Missing input tensor</summary>
        ErrorMissingInputTensor = 4,
        /// <summary>Unsupported</summary>
        ErrorUnsupported = 5,
        /// <summary>Not found</summary>
        ErrorNotFound = 6,
        /// <summary>Timeout expired</summary>
        ErrorTimeoutExpired = 7,
        /// <summary>Wrong version</summary>
        ErrorWrongVersion = 8,
        /// <summary>Unknown error</summary>
        ErrorUnknown = 9,
        /// <summary>Already exists</summary>
        ErrorAlreadyExists = 10,
        /// <summary>Inference cancelled</summary>
        Cancelled = 100,
        /// <summary>File IO error</summary>
        ErrorFileIO = 500,
        /// <summary>Invalid flatbuffer</summary>
        ErrorInvalidFlatbuffer = 501,
        /// <summary>Dynamic loading error</summary>
        ErrorDynamicLoading = 502,
        /// <summary>Serialization error</summary>
        ErrorSerialization = 503,
        /// <summary>Compilation error</summary>
        ErrorCompilation = 504,
        /// <summary>Index out of bound</summary>
        ErrorIndexOOB = 1000,
        /// <summary>Invalid IR type</summary>
        ErrorInvalidIrType = 1001,
        /// <summary>Invalid graph invariant</summary>
        ErrorInvalidGraphInvariant = 1002,
        /// <summary>Graph modification error</summary>
        ErrorGraphModification = 1003,
        /// <summary>Invalid tool config</summary>
        ErrorInvalidToolConfig = 1500,
        /// <summary>Legalize no match</summary>
        LegalizeNoMatch = 2000,
        /// <summary>Invalid legalization</summary>
        ErrorInvalidLegalization = 2001,
        /// <summary>Pattern no match</summary>
        PatternNoMatch = 3000,
        /// <summary>Invalid transformation</summary>
        InvalidTransformation = 3001,
        /// <summary>Unsupported runtime version</summary>
        ErrorUnsupportedRuntimeVersion = 4000,
        /// <summary>Unsupported compiler version</summary>
        ErrorUnsupportedCompilerVersion = 4001,
        /// <summary>Incompatible byte code version</summary>
        ErrorIncompatibleByteCodeVersion = 4002,
        /// <summary>Unsupported op shape inferer</summary>
        ErrorUnsupportedOpShapeInferer = 5000,
        /// <summary>Shape inference failed</summary>
        ErrorShapeInferenceFailed = 5001,
    }

    /// <summary>
    /// Primitive types for elements in a tensor (LiteRtElementType). The values match TfLiteType.
    /// </summary>
    public enum ElementType
    {
        /// <summary>No type</summary>
        None = 0,
        /// <summary>bool</summary>
        Bool = 6,
        /// <summary>2-bit signed integer</summary>
        Int2 = 20,
        /// <summary>4-bit signed integer</summary>
        Int4 = 18,
        /// <summary>8-bit signed integer</summary>
        Int8 = 9,
        /// <summary>16-bit signed integer</summary>
        Int16 = 7,
        /// <summary>32-bit signed integer</summary>
        Int32 = 2,
        /// <summary>64-bit signed integer</summary>
        Int64 = 4,
        /// <summary>4-bit unsigned integer</summary>
        UInt4 = 21,
        /// <summary>8-bit unsigned integer</summary>
        UInt8 = 3,
        /// <summary>16-bit unsigned integer</summary>
        UInt16 = 17,
        /// <summary>32-bit unsigned integer</summary>
        UInt32 = 16,
        /// <summary>64-bit unsigned integer</summary>
        UInt64 = 13,
        /// <summary>8-bit float, E4M3FN</summary>
        Float8E4M3FN = 22,
        /// <summary>8-bit float, E5M2</summary>
        Float8E5M2 = 23,
        /// <summary>16-bit float</summary>
        Float16 = 10,
        /// <summary>bfloat16</summary>
        BFloat16 = 19,
        /// <summary>32-bit float</summary>
        Float32 = 1,
        /// <summary>64-bit float</summary>
        Float64 = 11,
        /// <summary>64-bit complex</summary>
        Complex64 = 8,
        /// <summary>128-bit complex</summary>
        Complex128 = 12,
        /// <summary>TF resource</summary>
        TfResource = 14,
        /// <summary>TF string</summary>
        TfString = 5,
        /// <summary>TF variant</summary>
        TfVariant = 15,
    }

    /// <summary>
    /// Hardware accelerators (LiteRtHwAccelerators). Can be combined.
    /// </summary>
    [Flags]
    public enum HwAccelerators
    {
        /// <summary>None</summary>
        None = 0,
        /// <summary>CPU</summary>
        Cpu = 1 << 0,
        /// <summary>GPU</summary>
        Gpu = 1 << 1,
        /// <summary>NPU</summary>
        Npu = 1 << 2,
    }

    /// <summary>
    /// Lock mode for tensor buffer (LiteRtTensorBufferLockMode)
    /// </summary>
    public enum TensorBufferLockMode
    {
        /// <summary>Read</summary>
        Read = 0,
        /// <summary>Write</summary>
        Write = 1,
        /// <summary>Read and write</summary>
        ReadWrite = 2,
    }

    /// <summary>
    /// The type of memory backing a tensor buffer (LiteRtTensorBufferType)
    /// </summary>
    public enum TensorBufferType
    {
        /// <summary>Unknown</summary>
        Unknown = 0,
        /// <summary>Host (CPU) memory</summary>
        HostMemory = 1,
        /// <summary>Android hardware buffer</summary>
        Ahwb = 2,
        /// <summary>ION buffer</summary>
        Ion = 3,
        /// <summary>DMA-BUF buffer</summary>
        DmaBuf = 4,
        /// <summary>FastRPC buffer</summary>
        FastRpc = 5,
        /// <summary>OpenGL buffer</summary>
        GlBuffer = 6,
        /// <summary>OpenGL texture</summary>
        GlTexture = 7,
        /// <summary>OpenCL buffer</summary>
        OpenClBuffer = 10,
        /// <summary>OpenCL buffer, fp16</summary>
        OpenClBufferFp16 = 11,
        /// <summary>OpenCL texture</summary>
        OpenClTexture = 12,
        /// <summary>OpenCL texture, fp16</summary>
        OpenClTextureFp16 = 13,
        /// <summary>OpenCL packed buffer</summary>
        OpenClBufferPacked = 14,
        /// <summary>OpenCL image buffer</summary>
        OpenClImageBuffer = 15,
        /// <summary>OpenCL image buffer, fp16</summary>
        OpenClImageBufferFp16 = 16,
        /// <summary>WebGPU buffer</summary>
        WebGpuBuffer = 20,
        /// <summary>WebGPU buffer, fp16</summary>
        WebGpuBufferFp16 = 21,
        /// <summary>WebGPU texture</summary>
        WebGpuTexture = 22,
        /// <summary>WebGPU texture, fp16</summary>
        WebGpuTextureFp16 = 23,
        /// <summary>WebGPU image buffer</summary>
        WebGpuImageBuffer = 24,
        /// <summary>WebGPU image buffer, fp16</summary>
        WebGpuImageBufferFp16 = 25,
        /// <summary>WebGPU packed buffer</summary>
        WebGpuBufferPacked = 26,
        /// <summary>Metal buffer</summary>
        MetalBuffer = 30,
        /// <summary>Metal buffer, fp16</summary>
        MetalBufferFp16 = 31,
        /// <summary>Metal texture</summary>
        MetalTexture = 32,
        /// <summary>Metal texture, fp16</summary>
        MetalTextureFp16 = 33,
        /// <summary>Metal packed buffer</summary>
        MetalBufferPacked = 34,
        /// <summary>Vulkan buffer (experimental)</summary>
        VulkanBuffer = 40,
        /// <summary>Vulkan buffer, fp16 (experimental)</summary>
        VulkanBufferFp16 = 41,
        /// <summary>Vulkan texture (experimental)</summary>
        VulkanTexture = 42,
        /// <summary>Vulkan texture, fp16 (experimental)</summary>
        VulkanTextureFp16 = 43,
        /// <summary>Vulkan image buffer (experimental)</summary>
        VulkanImageBuffer = 44,
        /// <summary>Vulkan image buffer, fp16 (experimental)</summary>
        VulkanImageBufferFp16 = 45,
        /// <summary>Vulkan packed buffer (experimental)</summary>
        VulkanBufferPacked = 46,
        /// <summary>Start of the range reserved for user custom memory objects</summary>
        UserCustomBuffer = 100,
        /// <summary>End of the range reserved for user custom memory objects</summary>
        UserCustomBufferEnd = 199,
    }
}
