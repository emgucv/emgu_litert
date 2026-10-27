// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Linq;
using Emgu.LiteRT.LM.Chat;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Emgu.LiteRT.LM.Chat's ChatPhotos.SelectMatches: which photos a search shows. The scores are ones measured with
// EmbeddingGemma 2 Text Vision at 140 vision tokens (each photo's similarity to the description, and to "a photo").
// No model.
[TestFixture]
public class UnitTestPhotoMatches
{
    [Test]
    public void NothingIsShownWhenNoPhotoStandsOut()
    {
        // "cat" with no cat: a dog and walls scored 0.62-0.65, which the old rule (0.6, within 0.04 of the best) showed.
        float[] scores = { 0.645f, 0.636f, 0.635f, 0.624f, 0.620f };
        float[] baseline = { 0.618f, 0.649f, 0.648f, 0.668f, 0.671f };
        Assert.That(ChatPhotos.SelectMatches(scores, baseline), Is.Empty);
    }

    [Test]
    public void TheRightPhotoIsShown()
    {
        // "dog": the dog, then walls and faces well behind.
        float[] scores = { 0.723f, 0.631f, 0.628f, 0.627f };
        float[] baseline = { 0.618f, 0.648f, 0.649f, 0.668f };
        Assert.That(ChatPhotos.SelectMatches(scores, baseline), Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void SeveralMatchesAreShownBestFirst()
    {
        // "a hand": the raised hand (index 2) is weaker than the two hand photos but still a match; the wall isn't.
        float[] scores = { 0.727f, 0.662f, 0.745f, 0.652f };
        float[] baseline = { 0.668f, 0.626f, 0.671f, 0.649f };
        Assert.That(ChatPhotos.SelectMatches(scores, baseline), Is.EqualTo(new[] { 2, 0, 1 }));
    }

    [Test]
    public void MatchesFarBelowTheBestAndLowScoresAreLeftOut()
    {
        // A strong match (lift 0.15), one 0.06 below it, and one with a lift but a similarity under 0.6.
        float[] scores = { 0.78f, 0.69f, 0.59f };
        float[] baseline = { 0.63f, 0.60f, 0.40f };
        Assert.That(ChatPhotos.SelectMatches(scores, baseline), Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void AtMostTheGivenNumberAreShown()
    {
        float[] scores = Enumerable.Repeat(0.7f, 12).ToArray();
        float[] baseline = Enumerable.Repeat(0.6f, 12).ToArray();
        Assert.That(ChatPhotos.SelectMatches(scores, baseline), Has.Count.EqualTo(ChatPhotos.MaxResults));
        Assert.That(ChatPhotos.SelectMatches(scores, baseline, 3), Has.Count.EqualTo(3));
    }

    [Test]
    public void OneBaselineScorePerPhotoIsNeeded()
    {
        Assert.Throws<System.ArgumentException>(() => ChatPhotos.SelectMatches(new[] { 0.7f }, new float[0]));
    }
}
