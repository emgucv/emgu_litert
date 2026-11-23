// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Text.Json;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Agents.CV;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The record_clip alert action's parameters (TODO item 43): the length in seconds, its default and its limits, and what the planner is told per platform.
// The recording itself is Android's Camera2 (checked on a Galaxy S10 and a Pixel Fold), iOS's AVFoundation and, on Windows, OpenCV's writer.
[TestFixture]
public class UnitTestClipAction
{
    private static int Seconds(string json, out string normalized)
    {
        int seconds = VisualHostCatalog.ClipSeconds(VisualActionParameters.Parse(json), out JsonElement element);
        normalized = element.GetRawText();
        return seconds;
    }

    [Test]
    public void TheLengthDefaultsToTenSecondsAndIsNormalized()
    {
        Assert.That(Seconds("{}", out string normalized), Is.EqualTo(10));
        Assert.That(normalized, Is.EqualTo("{\"seconds\":10}"));
        Assert.That(Seconds("{\"seconds\":5}", out normalized), Is.EqualTo(5));
        Assert.That(normalized, Is.EqualTo("{\"seconds\":5}"));
        Assert.That(Seconds("{\"seconds\":3}", out _), Is.EqualTo(3));
        Assert.That(Seconds("{\"seconds\":30}", out _), Is.EqualTo(30));
    }

    [TestCase("{\"seconds\":2}")]
    [TestCase("{\"seconds\":31}")]
    [TestCase("{\"seconds\":0}")]
    [TestCase("{\"seconds\":-5}")]
    [TestCase("{\"seconds\":7.5}")]
    [TestCase("{\"seconds\":\"10\"}")]
    [TestCase("{\"seconds\":null}")]
    [TestCase("{\"minutes\":1}")]
    [TestCase("{\"seconds\":10,\"audio\":true}")]
    [TestCase("[10]")]
    public void ALengthOutsideTheLimitsOrAnythingElseIsRejected(string json)
    {
        Assert.Throws<ArgumentException>(() => Seconds(json, out _));
    }

    [Test]
    public void TheDescriptorTellsThePlannerTheLimits()
    {
        VisualActionDescriptor clip = VisualHostCatalog.Clip;
        Assert.That(clip.Id, Is.EqualTo("record_clip"));
        Assert.That(clip.Available, Is.True);
        string schema = clip.ParameterSchema.GetRawText();
        Assert.That(schema, Does.Contain("\"seconds\"").And.Contain("\"minimum\":3").And.Contain("\"maximum\":30").And.Contain("\"additionalProperties\":false"));
        Assert.That(clip.Description, Does.Contain("Android, iOS and Windows only").And.Contain("no sound on Windows"));
    }

    [Test]
    public void ThePhotoActionTakesNoParametersAndIsNotTheSnapshot()
    {
        VisualActionDescriptor photo = VisualHostCatalog.Photo;
        Assert.That(photo.Id, Is.EqualTo("take_photo"));
        Assert.That(photo.Id, Is.Not.EqualTo(VisualHostCatalog.Snapshot.Id));
        Assert.That(photo.Available, Is.True);
        Assert.That(photo.ParameterSchema.GetRawText(), Does.Contain("\"properties\":{}").And.Contain("\"additionalProperties\":false"));
        Assert.That(photo.Description, Does.Contain("full-resolution").And.Contain("Android, iOS and Windows only"));
    }

    // Windows records clips and takes photos but has no selfie camera to switch to, and its clips have no sound.
    [Test]
    public void ThePlannerIsToldOnlyWhatThePlatformCanDo()
    {
        string phone = VisualHostCatalog.PlannerInstructions(cameraActions: true);
        Assert.That(phone, Does.Contain("selfie (front) camera").And.Contain("record_clip").And.Contain("take_photo").And.Contain("with sound"));
        Assert.That(VisualHostCatalog.PlannerInstructions(switchCameras: true, photosAndClips: true, clipSound: true), Is.EqualTo(phone));

        string windows = VisualHostCatalog.PlannerInstructions(switchCameras: false, photosAndClips: true, clipSound: false);
        Assert.That(windows, Does.Not.Contain("selfie (front) camera"));
        Assert.That(windows, Does.Contain("record_clip").And.Contain("take_photo").And.Contain("without sound"));
        Assert.That(windows, Does.Not.Contain("with sound"));

        string none = VisualHostCatalog.PlannerInstructions(cameraActions: false);
        Assert.That(none, Does.Not.Contain("selfie (front) camera").And.Not.Contain("record_clip").And.Not.Contain("take_photo"));
    }

    [Test]
    public void TheRequestShownOnACardIsOneCleanLine()
    {
        Assert.That(VisualHostCatalog.CleanRequest("  When you see a person,\n take a   photo.  "), Is.EqualTo("When you see a person, take a photo."));
        Assert.That(VisualHostCatalog.CleanRequest(""), Is.Null);
        Assert.That(VisualHostCatalog.CleanRequest("  \n\t "), Is.Null);
        Assert.That(VisualHostCatalog.CleanRequest(null), Is.Null);
        string cut = VisualHostCatalog.CleanRequest(new string('a', 500));
        Assert.That(cut.Length, Is.EqualTo(VisualHostCatalog.MaximumRequestLength));
        Assert.That(cut, Does.EndWith("…"));
        Assert.That(VisualHostCatalog.CleanRequest(new string('b', 300)).Length, Is.EqualTo(300));
    }
}
