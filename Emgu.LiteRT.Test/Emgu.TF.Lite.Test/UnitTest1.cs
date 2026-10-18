using System;
#if VS_TEST
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestAttribute = Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute;
using TestFixture = Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute;
#else
using NUnit.Framework;
#endif
using Emgu.TF.Lite;
using Emgu.TF.Lite.Models;
using Emgu.LiteRT.Util;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using LiteRt = Emgu.LiteRT;

namespace Emgu.TF.Lite.Test
{
    // Needs the native libraries (tfliteextern, libLiteRt): the CI test workflow, which runs without them, skips the
    // "Native" category.
    [TestFixture]
#if VS_TEST
    [TestCategory("Native")]
#else
    [Category("Native")]
#endif
    public class UnitTest1
    {
        [TestAttribute]
        public void TestGetVersion()
        {
            String version = TfLiteInvoke.Version;
            String emgucvBuildInfo = Emgu.CV.CvInvoke.BuildInformation;
        }

        [TestAttribute]
        public async Task TestMobilenet()
        {
            using (Mobilenet mobilenet = new Mobilenet())
            {
                await mobilenet.Init();
                var result = mobilenet.Recognize("grace_hopper.jpg")[0];
            }
        }

        // Runs MobileNet through the new LiteRt CompiledModel API (libLiteRt, P/Invoked directly) and checks it
        // against the existing Interpreter API (tfliteextern) on the same input. The LiteRt alias avoids
        // Emgu.LiteRT.Environment clashing with System.Environment.
        [TestAttribute]
        public async Task TestLiteRtMobilenet()
        {
            await RunLiteRtMobilenet(LiteRt.HwAccelerators.Cpu, 1e-4);
        }

        // The same on the GPU, with LiteRT's GPU accelerator plugin next to libLiteRt (on macOS
        // libLiteRtMetalAccelerator.dylib, copied by Emgu.LiteRT.Runtime/Mac when it exists): skipped without it. The
        // GPU computes in lower precision, so the outputs only have to be close.
        [TestAttribute]
        public async Task TestLiteRtMobilenetGpu()
        {
            if (!StageGpuPlugin())
            {
#if VS_TEST
                Assert.Inconclusive("No LiteRT GPU accelerator plugin next to libLiteRt");
#else
                Assert.Ignore("No LiteRT GPU accelerator plugin next to libLiteRt");
#endif
            }
            await RunLiteRtMobilenet(LiteRt.HwAccelerators.Gpu, 1e-2);
        }

        // True if a LiteRT GPU accelerator plugin is next to libLiteRt. On Windows the plugin is not in the normal
        // runtime package: it needs the matching source-built bundle (platforms/windows/bazel_build_windows_gpu.ps1,
        // by default .tmp/wgpu/artifacts), whose folder EMGU_LITERT_WEBGPU_DIR names; its DLLs are copied next to the
        // test (replacing libLiteRt.dll fails once it is loaded, so run this test on its own with --filter).
        private static bool StageGpuPlugin()
        {
            string baseDir = AppContext.BaseDirectory;
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                string bundle = System.Environment.GetEnvironmentVariable("EMGU_LITERT_WEBGPU_DIR");
                if (!String.IsNullOrEmpty(bundle) && System.IO.Directory.Exists(bundle))
                {
                    foreach (string name in new[] { "libLiteRt.dll", "libLiteRtWebGpuAccelerator.dll", "webgpu_dawn.dll",
                        "dxcompiler.dll", "dxil.dll", "d3dcompiler_47.dll" })
                    {
                        string target = System.IO.Path.Combine(baseDir, name);
                        if (System.IO.File.Exists(target) && System.IO.File.Exists(System.IO.Path.Combine(bundle, name))
                            && System.IO.File.ReadAllBytes(target).AsSpan().SequenceEqual(System.IO.File.ReadAllBytes(System.IO.Path.Combine(bundle, name))))
                            continue;
                        System.IO.File.Copy(System.IO.Path.Combine(bundle, name), target, true);
                        new System.IO.FileInfo(target).IsReadOnly = false;
                    }
                }
                return System.IO.File.Exists(System.IO.Path.Combine(baseDir, "libLiteRtWebGpuAccelerator.dll"));
            }
            return System.IO.File.Exists(System.IO.Path.Combine(baseDir, "libLiteRtMetalAccelerator.dylib"));
        }

        private static async Task RunLiteRtMobilenet(LiteRt.HwAccelerators accelerators, double tolerance)
        {
            DownloadableFile modelFile = new DownloadableFile(
                "https://github.com/emgucv/models/raw/master/mobilenet_v1_1.0_224_float_2017_11_08/mobilenet_v1_1.0_224.tflite",
                "Mobilenet",
                "FDACE547B17907FA22821F4898F2AF49DCE7787FE688AD7C5D8D0220F3781C65");

            using (Mobilenet mobilenet = new Mobilenet())
            {
                await mobilenet.Init(modelFile);
                mobilenet.Recognize("grace_hopper.jpg");
                float[] input = (float[])mobilenet.InputTensor.Data;
                float[] expectedOutput = (float[])mobilenet.Interpreter.GetTensor(mobilenet.Interpreter.OutputIndices[0]).Data;

                using (LiteRt.Environment environment = new LiteRt.Environment())
                using (LiteRt.Model model = new LiteRt.Model(environment, modelFile.LocalFile))
                using (LiteRt.CompiledModel compiledModel = new LiteRt.CompiledModel(environment, model, accelerators))
                {
                    LiteRt.Signature signature = model.GetSignature(0);
                    Console.WriteLine("Signature '{0}': inputs {1}, outputs {2}",
                        signature.Key,
                        String.Join(", ", signature.InputNames),
                        String.Join(", ", signature.OutputNames));
                    Console.WriteLine("Input type: {0}; output type: {1}",
                        signature.GetInputTensorType(0),
                        signature.GetOutputTensorType(0));

                    LiteRt.TensorBuffer[] inputBuffers = compiledModel.CreateInputBuffers();
                    LiteRt.TensorBuffer[] outputBuffers = compiledModel.CreateOutputBuffers();
                    try
                    {
                        inputBuffers[0].Write(input);
                        compiledModel.Run(inputBuffers, outputBuffers);
                        float[] output = outputBuffers[0].Read<float>();

                        if (output.Length != expectedOutput.Length)
                            throw new Exception(String.Format("LiteRt output has {0} values, expected {1}", output.Length, expectedOutput.Length));
                        double maxDiff = 0;
                        for (int i = 0; i < output.Length; i++)
                            maxDiff = Math.Max(maxDiff, Math.Abs(output[i] - expectedOutput[i]));
                        int top = Array.IndexOf(output, output.Max());
                        int expectedTop = Array.IndexOf(expectedOutput, expectedOutput.Max());
                        Console.WriteLine("LiteRt top class {0} ({1}): {2}; Interpreter top class {3}: {4}; max abs diff {5}",
                            top, mobilenet.Labels[top], output[top], expectedTop, expectedOutput[expectedTop], maxDiff);
                        if (top != expectedTop || maxDiff > tolerance)
                            throw new Exception("LiteRt CompiledModel output does not match the Interpreter output");
                    }
                    finally
                    {
                        foreach (LiteRt.TensorBuffer b in inputBuffers)
                            b.Dispose();
                        foreach (LiteRt.TensorBuffer b in outputBuffers)
                            b.Dispose();
                    }
                }
            }
        }
    }
}
