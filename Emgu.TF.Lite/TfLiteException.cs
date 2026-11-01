//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;

namespace Emgu.TF.Lite
{
    /// <summary>
    /// The exception thrown when TFLite reports an error. Like Emgu CV's CvException, it is thrown from managed code
    /// after the native call returned, with the message TFLite reported during it (tfliteextern records it for the
    /// calling thread): an exception can't be thrown from inside the native call.
    /// </summary>
    public class TfLiteException : Exception
    {
        /// <summary>
        /// Create a TfLiteException
        /// </summary>
        /// <param name="status">The status of the failed call</param>
        /// <param name="message">The message TFLite reported</param>
        public TfLiteException(Status status, String message)
            : base("TFLite: " + (String.IsNullOrEmpty(message) ? status.ToString() : message))
        {
            Status = status;
            ErrorMessage = message;
        }

        /// <summary>
        /// The status of the failed call
        /// </summary>
        public Status Status { get; private set; }

        /// <summary>
        /// The message TFLite reported, or null
        /// </summary>
        public String ErrorMessage { get; private set; }
    }
}
