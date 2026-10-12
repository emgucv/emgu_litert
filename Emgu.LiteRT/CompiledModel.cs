//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT
{
    /// <summary>
    /// A model compiled for the selected hardware accelerators (LiteRtCompiledModel). This is LiteRT's
    /// inference API: create the input and output buffers, write the inputs, then call Run.
    /// </summary>
    public class CompiledModel : Emgu.LiteRT.Util.UnmanagedObject
    {
        // Held so the environment and model are not finalized while this compiled model still uses them.
        private readonly Environment _environment;
        private readonly Model _model;

        /// <summary>
        /// Compile a model.
        /// </summary>
        /// <param name="environment">The LiteRT environment</param>
        /// <param name="model">The model to compile</param>
        /// <param name="options">The compilation options. If null, the LiteRT defaults are used.</param>
        public CompiledModel(Environment environment, Model model, Options options = null)
        {
            _environment = environment;
            _model = model;
            LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtCreateCompiledModel(environment, model, options, out _ptr));
        }

        /// <summary>
        /// Compile a model for the given hardware accelerators.
        /// </summary>
        /// <param name="environment">The LiteRT environment</param>
        /// <param name="model">The model to compile</param>
        /// <param name="hardwareAccelerators">The hardware accelerators to use</param>
        public CompiledModel(Environment environment, Model model, HwAccelerators hardwareAccelerators)
        {
            _environment = environment;
            _model = model;
            // The options are owned by the caller and only needed during compilation.
            using (Options options = new Options(hardwareAccelerators))
            {
                LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtCreateCompiledModel(environment, model, options, out _ptr));
            }
        }

        /// <summary>
        /// The model this compiled model was created from
        /// </summary>
        public Model Model
        {
            get { return _model; }
        }

        /// <summary>
        /// True if the whole model runs on the selected accelerators
        /// </summary>
        public bool IsFullyAccelerated
        {
            get
            {
                LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtCompiledModelIsFullyAccelerated(_ptr, out bool fullyAccelerated));
                return fullyAccelerated;
            }
        }

        /// <summary>
        /// Create a buffer for the given input, matching the requirements of the compiled model.
        /// </summary>
        /// <param name="signatureIndex">The signature index</param>
        /// <param name="inputIndex">The input index</param>
        /// <returns>The input buffer</returns>
        public TensorBuffer CreateInputBuffer(int signatureIndex, int inputIndex)
        {
            LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetCompiledModelInputBufferRequirements(
                _ptr, new UIntPtr((uint)signatureIndex), new UIntPtr((uint)inputIndex), out IntPtr requirements));
            RankedTensorType tensorType = _model.GetSignature(signatureIndex).GetInputTensorType(inputIndex);
            return CreateBufferFromRequirements(tensorType, requirements);
        }

        /// <summary>
        /// Create a buffer for the given output, matching the requirements of the compiled model.
        /// </summary>
        /// <param name="signatureIndex">The signature index</param>
        /// <param name="outputIndex">The output index</param>
        /// <returns>The output buffer</returns>
        public TensorBuffer CreateOutputBuffer(int signatureIndex, int outputIndex)
        {
            RankedTensorType tensorType = _model.GetSignature(signatureIndex).GetOutputTensorType(outputIndex);
            if (!HasKnownShape(tensorType))
            {
                // The model file doesn't record this output's shape (e.g. the outputs of a custom op such as
                // TFLite_Detection_PostProcess, "Float32[]"), so the buffer requirements are for a single value; the
                // compiled model knows the actual shape.
                RankedTensorType resolved = GetOutputTensorTypes(signatureIndex, true)[outputIndex];
                if (HasKnownShape(resolved) && resolved.Rank > 0)
                {
                    long size = resolved.ElementCount * ElementSize(resolved.ElementType);
                    LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtCreateManagedTensorBuffer(
                        _environment, TensorBufferType.HostMemory, ref resolved, new UIntPtr((ulong)size), out IntPtr buffer));
                    return new TensorBuffer(buffer);
                }
            }
            LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetCompiledModelOutputBufferRequirements(
                _ptr, new UIntPtr((uint)signatureIndex), new UIntPtr((uint)outputIndex), out IntPtr requirements));
            return CreateBufferFromRequirements(tensorType, requirements);
        }

        /// <summary>
        /// The types of a signature's outputs with the shapes the compiled model resolved, which the model file may not
        /// record (Signature.GetOutputTensorType then has no dimensions).
        /// </summary>
        /// <param name="signatureIndex">The signature index</param>
        /// <param name="updateAllocation">Whether LiteRT should (re)allocate the model's tensors first, so shapes that are
        /// only known once they are allocated - e.g. the outputs of TFLite_Detection_PostProcess, before the first run -
        /// are resolved</param>
        /// <returns>The output tensor types, in the signature's output order</returns>
        public RankedTensorType[] GetOutputTensorTypes(int signatureIndex = 0, bool updateAllocation = false)
        {
            Signature signature = _model.GetSignature(signatureIndex);
            int count = signature.OutputCount;
            RankedTensorType[] types = new RankedTensorType[count];
            if (count == 0)
                return types;
            // LiteRtLayout: rank:7 + has_strides:1 bits in a 32-bit unit, int32 dimensions[8], uint32 strides[8].
            const int layoutSize = 4 + 4 * RankedTensorType.MaxRank * 2;
            IntPtr layouts = Marshal.AllocHGlobal(layoutSize * count);
            try
            {
                LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetCompiledModelOutputTensorLayouts(
                    _ptr, new UIntPtr((uint)signatureIndex), new UIntPtr((uint)count), layouts, updateAllocation));
                for (int i = 0; i < count; i++)
                {
                    IntPtr layout = layouts + i * layoutSize;
                    int rank = Marshal.ReadInt32(layout) & 0x7F;
                    int[] dimensions = new int[rank];
                    for (int d = 0; d < rank; d++)
                        dimensions[d] = Marshal.ReadInt32(layout, 4 + 4 * d);
                    types[i] = new RankedTensorType(signature.GetOutputTensorType(i).ElementType, dimensions);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(layouts);
            }
            return types;
        }

        // A shape with a known rank and no dynamic (negative) dimension.
        private static bool HasKnownShape(RankedTensorType tensorType)
        {
            if (tensorType.Rank == 0)
                return false;
            foreach (int dimension in tensorType.Dimensions)
                if (dimension < 0)
                    return false;
            return true;
        }

        private static long ElementSize(ElementType elementType)
        {
            switch (elementType)
            {
                case ElementType.Bool:
                case ElementType.Int8:
                case ElementType.UInt8:
                    return 1;
                case ElementType.Int16:
                case ElementType.UInt16:
                case ElementType.Float16:
                case ElementType.BFloat16:
                    return 2;
                case ElementType.Int64:
                case ElementType.UInt64:
                case ElementType.Float64:
                case ElementType.Complex64:
                    return 8;
                default:
                    return 4;
            }
        }

        /// <summary>
        /// Create buffers for all the inputs of a signature.
        /// </summary>
        /// <param name="signatureIndex">The signature index</param>
        /// <returns>The input buffers</returns>
        public TensorBuffer[] CreateInputBuffers(int signatureIndex = 0)
        {
            TensorBuffer[] buffers = new TensorBuffer[_model.GetSignature(signatureIndex).InputCount];
            for (int i = 0; i < buffers.Length; i++)
                buffers[i] = CreateInputBuffer(signatureIndex, i);
            return buffers;
        }

        /// <summary>
        /// Create buffers for all the outputs of a signature.
        /// </summary>
        /// <param name="signatureIndex">The signature index</param>
        /// <returns>The output buffers</returns>
        public TensorBuffer[] CreateOutputBuffers(int signatureIndex = 0)
        {
            TensorBuffer[] buffers = new TensorBuffer[_model.GetSignature(signatureIndex).OutputCount];
            for (int i = 0; i < buffers.Length; i++)
                buffers[i] = CreateOutputBuffer(signatureIndex, i);
            return buffers;
        }

        /// <summary>
        /// Run the model synchronously.
        /// </summary>
        /// <param name="signatureIndex">The signature index</param>
        /// <param name="inputBuffers">The input buffers, in the signature's input order</param>
        /// <param name="outputBuffers">The output buffers, in the signature's output order</param>
        public void Run(int signatureIndex, TensorBuffer[] inputBuffers, TensorBuffer[] outputBuffers)
        {
            IntPtr[] inputs = ToPtrArray(inputBuffers);
            IntPtr[] outputs = ToPtrArray(outputBuffers);
            LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtRunCompiledModel(
                _ptr,
                new UIntPtr((uint)signatureIndex),
                new UIntPtr((uint)inputs.Length), inputs,
                new UIntPtr((uint)outputs.Length), outputs));
            GC.KeepAlive(inputBuffers);
            GC.KeepAlive(outputBuffers);
        }

        /// <summary>
        /// Run the first signature of the model synchronously.
        /// </summary>
        /// <param name="inputBuffers">The input buffers, in the signature's input order</param>
        /// <param name="outputBuffers">The output buffers, in the signature's output order</param>
        public void Run(TensorBuffer[] inputBuffers, TensorBuffer[] outputBuffers)
        {
            Run(0, inputBuffers, outputBuffers);
        }

        private TensorBuffer CreateBufferFromRequirements(RankedTensorType tensorType, IntPtr requirements)
        {
            LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtCreateManagedTensorBufferFromRequirements(
                _environment, ref tensorType, requirements, out IntPtr buffer));
            return new TensorBuffer(buffer);
        }

        private static IntPtr[] ToPtrArray(TensorBuffer[] buffers)
        {
            IntPtr[] ptrs = new IntPtr[buffers.Length];
            for (int i = 0; i < buffers.Length; i++)
                ptrs[i] = buffers[i].Ptr;
            return ptrs;
        }

        /// <summary>
        /// Release the unmanaged compiled model
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtInvoke.LiteRtDestroyCompiledModel(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    public static partial class LiteRtInvoke
    {
        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtCreateCompiledModel(IntPtr environment, IntPtr model, IntPtr options, out IntPtr compiledModel);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern void LiteRtDestroyCompiledModel(IntPtr compiledModel);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetCompiledModelInputBufferRequirements(
            IntPtr compiledModel, UIntPtr signatureIndex, UIntPtr inputIndex, out IntPtr bufferRequirements);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetCompiledModelOutputBufferRequirements(
            IntPtr compiledModel, UIntPtr signatureIndex, UIntPtr outputIndex, out IntPtr bufferRequirements);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetCompiledModelOutputTensorLayouts(
            IntPtr compiledModel, UIntPtr signatureIndex, UIntPtr numLayouts, IntPtr layouts,
            [MarshalAs(BoolMarshalType)] bool updateAllocation);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtRunCompiledModel(
            IntPtr compiledModel,
            UIntPtr signatureIndex,
            UIntPtr numInputBuffers, IntPtr[] inputBuffers,
            UIntPtr numOutputBuffers, IntPtr[] outputBuffers);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtCompiledModelIsFullyAccelerated(
            IntPtr compiledModel,
            [MarshalAs(BoolMarshalType)] out bool fullyAccelerated);
    }
}
