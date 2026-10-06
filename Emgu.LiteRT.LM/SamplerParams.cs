//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// The sampler parameters of a session (LiteRtLmSamplerParams).
    /// </summary>
    /// <remarks>The C API has no getters, so the properties return the values last set through this object
    /// (the type's default value before that, not LiteRT-LM's own default).</remarks>
    public class SamplerParams : Emgu.LiteRT.Util.UnmanagedObject
    {
        private int _topK;
        private float _topP;
        private float _temperature;
        private int _seed;

        /// <summary>
        /// Create sampler parameters of the given type.
        /// </summary>
        /// <param name="type">The sampler type</param>
        public SamplerParams(SamplerType type)
        {
            Type = type;
            _ptr = LiteRtLmInvoke.CheckPtr(LiteRtLmInvoke.litert_lm_sampler_params_create(type), "litert_lm_sampler_params_create");
        }

        /// <summary>
        /// The sampler type
        /// </summary>
        public SamplerType Type { get; }

        /// <summary>
        /// The number of most likely tokens to sample from (top-k sampling)
        /// </summary>
        public int TopK
        {
            get { return _topK; }
            set { LiteRtLmInvoke.litert_lm_sampler_params_set_top_k(_ptr, value); _topK = value; }
        }

        /// <summary>
        /// The cumulative probability of the tokens to sample from (top-p sampling)
        /// </summary>
        public float TopP
        {
            get { return _topP; }
            set { LiteRtLmInvoke.litert_lm_sampler_params_set_top_p(_ptr, value); _topP = value; }
        }

        /// <summary>
        /// The sampling temperature
        /// </summary>
        public float Temperature
        {
            get { return _temperature; }
            set { LiteRtLmInvoke.litert_lm_sampler_params_set_temperature(_ptr, value); _temperature = value; }
        }

        /// <summary>
        /// The random seed
        /// </summary>
        public int Seed
        {
            get { return _seed; }
            set { LiteRtLmInvoke.litert_lm_sampler_params_set_seed(_ptr, value); _seed = value; }
        }

        /// <summary>
        /// Release the unmanaged sampler parameters
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_sampler_params_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    public static partial class LiteRtLmInvoke
    {
        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_sampler_params_create(SamplerType type);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_sampler_params_delete(IntPtr parameters);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_sampler_params_set_top_k(IntPtr parameters, int topK);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_sampler_params_set_top_p(IntPtr parameters, float topP);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_sampler_params_set_temperature(IntPtr parameters, float temperature);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_sampler_params_set_seed(IntPtr parameters, int seed);
    }
}
