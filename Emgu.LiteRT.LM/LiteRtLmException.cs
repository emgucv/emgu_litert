//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// The exception thrown when a LiteRT-LM C API call fails, or a streaming generation reports an error.
    /// </summary>
    public class LiteRtLmException : Exception
    {
        /// <summary>
        /// Create a LiteRtLmException
        /// </summary>
        /// <param name="message">The error message</param>
        public LiteRtLmException(String message)
            : base(message)
        {
        }
    }
}
