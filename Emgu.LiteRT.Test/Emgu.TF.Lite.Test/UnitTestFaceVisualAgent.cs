// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Agents.Vision;
using Emgu.LiteRT.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The face-expression visual agent: expressions read from the 52 blendshape scores - made-up ones here, from synthetic faces in the native test -, the
// words people use, and a monitor for "smiling" selecting the agent.
[TestFixture]
public class UnitTestFaceVisualAgent
{
    // Blendshape scores with the named ones set, the rest 0. The defaults are a neutral face as the synthetic faces scored it.
    internal static float[] Scores(params (string Name, float Value)[] values)
    {
        var scores = new float[FaceTracker.BlendshapeCount];
        foreach (var (name, value) in new[] { ("eyeBlinkLeft", 0.14f), ("eyeBlinkRight", 0.10f), ("eyeSquintLeft", 0.55f), ("eyeSquintRight", 0.53f) }.Concat(values))
            scores[FaceTracker.IndexOfBlendshape(name)] = value;
        return scores;
    }

    private static string[] Names(float[] scores, float min = 0.5f) => FaceExpressions.Classify(scores, min).Select(r => r.Name).ToArray();

    [Test]
    public void ANeutralFaceShowsNothing()
    {
        Assert.That(Names(Scores()), Is.Empty);
        Assert.That(FaceExpressions.Classify(Scores()), Is.Empty, "not even weakly");
    }

    // The scores of the synthetic faces (Z-Image-Turbo, one seed each), with what they show.
    [Test]
    public void TheSyntheticFacesScoresGiveTheirExpressions()
    {
        // A broad smile: the eyes narrow, but they aren't closed and it isn't a wink.
        Assert.That(Names(Scores(("mouthSmileLeft", 0.96f), ("mouthSmileRight", 0.96f), ("eyeBlinkLeft", 0.30f), ("eyeBlinkRight", 0.35f), ("jawOpen", 0.20f))),
            Is.EqualTo(new[] { "smiling" }));
        // Both eyes closed, with the brows a little up.
        Assert.That(Names(Scores(("eyeBlinkLeft", 0.81f), ("eyeBlinkRight", 0.77f), ("browInnerUp", 0.16f), ("browOuterUpLeft", 0.48f), ("browOuterUpRight", 0.33f))),
            Is.EqualTo(new[] { "eyes closed" }));
        // A surprised open mouth raises the brows too.
        Assert.That(Names(Scores(("jawOpen", 0.79f), ("browInnerUp", 0.27f), ("browOuterUpLeft", 0.86f), ("browOuterUpRight", 0.74f), ("eyeBlinkLeft", 0f), ("eyeBlinkRight", 0f))),
            Is.EqualTo(new[] { "mouth open", "eyebrows raised" }));
        // A wink closes one eye only about half way in the scores (a squint takes the rest), and comes with a half smile.
        Assert.That(Names(Scores(("eyeBlinkLeft", 0.23f), ("eyeBlinkRight", 0.52f), ("mouthSmileLeft", 0.12f), ("mouthSmileRight", 0.50f))),
            Is.EqualTo(new[] { "winking" }));
        Assert.That(Names(Scores(("eyeBlinkLeft", 0.54f), ("eyeBlinkRight", 0.15f))), Is.EqualTo(new[] { "winking" }), "the other eye");
    }

    [Test]
    public void TheScoreIsHalfAtTheThresholdAndLinearEitherSide()
    {
        Assert.That(FaceExpressions.Scale(0.5f, 0.5f), Is.EqualTo(0.5f));
        Assert.That(FaceExpressions.Scale(0f, 0.4f), Is.EqualTo(0f));
        Assert.That(FaceExpressions.Scale(0.2f, 0.4f), Is.EqualTo(0.25f).Within(1e-6));
        Assert.That(FaceExpressions.Scale(0.7f, 0.4f), Is.EqualTo(0.75f).Within(1e-6));
        Assert.That(FaceExpressions.Scale(1.5f, 0.4f), Is.EqualTo(1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => FaceExpressions.Scale(0.5f, 0f));
        var smile = FaceExpressions.Classify(Scores(("mouthSmileLeft", 0.5f), ("mouthSmileRight", 0.5f))).Single();
        Assert.That(smile.Name, Is.EqualTo("smiling"));
        Assert.That(smile.Score, Is.EqualTo(0.5f).Within(1e-6));
        Assert.That(smile.Strength, Is.EqualTo(0.5f).Within(1e-6));
        // So a monitor's default confidence of 0.5 fires at the threshold and not below it.
        var condition = new ObjectCountCondition("smiling", 1, 0.5);
        var frame = new VisualFrame(new byte[12], 2, 2, 6, "camera", 1, DateTimeOffset.Now, TimeSpan.FromSeconds(1));
        VisualObservation Smile(float score) => new("object", "smiling", score, new VisualBox(0.2, 0.2, 0.5, 0.5));
        Assert.That(condition.Count(new VisualAnalysis("face-expression", frame, new[] { Smile(0.5f) })), Is.EqualTo(1));
        Assert.That(condition.Count(new VisualAnalysis("face-expression", frame, new[] { Smile(0.45f) })), Is.EqualTo(0));
    }

    [Test]
    public void BothEyesClosedIsNotAWinkAndAHalfClosedEyeIsNeither()
    {
        Assert.That(Names(Scores(("eyeBlinkLeft", 0.9f), ("eyeBlinkRight", 0.6f))), Is.EqualTo(new[] { "eyes closed", "winking" }), "one shut, one half: both");
        Assert.That(Names(Scores(("eyeBlinkLeft", 0.86f), ("eyeBlinkRight", 0.79f))), Is.EqualTo(new[] { "eyes closed" }));
        // An eye a little more closed than the other, but neither much: no wink.
        Assert.That(Names(Scores(("eyeBlinkLeft", 0.40f), ("eyeBlinkRight", 0.05f))), Is.Empty);
        Assert.Throws<ArgumentException>(() => FaceExpressions.Classify(new float[10]));
    }

    [TestCase("smiling", "smiling")]
    [TestCase("Smile!", "smiling")]
    [TestCase("a smile", "smiling")]
    [TestCase("grinning", "smiling")]
    [TestCase("😊", "smiling")]
    [TestCase("eyes closed", "eyes closed")]
    [TestCase("Eyes shut", "eyes closed")]
    [TestCase("wink", "winking")]
    [TestCase("😉", "winking")]
    [TestCase("open mouth", "mouth open")]
    [TestCase("mouth wide open", "mouth open")]
    [TestCase("raised eyebrows", "eyebrows raised")]
    [TestCase("eyebrows up", "eyebrows raised")]
    [TestCase("face-expression.smiling", "smiling")]
    [TestCase("face: wink", "winking")]
    public void TheWordsForAnExpressionFindItsClass(string said, string expression)
    {
        Assert.That(VisualClasses.CanonicalExpression(said), Is.EqualTo(expression));
    }

    [TestCase("person")]
    [TestCase("face")]
    [TestCase("blink")]          // too brief for a monitor
    [TestCase("drowsy")]         // the model cards rule out drowsiness and attention
    [TestCase("asleep")]
    [TestCase("sleepy")]
    [TestCase("happy")]
    [TestCase("")]
    [TestCase(null)]
    public void OtherWordsAreNotExpressions(string said)
    {
        Assert.That(VisualClasses.CanonicalExpression(said), Is.Null);
    }

    [TestCase("Take a picture when I smile", "smiling")]
    [TestCase("Say cheese when she is smiling.", "smiling")]
    [TestCase("Beep when I close my eyes", "eyes closed")]
    [TestCase("Take a photo when my eyes are closed", "eyes closed")]
    [TestCase("Play a chime when I wink at the camera", "winking")]
    [TestCase("Record a clip when he opens his mouth", "mouth open")]
    [TestCase("Take a snapshot when I raise my eyebrows", "eyebrows raised")]
    [TestCase("Beep when my brows go up", "eyebrows raised")]
    [TestCase("Take a picture when someone raises their hand", null)]
    [TestCase("Take a picture when you see a person", null)]
    [TestCase("Close the door when I leave", null)]
    [TestCase("", null)]
    [TestCase(null, null)]
    public void TheRequestNamesAnExpressionInSoManyWords(string request, string expression)
    {
        Assert.That(VisualClasses.ExpressionFromRequest(request), Is.EqualTo(expression));
    }

    [Test]
    public void ALabelThatIsNoClassFallsBackToWhatTheUserAsked()
    {
        Assert.That(VisualClasses.ExpressionForMonitor("smile", "Beep when I wink"), Is.EqualTo("smiling"));
        Assert.That(VisualClasses.ExpressionForMonitor("person", "Take a picture when I smile"), Is.EqualTo("smiling"));
        Assert.That(VisualClasses.ExpressionForMonitor("face-expression", "Beep when I close my eyes"), Is.EqualTo("eyes closed"));
        Assert.That(VisualClasses.ExpressionForMonitor("face", "Take a picture when she opens her mouth"), Is.EqualTo("mouth open"));
        // Another agent's class or posture words stay theirs.
        Assert.That(VisualClasses.ExpressionForMonitor("dog", "Beep when the dog opens its mouth"), Is.Null);
        Assert.That(VisualClasses.ExpressionForMonitor("thumbs up", "Take a picture when I smile and give a thumbs up"), Is.Null);
        Assert.That(VisualClasses.ExpressionForMonitor("waving", "Beep when I smile and wave"), Is.Null);
        Assert.That(VisualClasses.ExpressionForMonitor("person", "Take a picture when someone raises their hand"), Is.Null);
        Assert.That(VisualClasses.ExpressionForMonitor("person", "Take a picture when you see a person"), Is.Null);
        // An expression label isn't taken for a posture by the pose agent's fallback.
        Assert.That(VisualClasses.PostureForMonitor("smiling", "Beep when I stand up and smile"), Is.Null);
    }

    [Test]
    public void TheExpressionClassesAreTheAgentsAndDontOverlapOthers()
    {
        Assert.That(VisualClasses.FaceClasses, Is.EqualTo(new[] { "smiling", "eyes closed", "winking", "mouth open", "eyebrows raised" }));
        Assert.That(VisualClasses.FaceClasses, Is.Unique);
        foreach (string expression in VisualClasses.FaceClasses)
        {
            Assert.That(VisualClasses.CanonicalExpression(expression), Is.EqualTo(expression), expression);
            Assert.That(VisualClasses.CanonicalPose(expression), Is.Null, expression);
            Assert.That(VisualClasses.CanonicalGesture(expression), Is.Null, expression);
        }
        Assert.That(VisualClasses.ObjectClasses.Intersect(VisualClasses.FaceClasses), Is.Empty);
        Assert.That(VisualClasses.Gestures.Intersect(VisualClasses.FaceClasses), Is.Empty);
        Assert.That(VisualClasses.PoseClasses.Intersect(VisualClasses.FaceClasses), Is.Empty);
        foreach (string other in VisualClasses.Gestures.Concat(VisualClasses.PoseClasses))
            Assert.That(VisualClasses.CanonicalExpression(other), Is.Null, other);
    }

    private sealed class FakeAgent(VisualAgentDescriptor descriptor) : IVisualAgent
    {
        public VisualAgentDescriptor Descriptor => descriptor;
        public Task PrepareAsync(Action<string> progress, CancellationToken token) => Task.CompletedTask;
        public Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken token) => throw new NotSupportedException();
        public void Dispose() { }
    }

    [Test]
    public void AMonitorForAnExpressionSelectsTheFaceAgent()
    {
        var agents = new VisualAgentRegistry();
        agents.Register(new FakeAgent(CocoVisualAgent.Describe()));
        agents.Register(new FakeAgent(HandVisualAgent.Describe()));
        agents.Register(new FakeAgent(PoseVisualAgent.Describe()));
        agents.Register(new FakeAgent(FaceVisualAgent.Describe()));
        foreach (string expression in VisualClasses.FaceClasses)
            Assert.That(agents.SelectObjects(expression).Descriptor.Id, Is.EqualTo("face-expression"), expression);
        Assert.That(agents.SelectObjects("waving").Descriptor.Id, Is.EqualTo("body-pose"));
        Assert.That(agents.SelectObjects("person").Descriptor.Id, Is.EqualTo("coco-ssd-mobilenet-v3"));
        var descriptor = agents.Describe().Single(d => d.Id == "face-expression");
        Assert.That(descriptor.Capabilities, Does.Contain("object_detection").And.Contain("face_expression"));
        Assert.That(descriptor.ObjectClasses, Is.EquivalentTo(VisualClasses.FaceClasses));
    }

    [Test]
    public void ExpressionsBecomeObservationsWithTheFacesBox()
    {
        var landmarks = Enumerable.Range(0, FaceTracker.LandmarkCount)
            .Select(i => new[] { 0.3f + 0.4f * (i % 10) / 9, 0.2f + 0.5f * (i / 10 % 10) / 9, 0f }).ToArray();
        var face = new FaceForTest(landmarks, Scores(("mouthSmileLeft", 0.9f), ("mouthSmileRight", 0.9f))).Face;
        var observation = FaceVisualAgent.Observations(face).Single();
        Assert.That(observation.Kind, Is.EqualTo("object"));
        Assert.That(observation.Label, Is.EqualTo("smiling"));
        Assert.That(observation.Confidence, Is.EqualTo(0.9).Within(1e-6));
        Assert.That(observation.Box.X, Is.EqualTo(0.3).Within(1e-6));
        Assert.That(observation.Box.Y, Is.EqualTo(0.2).Within(1e-6));
        Assert.That(observation.Box.Width, Is.EqualTo(0.4).Within(1e-6));
        Assert.That(observation.Box.Height, Is.EqualTo(0.5).Within(1e-6));
        Assert.That(FaceVisualAgent.Observations(null), Is.Empty);
    }

    // Face's setters are internal to Emgu.LiteRT.Models; reflection builds one for the observation test.
    private sealed class FaceForTest
    {
        public Face Face { get; }
        public FaceForTest(float[][] landmarks, float[] blendshapes)
        {
            Face = (Face)Activator.CreateInstance(typeof(Face), true);
            typeof(Face).GetProperty(nameof(Face.Landmarks)).SetValue(Face, landmarks);
            typeof(Face).GetProperty(nameof(Face.Blendshapes)).SetValue(Face, blendshapes);
        }
    }
}

[TestFixture, Category("Native")]
public class UnitTestFaceVisualAgentNative
{
    // Synthetic faces drawn by Z-Image-Turbo (TestData/face_*.jpg; FACE.md) and the demo's sample: what each shows at a monitor's default confidence.
    [TestCase("face_neutral.jpg")]
    [TestCase("face_portrait.png", "smiling")]
    [TestCase("face_eyes_closed.jpg", "eyes closed")]
    [TestCase("face_mouth_open.jpg", "mouth open", "eyebrows raised")]
    [TestCase("face_wink.jpg", "smiling", "winking")]
    public async Task EachSyntheticFaceShowsItsExpressions(string file, params string[] expected)
    {
        string[] models = await UnitTestFaceTrackerNative.ModelsAsync();
        using var tracker = new FaceTracker(models[0], models[1], models[2]);
        using var bgr = Emgu.CV.CvInvoke.Imread(Path.Combine(AppContext.BaseDirectory, file), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
        var faces = FaceImageInput.Track(tracker, bgr);
        Assert.That(faces.Count, Is.EqualTo(1), file);
        var observations = FaceVisualAgent.Observations(faces[0]);
        TestContext.Out.WriteLine(file + ": " + string.Join(", ", observations.Select(o => $"{o.Label} {o.Confidence:0.00}")));
        Assert.That(observations.Where(o => o.Confidence >= 0.5).Select(o => o.Label), Is.EqualTo(expected), file);
        // The box is the face, inside the image.
        foreach (var o in observations)
        {
            Assert.That(o.Box.Width, Is.InRange(0.2, 1.0)); Assert.That(o.Box.Height, Is.InRange(0.2, 1.0));
            Assert.That(o.Box.X + o.Box.Width, Is.LessThanOrEqualTo(1.0 + 1e-9));
        }
    }
}
