// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Maui.Demo.Lite;

/// <summary>A sound an alert can play: one of the app's own or one the user added.</summary>
internal sealed class AlertSound
{
    /// <summary>The name people say it by ("lullaby", "rain")</summary>
    public string Name { get; init; }
    /// <summary>The file</summary>
    public string Path { get; init; }
    /// <summary>Whether it ships with the app (and can't be deleted)</summary>
    public bool BuiltIn { get; init; }
}

/// <summary>
/// The sounds that the <c>play_file</c> alert action (and a sound monitor's play-file option) can play (TODO item 53): two the app ships - soft
/// white noise and a music-box lullaby, both synthesized for it by <c>tools/make_alert_sounds.py</c> - and any the user adds from a file or by recording.
/// They live in one folder: the built-in ones copied from the app package the first time, the user's in a subfolder. Names are what people say
/// ("play the lullaby", "my rain sound"); <see cref="Find"/> matches loosely and says when it can't choose.
/// </summary>
internal sealed class AlertSoundLibrary
{
    /// <summary>The built-in sounds: the name and the asset in the app package</summary>
    public static readonly (string Name, string Asset)[] BuiltIns = { ("white noise", "alert_white_noise.wav"), ("lullaby", "alert_lullaby.wav") };

    /// <summary>The file types that can be added</summary>
    public static readonly string[] Extensions = { ".wav", ".mp3", ".m4a", ".aac", ".flac", ".ogg" };

    /// <summary>The largest file that can be added, in bytes</summary>
    public const long MaximumBytes = 25L * 1024 * 1024;

    /// <summary>The most sounds the user can add</summary>
    public const int MaximumUserSounds = 20;

    private readonly string _folder;
    private readonly Func<string, CancellationToken, Task<Stream>> _openAsset;
    private readonly object _gate = new();

    /// <param name="folder">Where the sounds are kept</param>
    /// <param name="openAsset">Opens a file of the app package by name (null in tests that add nothing built-in)</param>
    public AlertSoundLibrary(string folder, Func<string, CancellationToken, Task<Stream>> openAsset = null)
    {
        _folder = folder ?? throw new ArgumentNullException(nameof(folder));
        _openAsset = openAsset;
    }

    private string UserFolder => System.IO.Path.Combine(_folder, "user");

    /// <summary>Copy the built-in sounds out of the app package if they aren't there (a few MB; once).</summary>
    public async Task EnsureBuiltInsAsync(CancellationToken token = default)
    {
        Directory.CreateDirectory(_folder);
        foreach (var (name, asset) in BuiltIns)
        {
            string path = System.IO.Path.Combine(_folder, asset);
            if (File.Exists(path) || _openAsset == null) continue;
            string temporary = path + "." + Guid.NewGuid().ToString("N");
            try
            {
                using (Stream source = await _openAsset(asset, token).ConfigureAwait(false))
                using (var target = File.Create(temporary))
                    await source.CopyToAsync(target, 81920, token).ConfigureAwait(false);
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    /// <summary>The sounds that can be played now: the built-in ones that have been copied, then the user's by name.</summary>
    public IReadOnlyList<AlertSound> List()
    {
        var sounds = new List<AlertSound>();
        foreach (var (name, asset) in BuiltIns)
        {
            string path = System.IO.Path.Combine(_folder, asset);
            if (File.Exists(path)) sounds.Add(new AlertSound { Name = name, Path = path, BuiltIn = true });
        }
        if (Directory.Exists(UserFolder))
            foreach (string file in Directory.GetFiles(UserFolder).Where(f => Extensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => System.IO.Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase))
                sounds.Add(new AlertSound { Name = System.IO.Path.GetFileNameWithoutExtension(file), Path = file, BuiltIn = false });
        return sounds;
    }

    /// <summary>The names, for a tool's description.</summary>
    public IReadOnlyList<string> Names() => List().Select(s => s.Name).ToArray();

    private static readonly HashSet<string> FillerWords = new(StringComparer.Ordinal) { "the", "a", "my", "your", "our", "sound", "sounds", "song", "music", "file", "audio", "track", "tune", "please" };

    /// <summary>A name in lower case words without punctuation or the words that don't name a sound ("the", "my", "sound").</summary>
    internal static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var words = new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        var kept = words.Where(w => !FillerWords.Contains(w)).ToList();
        return string.Join(" ", kept.Count > 0 ? kept : words);
    }

    /// <summary>
    /// The sound a name asks for: the same words, else the one whose words contain (or are contained in) the request's, else the one that shares
    /// the most words - and only if that's a clear winner. Null if nothing fits or two fit equally well.
    /// </summary>
    public AlertSound Find(string name) => Find(name, out _);

    /// <param name="name">What was asked for</param>
    /// <param name="problem">Why null was returned: nothing like it, or which sounds it could mean</param>
    public AlertSound Find(string name, out string problem)
    {
        problem = null;
        var sounds = List();
        string wanted = Normalize(name);
        if (wanted.Length == 0) { problem = "Say which sound: " + Describe(sounds) + "."; return null; }
        var exact = sounds.Where(s => Normalize(s.Name) == wanted).ToList();
        if (exact.Count == 1) return exact[0];
        string[] wantedWords = wanted.Split(' ');
        var scored = sounds.Select(s =>
        {
            string n = Normalize(s.Name);
            string[] words = n.Split(' ');
            int shared = words.Intersect(wantedWords).Count();
            bool contained = n.Contains(wanted) || wanted.Contains(n);
            return (Sound: s, Score: contained ? 100 + shared : shared);
        }).Where(x => x.Score > 0).OrderByDescending(x => x.Score).ToList();
        if (scored.Count == 0) { problem = "There is no sound called '" + name + "'. Available: " + Describe(sounds) + "."; return null; }
        if (scored.Count > 1 && scored[0].Score == scored[1].Score)
        {
            problem = "'" + name + "' could mean " + string.Join(" or ", scored.Where(x => x.Score == scored[0].Score).Select(x => "'" + x.Sound.Name + "'")) + ". Say which one.";
            return null;
        }
        return scored[0].Sound;
    }

    private static string Describe(IReadOnlyList<AlertSound> sounds) => sounds.Count == 0 ? "none yet" : string.Join(", ", sounds.Select(s => s.Name));

    /// <summary>
    /// Add a sound from a stream. The name is the file's without its extension (or <paramref name="name"/>); a name already taken gets " 2", " 3"...
    /// Refuses other file types, files over <see cref="MaximumBytes"/> and a twenty-first sound.
    /// </summary>
    public async Task<AlertSound> AddAsync(Stream source, string fileName, string name = null, CancellationToken token = default)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        string extension = System.IO.Path.GetExtension(fileName ?? "").ToLowerInvariant();
        if (!Extensions.Contains(extension)) throw new ArgumentException("Choose a " + string.Join(", ", Extensions.Select(e => e.TrimStart('.').ToUpperInvariant())) + " file.");
        Directory.CreateDirectory(UserFolder);
        if (Directory.GetFiles(UserFolder).Length >= MaximumUserSounds) throw new InvalidOperationException("There are already " + MaximumUserSounds + " sounds. Delete one first.");
        string wanted = CleanName(string.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileNameWithoutExtension(fileName) : name);
        string final;
        lock (_gate)
        {
            string unique = wanted;
            for (int n = 2; Exists(unique); n++) unique = wanted + " " + n;
            final = System.IO.Path.Combine(UserFolder, unique + extension);
            File.WriteAllBytes(final, Array.Empty<byte>());            // reserve the name
        }
        try
        {
            using (var target = File.Create(final))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaximumBytes) throw new InvalidOperationException("That file is over " + MaximumBytes / 1048576 + " MB.");
                    await target.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                }
                if (total == 0) throw new InvalidOperationException("That file is empty.");
            }
        }
        catch { try { File.Delete(final); } catch (IOException) { } throw; }
        return new AlertSound { Name = System.IO.Path.GetFileNameWithoutExtension(final), Path = final, BuiltIn = false };
    }

    private bool Exists(string name) => BuiltIns.Any(b => Normalize(b.Name) == Normalize(name)) ||
        Extensions.Any(e => File.Exists(System.IO.Path.Combine(UserFolder, name + e))) ||
        (Directory.Exists(UserFolder) && Directory.GetFiles(UserFolder).Any(f => string.Equals(System.IO.Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase)));

    /// <summary>A name that is safe as a file name: letters, digits, spaces and a few marks, at most 40 characters; "sound" if nothing is left.</summary>
    internal static string CleanName(string text)
    {
        string cleaned = new string((text ?? "").Select(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '\'' ? c : ' ').ToArray());
        cleaned = string.Join(" ", cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length > 40) cleaned = cleaned.Substring(0, 40).TrimEnd();
        return cleaned.Length == 0 ? "sound" : cleaned;
    }

    /// <summary>Delete one of the user's sounds (the built-in ones can't be).</summary>
    public void Delete(AlertSound sound)
    {
        if (sound == null) throw new ArgumentNullException(nameof(sound));
        if (sound.BuiltIn) throw new InvalidOperationException("The built-in sounds can't be deleted.");
        if (File.Exists(sound.Path)) File.Delete(sound.Path);
    }
}
