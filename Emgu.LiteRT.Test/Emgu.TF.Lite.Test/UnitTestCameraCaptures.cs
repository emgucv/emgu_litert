using System;
using System.Collections.Generic;
using System.Linq;
using Emgu.LiteRT.LM.Agents.Events;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The session's camera captures behind the chat's cards and the camera panel's strip, and a kept capture's saved form.
[TestFixture]
public class UnitTestCameraCaptures
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 11, 8, 15, 42, 10, TimeSpan.FromHours(-5));

    private static CameraCapture Capture(CameraCaptureKind kind, int second) =>
        new CameraCapture(kind, Start.AddSeconds(second), "a person", path: kind == CameraCaptureKind.Snapshot ? null : "c" + second);

    [Test]
    public void NewestFirst()
    {
        var captures = new CameraCaptures();
        var added = new List<CameraCapture>();
        captures.Added += added.Add;
        CameraCapture a = Capture(CameraCaptureKind.Photo, 1), b = Capture(CameraCaptureKind.Snapshot, 2), c = Capture(CameraCaptureKind.Clip, 3);
        captures.Add(a); captures.Add(b); captures.Add(c);
        Assert.That(captures.Items, Is.EqualTo(new[] { c, b, a }));
        Assert.That(added, Is.EqualTo(new[] { a, b, c }));
    }

    [Test]
    public void EachKindHasItsOwnCap()
    {
        var captures = new CameraCaptures(maxSnapshots: 3, maxPhotos: 2, maxClips: 2);
        var removed = new List<(CameraCapture Capture, CameraCaptureRemoval Why)>();
        captures.Removed += (c, why) => removed.Add((c, why));
        var photos = Enumerable.Range(0, 4).Select(i => Capture(CameraCaptureKind.Photo, i)).ToList();
        var snapshots = Enumerable.Range(10, 3).Select(i => Capture(CameraCaptureKind.Snapshot, i)).ToList();
        foreach (CameraCapture capture in photos.Concat(snapshots))
            captures.Add(capture);

        // The two oldest photos went, each once; the snapshots are within their cap.
        Assert.That(removed, Is.EqualTo(new[] { (photos[0], CameraCaptureRemoval.Cap), (photos[1], CameraCaptureRemoval.Cap) }));
        Assert.That(captures.Items.Count(c => c.Kind == CameraCaptureKind.Photo), Is.EqualTo(2));
        Assert.That(captures.Items.Count(c => c.Kind == CameraCaptureKind.Snapshot), Is.EqualTo(3));
    }

    [Test]
    public void AKeptCaptureDoesNotCountTowardItsKindsCap()
    {
        var captures = new CameraCaptures(maxPhotos: 2, maxItems: 4);
        var removed = new List<CameraCapture>();
        captures.Removed += (c, _) => removed.Add(c);
        CameraCapture kept = Capture(CameraCaptureKind.Photo, 0);
        captures.Add(kept);
        captures.Keep(kept);
        CameraCapture one = Capture(CameraCaptureKind.Photo, 1), two = Capture(CameraCaptureKind.Photo, 2);
        captures.Add(one); captures.Add(two);
        Assert.That(removed, Is.Empty, "two open photos and a kept one");

        // A third open photo pushes out the oldest open one, not the kept one; past maxItems the oldest goes, kept or not.
        CameraCapture three = Capture(CameraCaptureKind.Photo, 3);
        captures.Add(three);
        Assert.That(removed, Is.EqualTo(new[] { one }));
        captures.Add(Capture(CameraCaptureKind.Snapshot, 4));
        captures.Add(Capture(CameraCaptureKind.Snapshot, 5));
        Assert.That(removed, Is.EqualTo(new[] { one, kept }));
        Assert.That(captures.Items.Count, Is.EqualTo(4));
    }

    [Test]
    public void ClearRemovesEachOnceOldestFirst()
    {
        var captures = new CameraCaptures();
        var removed = new List<(CameraCapture Capture, CameraCaptureRemoval Why)>();
        captures.Removed += (c, why) => removed.Add((c, why));
        CameraCapture a = Capture(CameraCaptureKind.Snapshot, 1), b = Capture(CameraCaptureKind.Clip, 2);
        captures.Add(a); captures.Add(b);
        captures.Clear();
        Assert.That(removed, Is.EqualTo(new[] { (a, CameraCaptureRemoval.Clear), (b, CameraCaptureRemoval.Clear) }));
        Assert.That(captures.Items, Is.Empty);
        captures.Clear();
        Assert.That(removed.Count, Is.EqualTo(2));
    }

    [Test]
    public void AKeptPhotoReadsBackWithItsDetails()
    {
        var photo = new CameraCapture(CameraCaptureKind.Photo, Start, "a person; at the door = yes", path: "p.jpg") { Width = 4032, Height = 3024 };
        string mediaType = CameraCaptureFile.MediaTypeOf(photo);
        Assert.That(mediaType, Does.StartWith("image/jpeg; taken="));
        Assert.That(CameraCaptureFile.KindOf(photo.Kind), Is.EqualTo(CameraCaptureFile.PhotoKind));

        CameraCapture back = CameraCaptureFile.Parse(CameraCaptureFile.PhotoKind, mediaType, new byte[] { 1, 2 });
        Assert.That(back.Kind, Is.EqualTo(CameraCaptureKind.Photo));
        Assert.That(back.TakenAt, Is.EqualTo(Start));
        Assert.That(back.TakenAt.Offset, Is.EqualTo(Start.Offset));
        Assert.That(back.Condition, Is.EqualTo("a person; at the door = yes"));
        Assert.That((back.Width, back.Height), Is.EqualTo((4032, 3024)));
        Assert.That(back.Data, Is.EqualTo(new byte[] { 1, 2 }));
        Assert.That(back.Kept, Is.True);
    }

    [Test]
    public void AKeptClipReadsBackWithItsLengthAndSound()
    {
        var clip = new CameraCapture(CameraCaptureKind.Clip, Start, null, path: "c.mp4") { Duration = TimeSpan.FromMilliseconds(5040), WithSound = true };
        string mediaType = CameraCaptureFile.MediaTypeOf(clip);
        Assert.That(mediaType, Does.StartWith("video/mp4;"));
        CameraCapture back = CameraCaptureFile.Parse(CameraCaptureFile.ClipKind, mediaType, new byte[] { 3 });
        Assert.That(back.Kind, Is.EqualTo(CameraCaptureKind.Clip));
        Assert.That(back.Duration, Is.EqualTo(TimeSpan.FromMilliseconds(5040)));
        Assert.That(back.WithSound, Is.True);
        Assert.That(back.Condition, Is.Null);
    }

    [Test]
    public void OtherKindsAreNotCaptures()
    {
        Assert.That(CameraCaptureFile.Parse("written_document", "application/pdf; name=a.pdf", new byte[1]), Is.Null);
        Assert.That(CameraCaptureFile.FromKind("generated"), Is.Null);
        Assert.That(CameraCaptureFile.FromKind(CameraCaptureFile.SnapshotKind), Is.EqualTo(CameraCaptureKind.Snapshot));
    }
}
