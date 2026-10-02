//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;

namespace Emgu.TF.Lite.LiteRt
{
    /// <summary>
    /// The exception thrown when a LiteRt C API call returns a status other than Ok.
    /// </summary>
    public class LiteRtException : Exception
    {
        /// <summary>
        /// The status returned by the failing LiteRt C API call
        /// </summary>
        public Status Status { get; }

        /// <summary>
        /// Create a LiteRtException from a status code
        /// </summary>
        /// <param name="status">The status returned by the failing LiteRt C API call</param>
        public LiteRtException(Status status)
            : base(String.Format("LiteRT error {0}: {1}", status, LiteRtInvoke.GetStatusString(status)))
        {
            Status = status;
        }
    }
}
