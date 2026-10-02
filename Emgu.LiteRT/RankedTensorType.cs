//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT
{
    /// <summary>
    /// Tensor type with a fixed rank (LiteRtRankedTensorType): the element type plus the shape.
    /// This mirrors the ABI-stable C struct (72 bytes on 64-bit platforms).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RankedTensorType
    {
        /// <summary>
        /// The maximum rank supported by LiteRT (LITERT_TENSOR_MAX_RANK)
        /// </summary>
        public const int MaxRank = 8;

        /// <summary>
        /// The element type
        /// </summary>
        public ElementType ElementType;

        // LiteRtLayout: "unsigned int rank : 7; unsigned int has_strides : 1;" packed into one 32-bit unit.
        private uint _rankAndFlags;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxRank)]
        private int[] _dimensions;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxRank)]
        private uint[] _strides;

        /// <summary>
        /// Create a ranked tensor type
        /// </summary>
        /// <param name="elementType">The element type</param>
        /// <param name="dimensions">The dimensions. At most MaxRank values.</param>
        public RankedTensorType(ElementType elementType, params int[] dimensions)
        {
            if (dimensions == null)
                dimensions = new int[0];
            if (dimensions.Length > MaxRank)
                throw new ArgumentException(String.Format("Rank {0} exceeds the maximum rank {1}", dimensions.Length, MaxRank));
            ElementType = elementType;
            _rankAndFlags = (uint)dimensions.Length;
            _dimensions = new int[MaxRank];
            Array.Copy(dimensions, _dimensions, dimensions.Length);
            _strides = new uint[MaxRank];
        }

        /// <summary>
        /// The number of dimensions
        /// </summary>
        public int Rank
        {
            get { return (int)(_rankAndFlags & 0x7F); }
        }

        /// <summary>
        /// True if the layout has strides
        /// </summary>
        public bool HasStrides
        {
            get { return (_rankAndFlags & 0x80) != 0; }
        }

        /// <summary>
        /// The dimensions. A dynamic dimension is reported as -1.
        /// </summary>
        public int[] Dimensions
        {
            get
            {
                int[] dims = new int[Rank];
                if (_dimensions != null)
                    Array.Copy(_dimensions, dims, dims.Length);
                return dims;
            }
        }

        /// <summary>
        /// The strides, empty if HasStrides is false.
        /// </summary>
        public uint[] Strides
        {
            get
            {
                if (!HasStrides || _strides == null)
                    return new uint[0];
                uint[] strides = new uint[Rank];
                Array.Copy(_strides, strides, strides.Length);
                return strides;
            }
        }

        /// <summary>
        /// The total number of elements, i.e. the product of all dimensions.
        /// </summary>
        public long ElementCount
        {
            get
            {
                long count = 1;
                foreach (int d in Dimensions)
                    count *= d;
                return count;
            }
        }

        /// <summary>
        /// Returns a string like "Float32[1,224,224,3]"
        /// </summary>
        /// <returns>The string representation of this tensor type</returns>
        public override string ToString()
        {
            return String.Format("{0}[{1}]", ElementType, String.Join(",", Dimensions));
        }
    }
}
