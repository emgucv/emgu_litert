//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Emgu.TF.Lite
{
    public partial class TfLiteInvoke
    {
        /// <summary>
        /// The file name of the tfliteextern library. On iOS it is tfliteextern.framework (linked to LiteRt.framework,
        /// LiteRT's runtime), named by its path; on Mac Catalyst and Unity iOS tfliteextern is a static library linked
        /// into the app, so its symbols are looked up there.
        /// </summary>
#if __IOS__ && !__MACCATALYST__ && !UNITY_IPHONE && !UNITY_EDITOR
        public const string ExternLibrary = "@rpath/tfliteextern.framework/tfliteextern";
#elif (__IOS__ || UNITY_IPHONE) && (!UNITY_EDITOR)
        public const string ExternLibrary = "__Internal";
#else
        public const string ExternLibrary = "tfliteextern";
#endif

        /// <summary>
        /// The List of the tensorflow lite modules
        /// </summary>
        public static List<String> TensorflowModuleList = new List<String>
        {
            ExternLibrary
        };

    }
}
