//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// One input of a prompt (LiteRtLmInputData): text, an encoded image or encoded audio. The data is copied when
    /// the input is created.
    /// </summary>
    public class InputData : Emgu.LiteRT.Util.UnmanagedObject
    {
        /// <summary>
        /// Create an input from raw bytes.
        /// </summary>
        /// <param name="type">The input type</param>
        /// <param name="data">The bytes: UTF-8 text, or the encoded image / audio file content. Ignored (may be null)
        /// for ImageEnd and AudioEnd.</param>
        public InputData(InputDataType type, byte[] data)
        {
            Type = type;
            byte[] bytes = data ?? new byte[0];
            _ptr = LiteRtLmInvoke.CheckPtr(
                LiteRtLmInvoke.litert_lm_input_data_create(type, bytes, new UIntPtr((uint)bytes.Length)),
                "litert_lm_input_data_create");
        }

        /// <summary>
        /// Create a text input.
        /// </summary>
        /// <param name="text">The text</param>
        public InputData(String text)
            : this(InputDataType.Text, Encoding.UTF8.GetBytes(text ?? String.Empty))
        {
        }

        /// <summary>
        /// The input type
        /// </summary>
        public InputDataType Type { get; }

        /// <summary>
        /// Create a text input.
        /// </summary>
        /// <param name="text">The text</param>
        /// <returns>The text input</returns>
        public static InputData FromText(String text)
        {
            return new InputData(text);
        }

        /// <summary>
        /// Create an image input from an encoded image file's content (e.g. JPEG or PNG bytes).
        /// </summary>
        /// <param name="encodedImage">The encoded image</param>
        /// <returns>The image input</returns>
        public static InputData FromImage(byte[] encodedImage)
        {
            return new InputData(InputDataType.Image, encodedImage);
        }

        /// <summary>
        /// Create an audio input from an encoded audio file's content.
        /// </summary>
        /// <param name="encodedAudio">The encoded audio</param>
        /// <returns>The audio input</returns>
        public static InputData FromAudio(byte[] encodedAudio)
        {
            return new InputData(InputDataType.Audio, encodedAudio);
        }

        /// <summary>
        /// Get the native pointers of the inputs, for a const LiteRtLmInputData* const* parameter.
        /// </summary>
        internal static IntPtr[] ToPtrArray(InputData[] inputs)
        {
            if (inputs == null)
                return new IntPtr[0];
            IntPtr[] ptrs = new IntPtr[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                if (inputs[i] == null)
                    throw new ArgumentNullException("inputs", String.Format("Input {0} is null", i));
                ptrs[i] = inputs[i].Ptr;
            }
            return ptrs;
        }

        /// <summary>
        /// Release the unmanaged input
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_input_data_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    public static partial class LiteRtLmInvoke
    {
        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_input_data_create(InputDataType type, byte[] data, UIntPtr size);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_input_data_delete(IntPtr inputData);
    }
}
