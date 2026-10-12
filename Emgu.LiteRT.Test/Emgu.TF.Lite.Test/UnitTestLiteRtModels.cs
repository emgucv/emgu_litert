//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
#if VS_TEST
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestAttribute = Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute;
using TestFixture = Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute;
#else
using NUnit.Framework;
#endif
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emgu.LiteRT;
using LiteRtModels = Emgu.LiteRT.Models;
using TfLiteModels = Emgu.TF.Lite.Models;

namespace Emgu.TF.Lite.Test
{
    // Emgu.LiteRT.Models (LiteRT's CompiledModel API) against Emgu.TF.Lite.Models (the TFLite Interpreter API): the same
    // models and images should give the same results.
    [TestFixture]
    public class UnitTestLiteRtModels
    {
        private static void Skip(String message)
        {
#if VS_TEST
            Assert.Inconclusive(message);
#else
            Assert.Ignore(message);
#endif
        }

        private static bool HasGpuPlugin
        {
            get { return File.Exists(Path.Combine(AppContext.BaseDirectory, "libLiteRtMetalAccelerator.dylib")); }
        }

        [TestAttribute]
        public Task TestMobilenet()
        {
            return CompareMobilenet(HwAccelerators.Cpu, 1e-4);
        }

        [TestAttribute]
        public Task TestMobilenetGpu()
        {
            if (!HasGpuPlugin)
                Skip("No LiteRT GPU accelerator plugin next to libLiteRt");
            return CompareMobilenet(HwAccelerators.Gpu, 1e-2);
        }

        private static async Task CompareMobilenet(HwAccelerators accelerators, double tolerance)
        {
            using (LiteRtModels.Mobilenet compiled = new LiteRtModels.Mobilenet())
            using (TfLiteModels.Mobilenet interpreted = new TfLiteModels.Mobilenet())
            {
                await compiled.Init(accelerators: accelerators);
                await interpreted.Init();
                Console.WriteLine("Mobilenet input: {0}", compiled.InputTensorType);
                var a = compiled.Recognize("grace_hopper.jpg");
                var b = interpreted.Recognize("grace_hopper.jpg");
                Console.WriteLine("{0}: CompiledModel {1} {2:F4}, Interpreter {3} {4:F4}", accelerators,
                    a[0].Label, a[0].Probability, b[0].Label, b[0].Probability);
                if (a[0].Label != b[0].Label || Math.Abs(a[0].Probability - b[0].Probability) > tolerance)
                    throw new Exception("The CompiledModel result doesn't match the Interpreter's");
            }
        }

        [TestAttribute]
        public async Task TestInception()
        {
            using (LiteRtModels.Inception compiled = new LiteRtModels.Inception())
            using (TfLiteModels.Inception interpreted = new TfLiteModels.Inception())
            {
                await compiled.Init();
                await interpreted.Init();
                var a = compiled.Recognize("tulips.jpg");
                var b = interpreted.Recognize("tulips.jpg");
                Console.WriteLine("Inception: CompiledModel {0} {1:F4}, Interpreter {2} {3:F4}",
                    a[0].Label, a[0].Probability, b[0].Label, b[0].Probability);
                if (a[0].Label != b[0].Label || Math.Abs(a[0].Probability - b[0].Probability) > 1e-4)
                    throw new Exception("The CompiledModel result doesn't match the Interpreter's");
            }
        }

        [TestAttribute]
        public Task TestCocoSsdMobilenetV1()
        {
            return CompareCocoSsd(new LiteRtModels.CocoSsdMobilenetV1(), new TfLiteModels.CocoSsdMobilenetV1());
        }

        [TestAttribute]
        public Task TestCocoSsdMobilenetV3()
        {
            return CompareCocoSsd(new LiteRtModels.CocoSsdMobilenetV3(), new TfLiteModels.CocoSsdMobilenetV3());
        }

        private static async Task CompareCocoSsd(LiteRtModels.CocoSsdMobilenet compiled, TfLiteModels.CocoSsdMobilenet interpreted)
        {
            using (compiled)
            using (interpreted)
            {
                await compiled.Init();
                await interpreted.Init();
                Console.WriteLine("{0} input: {1}", compiled.GetType().Name, compiled.InputTensorType);
                var a = compiled.Recognize("surfers.jpg", 0.5f);
                Console.WriteLine("{0} outputs: {1}", compiled.GetType().Name,
                    String.Join(", ", compiled.CompiledModel.GetOutputTensorTypes().Select(t => t.ToString())));
                var b = interpreted.Recognize("surfers.jpg", 0.5f);
                Console.WriteLine("{0}: CompiledModel [{1}], Interpreter [{2}]", compiled.GetType().Name,
                    String.Join(", ", a.Select(r => String.Format("{0} {1:F2}", r.Label, r.Score))),
                    String.Join(", ", b.Select(r => String.Format("{0} {1:F2}", r.Label, r.Score))));
                if (a.Length == 0 || a.Length != b.Length)
                    throw new Exception("The CompiledModel detections don't match the Interpreter's");
                // Quantized models: the two APIs' integer kernels differ slightly (e.g. 0.74 vs 0.73 for V1).
                for (int i = 0; i < a.Length; i++)
                {
                    if (a[i].Label != b[i].Label || Math.Abs(a[i].Score - b[i].Score) > 0.02
                        || Enumerable.Range(0, 4).Any(k => Math.Abs(a[i].Rectangle[k] - b[i].Rectangle[k]) > 0.02))
                        throw new Exception("The CompiledModel detections don't match the Interpreter's");
                }
            }
        }
    }
}
