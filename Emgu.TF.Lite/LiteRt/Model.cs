//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Emgu.TF.Lite.LiteRt
{
    /// <summary>
    /// A LiteRT model (LiteRtModel), loaded from a .tflite file or buffer.
    /// </summary>
    public class Model : Emgu.TF.Util.UnmanagedObject
    {
        private readonly Environment _environment;
        private byte[] _buffer;
        private GCHandle _bufferHandle;

        /// <summary>
        /// Load a model from a file.
        /// </summary>
        /// <param name="environment">The LiteRT environment</param>
        /// <param name="fileName">The .tflite file</param>
        public Model(Environment environment, String fileName)
        {
            if (!File.Exists(fileName))
                throw new FileNotFoundException(String.Format("File {0} does not exist", fileName), fileName);
            _environment = environment;
            LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtCreateModelFromFile(environment, fileName, out _ptr));
        }

        /// <summary>
        /// Load a model from a buffer. The buffer is copied and pinned for the lifetime of the model, as
        /// required by LiteRT.
        /// </summary>
        /// <param name="environment">The LiteRT environment</param>
        /// <param name="buffer">The content of a .tflite file</param>
        public Model(Environment environment, byte[] buffer)
        {
            _environment = environment;
            _buffer = (byte[])buffer.Clone();
            _bufferHandle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
            try
            {
                LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtCreateModelFromBuffer(
                    environment,
                    _bufferHandle.AddrOfPinnedObject(),
                    new UIntPtr((ulong)_buffer.Length),
                    out _ptr));
            }
            catch
            {
                _bufferHandle.Free();
                _buffer = null;
                throw;
            }
        }

        /// <summary>
        /// The environment this model was loaded with
        /// </summary>
        public Environment Environment
        {
            get { return _environment; }
        }

        /// <summary>
        /// The number of signatures in the model
        /// </summary>
        public int SignatureCount
        {
            get
            {
                LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetNumModelSignatures(_ptr, out UIntPtr count));
                return (int)count.ToUInt64();
            }
        }

        /// <summary>
        /// Get the signature at the given index. The signature is only valid during the model's lifetime.
        /// </summary>
        /// <param name="index">The signature index</param>
        /// <returns>The signature</returns>
        public Signature GetSignature(int index)
        {
            LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetModelSignature(_ptr, new UIntPtr((uint)index), out IntPtr signature));
            return new Signature(this, signature);
        }

        /// <summary>
        /// All the signatures in the model.
        /// </summary>
        public Signature[] Signatures
        {
            get
            {
                Signature[] signatures = new Signature[SignatureCount];
                for (int i = 0; i < signatures.Length; i++)
                    signatures[i] = GetSignature(i);
                return signatures;
            }
        }

        /// <summary>
        /// Release the unmanaged model
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtInvoke.LiteRtDestroyModel(_ptr);
                _ptr = IntPtr.Zero;
            }
            if (_buffer != null)
            {
                _bufferHandle.Free();
                _buffer = null;
            }
        }
    }

    /// <summary>
    /// A model signature (LiteRtSignature): a named entry point with named inputs and outputs.
    /// It is owned by, and only valid during the lifetime of, its Model.
    /// </summary>
    public class Signature
    {
        private readonly Model _model;
        private readonly IntPtr _ptr;

        internal Signature(Model model, IntPtr ptr)
        {
            _model = model;
            _ptr = ptr;
        }

        /// <summary>
        /// The pointer to the unmanaged LiteRtSignature
        /// </summary>
        public IntPtr Ptr
        {
            get { return _ptr; }
        }

        /// <summary>
        /// The signature key
        /// </summary>
        public String Key
        {
            get
            {
                LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetSignatureKey(_ptr, out IntPtr key));
                return Marshal.PtrToStringAnsi(key);
            }
        }

        /// <summary>
        /// The number of inputs
        /// </summary>
        public int InputCount
        {
            get
            {
                LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetNumSignatureInputs(_ptr, out UIntPtr count));
                return (int)count.ToUInt64();
            }
        }

        /// <summary>
        /// The number of outputs
        /// </summary>
        public int OutputCount
        {
            get
            {
                LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetNumSignatureOutputs(_ptr, out UIntPtr count));
                return (int)count.ToUInt64();
            }
        }

        /// <summary>
        /// The names of the inputs
        /// </summary>
        public String[] InputNames
        {
            get
            {
                String[] names = new String[InputCount];
                for (int i = 0; i < names.Length; i++)
                {
                    LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetSignatureInputName(_ptr, new UIntPtr((uint)i), out IntPtr name));
                    names[i] = Marshal.PtrToStringAnsi(name);
                }
                return names;
            }
        }

        /// <summary>
        /// The names of the outputs
        /// </summary>
        public String[] OutputNames
        {
            get
            {
                String[] names = new String[OutputCount];
                for (int i = 0; i < names.Length; i++)
                {
                    LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetSignatureOutputName(_ptr, new UIntPtr((uint)i), out IntPtr name));
                    names[i] = Marshal.PtrToStringAnsi(name);
                }
                return names;
            }
        }

        /// <summary>
        /// Get the tensor type of the input at the given index
        /// </summary>
        /// <param name="index">The input index</param>
        /// <returns>The tensor type</returns>
        public RankedTensorType GetInputTensorType(int index)
        {
            LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetSignatureInputTensorByIndex(_ptr, new UIntPtr((uint)index), out IntPtr tensor));
            return GetRankedTensorType(tensor);
        }

        /// <summary>
        /// Get the tensor type of the output at the given index
        /// </summary>
        /// <param name="index">The output index</param>
        /// <returns>The tensor type</returns>
        public RankedTensorType GetOutputTensorType(int index)
        {
            LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetSignatureOutputTensorByIndex(_ptr, new UIntPtr((uint)index), out IntPtr tensor));
            return GetRankedTensorType(tensor);
        }

        private static RankedTensorType GetRankedTensorType(IntPtr tensor)
        {
            LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetRankedTensorType(tensor, out RankedTensorType type));
            return type;
        }
    }

    public static partial class LiteRtInvoke
    {
        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtCreateModelFromFile(
            IntPtr environment,
            [MarshalAs(StringMarshalType)] String fileName,
            out IntPtr model);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtCreateModelFromBuffer(IntPtr environment, IntPtr buffer, UIntPtr bufferSize, out IntPtr model);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern void LiteRtDestroyModel(IntPtr model);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetNumModelSignatures(IntPtr model, out UIntPtr numSignatures);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetModelSignature(IntPtr model, UIntPtr signatureIndex, out IntPtr signature);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetSignatureKey(IntPtr signature, out IntPtr signatureKey);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetNumSignatureInputs(IntPtr signature, out UIntPtr numInputs);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetSignatureInputName(IntPtr signature, UIntPtr inputIndex, out IntPtr inputName);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetSignatureInputTensorByIndex(IntPtr signature, UIntPtr inputIndex, out IntPtr tensor);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetNumSignatureOutputs(IntPtr signature, out UIntPtr numOutputs);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetSignatureOutputName(IntPtr signature, UIntPtr outputIndex, out IntPtr outputName);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetSignatureOutputTensorByIndex(IntPtr signature, UIntPtr outputIndex, out IntPtr tensor);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetRankedTensorType(IntPtr tensor, out RankedTensorType rankedTensorType);
    }
}
