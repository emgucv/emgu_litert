//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Emgu.LiteRT
{
    /// <summary>
    /// The compilation options used to create a CompiledModel (LiteRtOptions).
    /// </summary>
    public class Options : Emgu.LiteRT.Util.UnmanagedObject
    {
        /// <summary>
        /// Create compilation options with the default settings.
        /// </summary>
        public Options()
        {
            LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtCreateOptions(out _ptr));
        }

        /// <summary>
        /// Create compilation options targeting the specific hardware accelerators.
        /// </summary>
        /// <param name="hardwareAccelerators">The hardware accelerators to use</param>
        public Options(HwAccelerators hardwareAccelerators)
            : this()
        {
            HardwareAccelerators = hardwareAccelerators;
        }

        /// <summary>
        /// The hardware accelerators the model should be compiled for.
        /// </summary>
        public HwAccelerators HardwareAccelerators
        {
            get
            {
                LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetOptionsHardwareAccelerators(_ptr, out int accelerators));
                return (HwAccelerators)accelerators;
            }
            set
            {
                LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtSetOptionsHardwareAccelerators(_ptr, (int)value));
            }
        }

        /// <summary>
        /// Add CPU (XNNPACK) settings: the thread count and the weight cache. They apply when the model runs on the CPU.
        /// </summary>
        /// <param name="cpuOptions">The CPU settings</param>
        public void AddCpuOptions(CpuOptions cpuOptions)
        {
            if (cpuOptions == null)
                throw new ArgumentNullException("cpuOptions");
            // LiteRT renamed the CPU accelerator's identifier from "xnnpack" to "cpu_options" (LiteRT ca21dc99, LiteRT-LM
            // v0.18.1), and each runtime ignores the other's, so the payload goes under both and works with either libLiteRt.
            String toml = cpuOptions.ToToml();
            AddOpaqueOptions("cpu_options", toml);
            AddOpaqueOptions("xnnpack", toml);
        }

        /// <summary>
        /// Add GPU settings, such as the precision. They apply when the model runs on the GPU.
        /// </summary>
        /// <param name="gpuOptions">The GPU settings</param>
        public void AddGpuOptions(GpuOptions gpuOptions)
        {
            if (gpuOptions == null)
                throw new ArgumentNullException("gpuOptions");
            AddOpaqueOptions("gpu_options", gpuOptions.ToToml());
        }

        // LiteRT's accelerator options are opaque payloads: a null-terminated TOML text under an identifier, which the
        // accelerator parses when the model is compiled (what LiteRT's own LrtGetOpaque*OptionsData helpers build; those
        // helpers aren't exported by libLiteRt). The payload is freed by LiteRT through FreePayload.
        private void AddOpaqueOptions(String identifier, String toml)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(toml);
            IntPtr payload = Marshal.AllocHGlobal(utf8.Length + 1);
            Marshal.Copy(utf8, 0, payload, utf8.Length);
            Marshal.WriteByte(payload, utf8.Length, 0);
            IntPtr opaque;
            Status status = LiteRtInvoke.LiteRtCreateOpaqueOptions(identifier, payload, FreePayloadDelegate, out opaque);
            if (status != Status.Ok)
            {
                Marshal.FreeHGlobal(payload);
                LiteRtInvoke.CheckStatus(status);
            }
            status = LiteRtInvoke.LiteRtAddOpaqueOptions(_ptr, opaque);
            if (status != Status.Ok)
            {
                LiteRtInvoke.LiteRtDestroyOpaqueOptions(opaque);
                LiteRtInvoke.CheckStatus(status);
            }
        }

        private static readonly LiteRtInvoke.PayloadDestructor FreePayloadDelegate = FreePayload;

#if __IOS__
        [ObjCRuntime.MonoPInvokeCallback(typeof(LiteRtInvoke.PayloadDestructor))]
#elif UNITY_ANDROID || UNITY_IOS || UNITY_EDITOR || UNITY_STANDALONE
        [AOT.MonoPInvokeCallback(typeof(LiteRtInvoke.PayloadDestructor))]
#endif
        private static void FreePayload(IntPtr payload)
        {
            Marshal.FreeHGlobal(payload);
        }

        /// <summary>
        /// Release the unmanaged options
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtInvoke.LiteRtDestroyOptions(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    public static partial class LiteRtInvoke
    {
        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtCreateOptions(out IntPtr options);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern void LiteRtDestroyOptions(IntPtr options);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtSetOptionsHardwareAccelerators(IntPtr options, int hardwareAccelerators);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetOptionsHardwareAccelerators(IntPtr options, out int hardwareAccelerators);

        [UnmanagedFunctionPointer(LiteRtCallingConvention)]
        internal delegate void PayloadDestructor(IntPtr payload);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtCreateOpaqueOptions(
            [MarshalAs(StringMarshalType)] String payloadIdentifier,
            IntPtr payloadData,
            PayloadDestructor payloadDestructor,
            out IntPtr options);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern void LiteRtDestroyOpaqueOptions(IntPtr options);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtAddOpaqueOptions(IntPtr options, IntPtr opaqueOptions);
    }

    /// <summary>
    /// CPU (XNNPACK) settings for compiling a model, added with Options.AddCpuOptions. Unset values keep LiteRT's defaults.
    /// </summary>
    public class CpuOptions
    {
        /// <summary>
        /// The number of threads XNNPACK uses, or null for LiteRT's default
        /// </summary>
        public int? NumThreads { get; set; }

        /// <summary>
        /// A file where XNNPACK keeps the model's weights repacked for the CPU, or null for none. XNNPACK repacks the
        /// weights every time a model is compiled; with a cache file the first compilation writes it and later ones map
        /// it, so loading a large model takes milliseconds instead of seconds (and its weights are file-backed memory the
        /// system can page out). Use one file per model; the folder must exist.
        /// </summary>
        public String WeightCachePath { get; set; }

        internal String ToToml()
        {
            StringBuilder toml = new StringBuilder();
            if (NumThreads.HasValue)
                toml.AppendFormat(CultureInfo.InvariantCulture, "num_threads = {0}\n", NumThreads.Value);
            if (WeightCachePath != null)
                toml.AppendFormat("weight_cache_file_path = {0}\n", TomlString(WeightCachePath));
            return toml.ToString();
        }

        // A TOML string; LiteRT's parser takes the text between the quotes as it is, so paths use forward slashes
        // (which Windows accepts too) rather than escaped backslashes.
        internal static String TomlString(String value)
        {
            return "\"" + value.Replace('\\', '/') + "\"";
        }
    }

    /// <summary>
    /// The precision the GPU computes in
    /// </summary>
    public enum GpuPrecision
    {
        /// <summary>The accelerator's default (usually fp16)</summary>
        Default = 0,
        /// <summary>16-bit floats</summary>
        Fp16 = 1,
        /// <summary>32-bit floats: slower, but needed by models whose values overflow fp16</summary>
        Fp32 = 2,
        /// <summary>16-bit floats with 32-bit accumulation</summary>
        Fp16WithFp32Accumulation = 3
    }

    /// <summary>
    /// GPU settings for compiling a model, added with Options.AddGpuOptions. Unset values keep LiteRT's defaults.
    /// </summary>
    public class GpuOptions
    {
        /// <summary>
        /// The precision the GPU computes in, or null for the default
        /// </summary>
        public GpuPrecision? Precision { get; set; }

        /// <summary>
        /// Let the GPU run fully connected and convolution layers on quantized inputs (the activations are quantized too):
        /// less memory for int8 models, at some loss of accuracy. Null for the default.
        /// </summary>
        public bool? AllowSrcQuantizedFcConvOps { get; set; }

        /// <summary>
        /// A folder where the GPU accelerator keeps compiled programs (with SerializeProgramCache and ModelCacheKey), so
        /// later compilations of the model are faster; null for none
        /// </summary>
        public String SerializationDir { get; set; }

        /// <summary>
        /// The name the model's cached programs are kept under in SerializationDir
        /// </summary>
        public String ModelCacheKey { get; set; }

        /// <summary>
        /// Whether to keep the compiled programs in SerializationDir, or null for the default
        /// </summary>
        public bool? SerializeProgramCache { get; set; }

        internal String ToToml()
        {
            StringBuilder toml = new StringBuilder();
            if (Precision.HasValue)
                toml.AppendFormat(CultureInfo.InvariantCulture, "precision = {0}\n", (int)Precision.Value);
            if (AllowSrcQuantizedFcConvOps.HasValue)
                toml.AppendFormat("allow_src_quantized_fc_conv_ops = {0}\n", AllowSrcQuantizedFcConvOps.Value ? "true" : "false");
            if (SerializationDir != null)
                toml.AppendFormat("serialization_dir = {0}\n", CpuOptions.TomlString(SerializationDir));
            if (ModelCacheKey != null)
                toml.AppendFormat("model_cache_key = {0}\n", CpuOptions.TomlString(ModelCacheKey));
            if (SerializeProgramCache.HasValue)
                toml.AppendFormat("serialize_program_cache = {0}\n", SerializeProgramCache.Value ? "true" : "false");
            return toml.ToString();
        }
    }
}
