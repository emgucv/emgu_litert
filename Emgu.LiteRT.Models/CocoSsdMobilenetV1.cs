//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// COCO SSD MobileNet v1 (quantized, 300x300) object detector for the 80 COCO
    /// classes, run with LiteRT's CompiledModel API. The counterpart of Emgu.TF.Lite.Models.CocoSsdMobilenetV1.
    /// </summary>
    public class CocoSsdMobilenetV1 : CocoSsdMobilenet
    {
        /// <summary>
        /// Download the model if it isn't already, and compile it.
        /// </summary>
        /// <param name="modelFile">The .tflite model, or null for COCO SSD MobileNet v1 (quantized, 300x300)</param>
        /// <param name="labelFile">The labels, or null for its COCO labels</param>
        /// <param name="accelerators">The hardware accelerators to compile the model for</param>
        /// <returns>Async task</returns>
        public override Task Init(
            DownloadableFile modelFile = null,
            DownloadableFile labelFile = null,
            HwAccelerators accelerators = HwAccelerators.Cpu)
        {
            String defaultLocalSubfolder = "CocoSsdMobilenetV1";
            if (modelFile == null)
            {
                modelFile = new DownloadableFile(
                    "https://github.com/emgucv/models/raw/master/coco_ssd_mobilenet_v1_1.0_quant_2018_06_29/detect.tflite",
                    defaultLocalSubfolder,
                    "E4B118E5E4531945DE2E659742C7C590F7536F8D0ED26D135ABCFE83B4779D13");
            }
            if (labelFile == null)
            {
                labelFile = new DownloadableFile(
                    "https://github.com/emgucv/models/raw/master/coco_ssd_mobilenet_v1_1.0_quant_2018_06_29/labelmap.txt",
                    defaultLocalSubfolder,
                    "C7E79C855F73CBBA9F33D649D60E1676EB0A974021A41696D1AC0D4B7F7E0211");
            }
            return InitCocoSsd(modelFile, labelFile, accelerators);
        }
    }
}
