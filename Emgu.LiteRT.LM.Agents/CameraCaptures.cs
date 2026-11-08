// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Emgu.LiteRT.LM.Agents.Events
{
    /// <summary>What a camera action took.</summary>
    public enum CameraCaptureKind
    {
        /// <summary>A frame from the live preview (take_snapshot).</summary>
        Snapshot,
        /// <summary>A full-resolution photo (take_photo).</summary>
        Photo,
        /// <summary>A short video (record_clip).</summary>
        Clip
    }

    /// <summary>
    /// A picture or clip a camera action took in this session: what the chat's card and the camera panel's strip show. The
    /// file (<see cref="Path"/>) or the bytes (<see cref="Data"/>) are the app's; <see cref="Tag"/> holds its views.
    /// </summary>
    public sealed class CameraCapture
    {
        /// <param name="kind">What it is</param>
        /// <param name="takenAt">When it was taken</param>
        /// <param name="condition">The monitor's condition (or the sound) that set it off, or null</param>
        /// <param name="path">The file of a photo or clip, or null</param>
        /// <param name="data">The JPEG of a snapshot, or null</param>
        /// <param name="thumbnail">A small JPEG for the strip, or null</param>
        public CameraCapture(CameraCaptureKind kind, DateTimeOffset takenAt, string condition, string path = null, byte[] data = null,
            byte[] thumbnail = null)
        {
            Kind = kind;
            TakenAt = takenAt;
            Condition = condition;
            Path = path;
            Data = data;
            Thumbnail = thumbnail;
        }

        /// <summary>What it is.</summary>
        public CameraCaptureKind Kind { get; }

        /// <summary>When it was taken.</summary>
        public DateTimeOffset TakenAt { get; }

        /// <summary>The condition that set it off, or null.</summary>
        public string Condition { get; }

        /// <summary>The file of a photo or clip, or null.</summary>
        public string Path { get; }

        /// <summary>The JPEG of a snapshot, or null.</summary>
        public byte[] Data { get; }

        /// <summary>A small JPEG for the strip, or null.</summary>
        public byte[] Thumbnail { get; }

        /// <summary>A photo's size in pixels (0 if unknown).</summary>
        public int Width { get; set; }

        /// <summary>A photo's size in pixels (0 if unknown).</summary>
        public int Height { get; set; }

        /// <summary>A clip's length.</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>Whether a clip has sound.</summary>
        public bool WithSound { get; set; }

        /// <summary>
        /// True once the user kept it in the chat (saved with the reply): it no longer counts toward its kind's cap, and the
        /// app keeps its card and file when it leaves the list.
        /// </summary>
        public bool Kept { get; set; }

        /// <summary>The app's views for it (the card, the strip's thumbnail).</summary>
        public object Tag { get; set; }
    }

    /// <summary>Why a capture left <see cref="CameraCaptures"/>.</summary>
    public enum CameraCaptureRemoval
    {
        /// <summary>Newer captures pushed it out (its kind's cap, or the overall cap for kept ones).</summary>
        Cap,
        /// <summary>The list was cleared (a new chat).</summary>
        Clear
    }

    /// <summary>
    /// The session's camera captures, newest first, with a cap per kind: one list the chat's cards and the camera panel's
    /// strip both follow, so a capture's card and thumbnail come and go together, and the one place an old file is let go
    /// of (<see cref="Removed"/>). Kept captures don't count toward their kind's cap; <see cref="MaxItems"/> bounds the
    /// whole list. Not thread-safe: use it on one thread (the UI's).
    /// </summary>
    public sealed class CameraCaptures
    {
        private readonly List<CameraCapture> _items = new List<CameraCapture>();
        private readonly Dictionary<CameraCaptureKind, int> _caps;

        /// <param name="maxSnapshots">The most snapshots not kept</param>
        /// <param name="maxPhotos">The most photos not kept</param>
        /// <param name="maxClips">The most clips not kept</param>
        /// <param name="maxItems">The most captures of all kinds, kept or not</param>
        public CameraCaptures(int maxSnapshots = 20, int maxPhotos = 5, int maxClips = 5, int maxItems = 40)
        {
            if (maxSnapshots < 1 || maxPhotos < 1 || maxClips < 1 || maxItems < 1)
                throw new ArgumentOutOfRangeException(nameof(maxItems), "Each cap must be at least 1");
            _caps = new Dictionary<CameraCaptureKind, int>
            {
                [CameraCaptureKind.Snapshot] = maxSnapshots,
                [CameraCaptureKind.Photo] = maxPhotos,
                [CameraCaptureKind.Clip] = maxClips
            };
            MaxItems = maxItems;
        }

        /// <summary>The most captures of all kinds.</summary>
        public int MaxItems { get; }

        /// <summary>The captures, newest first.</summary>
        public IReadOnlyList<CameraCapture> Items => _items;

        /// <summary>Raised after a capture is added.</summary>
        public event Action<CameraCapture> Added;

        /// <summary>Raised for each capture that leaves the list, and why: the app removes its views and, unless it was kept, its file.</summary>
        public event Action<CameraCapture, CameraCaptureRemoval> Removed;

        /// <summary>Add a capture as the newest, then drop the oldest past the caps.</summary>
        public void Add(CameraCapture capture)
        {
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            _items.Insert(0, capture);
            Added?.Invoke(capture);
            Trim();
        }

        /// <summary>Mark a capture kept (saved with the chat): it stops counting toward its kind's cap.</summary>
        public void Keep(CameraCapture capture)
        {
            if (capture != null)
                capture.Kept = true;
        }

        /// <summary>Remove every capture (oldest first), raising <see cref="Removed"/> for each.</summary>
        public void Clear()
        {
            while (_items.Count > 0)
            {
                CameraCapture oldest = _items[_items.Count - 1];
                _items.RemoveAt(_items.Count - 1);
                Removed?.Invoke(oldest, CameraCaptureRemoval.Clear);
            }
        }

        private void Trim()
        {
            foreach (CameraCaptureKind kind in _caps.Keys)
            {
                List<CameraCapture> open = _items.Where(c => c.Kind == kind && !c.Kept).ToList();
                for (int i = open.Count - 1; i >= _caps[kind]; i--)
                    Drop(open[i]);
            }
            while (_items.Count > MaxItems)
                Drop(_items[_items.Count - 1]);
        }

        private void Drop(CameraCapture capture)
        {
            _items.Remove(capture);
            Removed?.Invoke(capture, CameraCaptureRemoval.Cap);
        }
    }

    /// <summary>
    /// A capture kept with a chat's reply: the attachment's kind and a media type carrying what its card shows (when, why, the
    /// size or length), and back. The chats' store keeps the media type as it is, so the card can be made again when the
    /// chat is reopened.
    /// </summary>
    public static class CameraCaptureFile
    {
        /// <summary>The kind of a kept snapshot.</summary>
        public const string SnapshotKind = "camera_snapshot";

        /// <summary>The kind of a kept photo.</summary>
        public const string PhotoKind = "camera_photo";

        /// <summary>The kind of a kept clip.</summary>
        public const string ClipKind = "camera_clip";

        /// <summary>The attachment kind for a capture's kind.</summary>
        public static string KindOf(CameraCaptureKind kind) =>
            kind == CameraCaptureKind.Snapshot ? SnapshotKind : kind == CameraCaptureKind.Photo ? PhotoKind : ClipKind;

        /// <summary>The capture's kind for an attachment kind, or null for one that isn't a capture's.</summary>
        public static CameraCaptureKind? FromKind(string kind) =>
            kind == SnapshotKind ? CameraCaptureKind.Snapshot : kind == PhotoKind ? CameraCaptureKind.Photo : kind == ClipKind ? CameraCaptureKind.Clip
            : (CameraCaptureKind?)null;

        /// <summary>The media type to save a capture with: "image/jpeg" or "video/mp4" and its details as parameters.</summary>
        public static string MediaTypeOf(CameraCapture capture)
        {
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            var parts = new List<string> { capture.Kind == CameraCaptureKind.Clip ? "video/mp4" : "image/jpeg" };
            parts.Add("taken=" + Uri.EscapeDataString(capture.TakenAt.ToString("o", CultureInfo.InvariantCulture)));
            if (!string.IsNullOrEmpty(capture.Condition))
                parts.Add("condition=" + Uri.EscapeDataString(capture.Condition));
            if (capture.Width > 0 && capture.Height > 0)
                parts.Add("size=" + capture.Width.ToString(CultureInfo.InvariantCulture) + "x" + capture.Height.ToString(CultureInfo.InvariantCulture));
            if (capture.Kind == CameraCaptureKind.Clip)
            {
                parts.Add("ms=" + ((long)capture.Duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
                parts.Add("sound=" + (capture.WithSound ? "1" : "0"));
            }
            return string.Join("; ", parts);
        }

        /// <summary>
        /// A kept capture read back: its kind, details and bytes (as <see cref="CameraCapture.Data"/>; the app writes a photo or
        /// clip to a file to open it). Null when the kind isn't a capture's.
        /// </summary>
        /// <param name="kind">The attachment's kind</param>
        /// <param name="mediaType">Its media type, with the details</param>
        /// <param name="data">The bytes</param>
        public static CameraCapture Parse(string kind, string mediaType, byte[] data)
        {
            CameraCaptureKind? captureKind = FromKind(kind);
            if (captureKind == null)
                return null;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string part in (mediaType ?? "").Split(';').Skip(1))
            {
                int equals = part.IndexOf('=');
                if (equals > 0)
                    values[part.Substring(0, equals).Trim()] = Uri.UnescapeDataString(part.Substring(equals + 1).Trim());
            }
            DateTimeOffset takenAt = values.TryGetValue("taken", out string taken)
                && DateTimeOffset.TryParse(taken, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset at) ? at : default;
            values.TryGetValue("condition", out string condition);
            var capture = new CameraCapture(captureKind.Value, takenAt, condition, data: data) { Kept = true };
            if (values.TryGetValue("size", out string size))
            {
                string[] sides = size.Split('x');
                if (sides.Length == 2 && int.TryParse(sides[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
                    && int.TryParse(sides[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
                {
                    capture.Width = width;
                    capture.Height = height;
                }
            }
            if (values.TryGetValue("ms", out string ms) && long.TryParse(ms, NumberStyles.Integer, CultureInfo.InvariantCulture, out long milliseconds))
                capture.Duration = TimeSpan.FromMilliseconds(milliseconds);
            capture.WithSound = values.TryGetValue("sound", out string sound) && sound == "1";
            return capture;
        }
    }
}
