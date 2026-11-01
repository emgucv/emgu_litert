// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.IO;
using Emgu.LiteRT.LM;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Native errors come back as typed exceptions thrown after the call returns (Emgu CV's pattern): the native side
// records a status and a message for the calling thread, nothing is thrown through native frames, and the process
// survives a bad model file.
[TestFixture, Category("Native")]
public class UnitTestNativeErrors
{
    private static string TempFile(byte[] content, string extension)
    {
        string path = Path.Combine(Path.GetTempPath(), "emgu-native-errors-" + Guid.NewGuid().ToString("N") + extension);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Test]
    public void LiteRtLmReportsAStatusAndMessage()
    {
        Assert.That(LiteRtLmInvoke.ApiVersion, Is.Not.Null, "liblitert-lm with C API 1.0.0 or later");
        Assert.That(LiteRtLmInvoke.ApiVersion.Major, Is.GreaterThanOrEqualTo(1));
        string missing = Path.Combine(Path.GetTempPath(), "no-such-model-" + Guid.NewGuid().ToString("N") + ".litertlm");
        LiteRtLmException e = Assert.Throws<LiteRtLmException>(() => new LoadedFile(missing).Dispose());
        Assert.That(e.Status, Is.Not.EqualTo(LiteRtLmStatusCode.Ok));
        Assert.That(e.FunctionName, Is.EqualTo("litert_lm_loaded_file_create"));
        Assert.That(e.ErrorMessage, Is.Not.Null.And.Not.Empty);
        TestContext.Progress.WriteLine("{0}: {1}", e.Status, e.Message);
        // A corrupt file: an error, not a crash; and the next call isn't blamed for it.
        string corrupt = TempFile(new byte[4096], ".litertlm");
        try
        {
            e = Assert.Throws<LiteRtLmException>(() => new LoadedFile(corrupt).Dispose());
            TestContext.Progress.WriteLine("{0}: {1}", e.Status, e.Message);
        }
        finally
        {
            File.Delete(corrupt);
        }
    }

    [Test]
    public void TfLiteThrowsForACorruptModelWithoutCrashing()
    {
        string corrupt = TempFile(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }, ".tflite");
        try
        {
            TfLiteException e = Assert.Throws<TfLiteException>(() =>
            {
                using (FlatBufferModel model = new FlatBufferModel(corrupt))
                using (Interpreter interpreter = new Interpreter(model))
                {
                }
            });
            Assert.That(e.ErrorMessage, Is.Not.Null.And.Not.Empty);
            TestContext.Progress.WriteLine(e.Message);
            // Read and cleared by the throw.
            Assert.That(TfLiteInvoke.LastErrorMessage, Is.Null);
        }
        finally
        {
            File.Delete(corrupt);
        }
    }
}
