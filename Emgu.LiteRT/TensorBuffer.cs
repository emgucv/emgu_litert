//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT
{
    /// <summary>
    /// A buffer holding the data of an input or output tensor (LiteRtTensorBuffer).
    /// </summary>
    public class TensorBuffer : Emgu.LiteRT.Util.UnmanagedObject
    {
        internal TensorBuffer(IntPtr ptr)
        {
            _ptr = ptr;
        }

        /// <summary>
        /// Create a tensor buffer whose memory is allocated and owned by LiteRT.
        /// </summary>
        /// <param name="environment">The LiteRT environment</param>
        /// <param name="bufferType">The type of memory to allocate</param>
        /// <param name="tensorType">The tensor type</param>
        /// <param name="bufferSize">The buffer size in bytes</param>
        public TensorBuffer(Environment environment, TensorBufferType bufferType, RankedTensorType tensorType, long bufferSize)
        {
            LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtCreateManagedTensorBuffer(
                environment, bufferType, ref tensorType, new UIntPtr((ulong)bufferSize), out _ptr));
        }

        /// <summary>
        /// The type of memory backing this buffer
        /// </summary>
        public TensorBufferType BufferType
        {
            get
            {
                LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetTensorBufferType(_ptr, out TensorBufferType type));
                return type;
            }
        }

        /// <summary>
        /// The type of the tensor stored in this buffer
        /// </summary>
        public RankedTensorType TensorType
        {
            get
            {
                LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetTensorBufferTensorType(_ptr, out RankedTensorType type));
                return type;
            }
        }

        /// <summary>
        /// The allocated size of the buffer in bytes, which may include padding.
        /// </summary>
        public long Size
        {
            get
            {
                LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetTensorBufferSize(_ptr, out UIntPtr size));
                return (long)size.ToUInt64();
            }
        }

        /// <summary>
        /// The size of the tensor data in bytes, without padding.
        /// </summary>
        public long PackedSize
        {
            get
            {
                LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetTensorBufferPackedSize(_ptr, out UIntPtr size));
                return (long)size.ToUInt64();
            }
        }

        /// <summary>
        /// Lock the buffer for host (CPU) access. Call Unlock when done.
        /// </summary>
        /// <param name="lockMode">The lock mode</param>
        /// <returns>The host address of the buffer data</returns>
        public IntPtr Lock(TensorBufferLockMode lockMode)
        {
            LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtLockTensorBuffer(_ptr, out IntPtr hostMemory, lockMode));
            return hostMemory;
        }

        /// <summary>
        /// Unlock a buffer previously locked with Lock.
        /// </summary>
        public void Unlock()
        {
            LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtUnlockTensorBuffer(_ptr));
        }

        /// <summary>
        /// Copy the data into the buffer.
        /// </summary>
        /// <typeparam name="T">A primitive type, matching the element type of the tensor</typeparam>
        /// <param name="data">The data to write</param>
        public void Write<T>(T[] data) where T : struct
        {
            int byteCount = Buffer.ByteLength(data);
            long packedSize = PackedSize;
            if (byteCount > packedSize)
                throw new ArgumentException(String.Format(
                    "The data is {0} bytes, which is larger than the tensor size of {1} bytes", byteCount, packedSize));

            IntPtr hostMemory = Lock(TensorBufferLockMode.Write);
            try
            {
                CopyToNative(data, hostMemory, byteCount);
            }
            finally
            {
                Unlock();
            }
        }

        /// <summary>
        /// Copy the tensor data out of the buffer.
        /// </summary>
        /// <typeparam name="T">A primitive type, matching the element type of the tensor</typeparam>
        /// <returns>The tensor data</returns>
        public T[] Read<T>() where T : struct
        {
            T[] data = new T[PackedSize / Marshal.SizeOf(typeof(T))];
            IntPtr hostMemory = Lock(TensorBufferLockMode.Read);
            try
            {
                CopyFromNative(hostMemory, data, Buffer.ByteLength(data));
            }
            finally
            {
                Unlock();
            }
            return data;
        }

        private static void CopyToNative(Array data, IntPtr destination, int byteCount)
        {
            if (data is float[] f)
                Marshal.Copy(f, 0, destination, f.Length);
            else if (data is byte[] b)
                Marshal.Copy(b, 0, destination, b.Length);
            else if (data is int[] i)
                Marshal.Copy(i, 0, destination, i.Length);
            else if (data is short[] s)
                Marshal.Copy(s, 0, destination, s.Length);
            else if (data is long[] l)
                Marshal.Copy(l, 0, destination, l.Length);
            else if (data is double[] d)
                Marshal.Copy(d, 0, destination, d.Length);
            else
            {
                // Other primitive types (sbyte, ushort, uint, ...): stage through a byte array.
                byte[] staging = new byte[byteCount];
                Buffer.BlockCopy(data, 0, staging, 0, byteCount);
                Marshal.Copy(staging, 0, destination, byteCount);
            }
        }

        private static void CopyFromNative(IntPtr source, Array data, int byteCount)
        {
            if (data is float[] f)
                Marshal.Copy(source, f, 0, f.Length);
            else if (data is byte[] b)
                Marshal.Copy(source, b, 0, b.Length);
            else if (data is int[] i)
                Marshal.Copy(source, i, 0, i.Length);
            else if (data is short[] s)
                Marshal.Copy(source, s, 0, s.Length);
            else if (data is long[] l)
                Marshal.Copy(source, l, 0, l.Length);
            else if (data is double[] d)
                Marshal.Copy(source, d, 0, d.Length);
            else
            {
                byte[] staging = new byte[byteCount];
                Marshal.Copy(source, staging, 0, byteCount);
                Buffer.BlockCopy(staging, 0, data, 0, byteCount);
            }
        }

        /// <summary>
        /// Release the unmanaged tensor buffer
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtInvoke.LiteRtDestroyTensorBuffer(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    public static partial class LiteRtInvoke
    {
        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtCreateManagedTensorBuffer(
            IntPtr environment,
            TensorBufferType bufferType,
            ref RankedTensorType tensorType,
            UIntPtr bufferSize,
            out IntPtr buffer);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtCreateManagedTensorBufferFromRequirements(
            IntPtr environment,
            ref RankedTensorType tensorType,
            IntPtr requirements,
            out IntPtr buffer);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetTensorBufferType(IntPtr tensorBuffer, out TensorBufferType bufferType);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetTensorBufferTensorType(IntPtr tensorBuffer, out RankedTensorType tensorType);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetTensorBufferSize(IntPtr tensorBuffer, out UIntPtr size);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetTensorBufferPackedSize(IntPtr tensorBuffer, out UIntPtr size);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtLockTensorBuffer(IntPtr tensorBuffer, out IntPtr hostMemory, TensorBufferLockMode lockMode);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtUnlockTensorBuffer(IntPtr tensorBuffer);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern void LiteRtDestroyTensorBuffer(IntPtr tensorBuffer);
    }
}
