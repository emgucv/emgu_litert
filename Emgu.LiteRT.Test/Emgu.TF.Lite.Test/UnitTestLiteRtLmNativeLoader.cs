// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.IO;
using System.Runtime.InteropServices;
using Emgu.LiteRT.LM;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Where LiteRtLmNativeLoader looks for liblitert-lm.dll on Windows: the app's folder, then runtimes\win-<arch>\native
// (where the MAUI demo's copied DLLs are). Path logic only, with empty files: no native library is loaded.
[TestFixture]
public class UnitTestLiteRtLmNativeLoader
{
    private string _folder;

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), "emgu_loader_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_folder, true);
    }

    private string Touch(params string[] parts)
    {
        string path = Path.Combine(_folder, Path.Combine(parts));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, new byte[0]);
        return path;
    }

    [Test]
    public void FindsTheDllInTheRuntimesFolderOfTheArchitecture()
    {
        string x64 = Touch("runtimes", "win-x64", "native", "liblitert-lm.dll");
        string arm64 = Touch("runtimes", "win-arm64", "native", "liblitert-lm.dll");
        Assert.That(LiteRtLmNativeLoader.FindNativeLibrary(_folder, "liblitert-lm.dll", Architecture.X64), Is.EqualTo(x64));
        Assert.That(LiteRtLmNativeLoader.FindNativeLibrary(_folder, "liblitert-lm.dll", Architecture.Arm64), Is.EqualTo(arm64));
        Assert.That(LiteRtLmNativeLoader.FindNativeLibrary(_folder, "liblitert-lm.dll", Architecture.X86), Is.Null);
    }

    [Test]
    public void TheAppFolderComesFirst()
    {
        Touch("runtimes", "win-x64", "native", "liblitert-lm.dll");
        string inApp = Touch("liblitert-lm.dll");
        Assert.That(LiteRtLmNativeLoader.FindNativeLibrary(_folder, "liblitert-lm.dll", Architecture.X64), Is.EqualTo(inApp));
    }

    [Test]
    public void NothingWhenTheDllIsMissing()
    {
        Touch("runtimes", "win-x64", "native", "libLiteRt.dll");
        Assert.That(LiteRtLmNativeLoader.FindNativeLibrary(_folder, "liblitert-lm.dll", Architecture.X64), Is.Null);
        Assert.That(LiteRtLmNativeLoader.FindNativeLibrary(null, "liblitert-lm.dll", Architecture.X64), Is.Null);
        Assert.That(LiteRtLmNativeLoader.FindNativeLibrary(_folder, "liblitert-lm.dll", Architecture.Wasm), Is.Null);
    }
}
