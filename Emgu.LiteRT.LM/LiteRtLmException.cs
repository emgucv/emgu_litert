//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// The exception thrown when a LiteRT-LM C API call fails, or a streaming generation reports an error. Like Emgu
    /// CV's CvException, it is thrown from managed code after the native call returned: the native side only reports a
    /// status and records a message.
    /// </summary>
    public class LiteRtLmException : Exception
    {
        /// <summary>
        /// Create a LiteRtLmException with a message only (Status is Unknown)
        /// </summary>
        /// <param name="message">The error message</param>
        public LiteRtLmException(String message)
            : base(message)
        {
            Status = LiteRtLmStatusCode.Unknown;
        }

        /// <summary>
        /// Create a LiteRtLmException for a failed C API call
        /// </summary>
        /// <param name="status">The status the call returned</param>
        /// <param name="functionName">The C API function</param>
        /// <param name="errorMessage">The message LiteRT-LM recorded, or null</param>
        public LiteRtLmException(LiteRtLmStatusCode status, String functionName, String errorMessage)
            : base(String.Format("LiteRT-LM: {0} failed ({1}): {2}", functionName, status,
                String.IsNullOrEmpty(errorMessage) ? "see the LiteRT-LM log for details" : errorMessage))
        {
            Status = status;
            FunctionName = functionName;
            ErrorMessage = errorMessage;
        }

        /// <summary>
        /// The status the failed call returned (Unknown when the exception has a message only)
        /// </summary>
        public LiteRtLmStatusCode Status { get; private set; }

        /// <summary>
        /// The C API function that failed, or null
        /// </summary>
        public String FunctionName { get; private set; }

        /// <summary>
        /// The message LiteRT-LM recorded for the failure, or null
        /// </summary>
        public String ErrorMessage { get; private set; }
    }
}
