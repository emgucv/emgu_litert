// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Text.Json;
using Emgu.LiteRT.LM.Agents.Events;
using Maui.Demo.Lite;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The record_clip alert action's parameters (TODO item 43): the length in seconds, its default and its limits. The recording itself is Android's
// Camera2 and was checked on a Galaxy S10 and a Pixel Fold.
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
        Assert.That(clip.Description, Does.Contain("Android only"));
    }

    [Test]
    public void ThePhotoActionTakesNoParametersAndIsNotTheSnapshot()
    {
        VisualActionDescriptor photo = VisualHostCatalog.Photo;
        Assert.That(photo.Id, Is.EqualTo("take_photo"));
        Assert.That(photo.Id, Is.Not.EqualTo(VisualHostCatalog.Snapshot.Id));
        Assert.That(photo.Available, Is.True);
        Assert.That(photo.ParameterSchema.GetRawText(), Does.Contain("\"properties\":{}").And.Contain("\"additionalProperties\":false"));
        Assert.That(photo.Description, Does.Contain("full-resolution").And.Contain("Android only"));
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
