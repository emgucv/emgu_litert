//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// COCO SSD MobileNet v3 small (quantized, 320x320) object detector for the 80 COCO
    /// classes, run with LiteRT's CompiledModel API. The counterpart of Emgu.TF.Lite.Models.CocoSsdMobilenetV3.
    /// </summary>
    public class CocoSsdMobilenetV3 : CocoSsdMobilenet
    {
        /// <summary>
        /// Download the model if it isn't already, and compile it.
        /// </summary>
        /// <param name="modelFile">The .tflite model, or null for COCO SSD MobileNet v3 small (quantized, 320x320)</param>
        /// <param name="labelFile">The labels, or null for its COCO labels</param>
        /// <param name="accelerators">The hardware accelerators to compile the model for</param>
        /// <returns>Async task</returns>
        public override Task Init(
            DownloadableFile modelFile = null,
            DownloadableFile labelFile = null,
            HwAccelerators accelerators = HwAccelerators.Cpu)
        {
            String defaultLocalSubfolder = "CocoSsdMobilenetV3";
            if (modelFile == null)
            {
                modelFile = new DownloadableFile(
                    "https://github.com/emgucv/models/raw/master/coco_ssd_mobilenet_v3_small_2020_01_14/model.tflite",
                    defaultLocalSubfolder,
                    "0F52F7A4884DD6426D38F4AFC06DA75105EFE77F7C83E470254C267FE34CC43C");
            }
            if (labelFile == null)
            {
                labelFile = new DownloadableFile(
                    "https://github.com/emgucv/models/raw/master/coco_ssd_mobilenet_v3_small_2020_01_14/labelmap.txt",
                    defaultLocalSubfolder,
                    "C7E79C855F73CBBA9F33D649D60E1676EB0A974021A41696D1AC0D4B7F7E0211");
            }
            return InitCocoSsd(modelFile, labelFile, accelerators);
        }
    }
}
