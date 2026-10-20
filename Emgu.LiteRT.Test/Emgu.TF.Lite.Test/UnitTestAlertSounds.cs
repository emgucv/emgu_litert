// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Agents.Vision;
using Maui.Demo.Lite;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The alert sounds (TODO item 53): the library of sounds an alert can play - the built-in ones copied out of the package, the user's added from a stream - the
// loose matching of the names people say, and the play_file action made from them.
[TestFixture]
public class UnitTestAlertSounds
{
    private string _folder;

    [SetUp]
    public void Folder() => _folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "alert-sounds-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void Cleanup() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }

    private static Task<Stream> Asset(string name, CancellationToken token) => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3, (byte)name.Length }));
    private static MemoryStream Bytes(int length = 100) => new(Enumerable.Repeat((byte)7, length).ToArray());

    private async Task<AlertSoundLibrary> Library(params string[] userSounds)
    {
        var library = new AlertSoundLibrary(_folder, Asset);
        await library.EnsureBuiltInsAsync();
        foreach (string file in userSounds) await library.AddAsync(Bytes(), file);
        return library;
    }

    [Test]
    public async Task TheBuiltInSoundsAreCopiedOnceAndListedFirst()
    {
        var library = new AlertSoundLibrary(_folder, Asset);
        Assert.That(library.Names(), Is.Empty);                                  // nothing before they're copied
        await library.EnsureBuiltInsAsync();
        Assert.That(library.Names(), Is.EqualTo(new[] { "white noise", "lullaby" }));
        Assert.That(library.List().All(s => s.BuiltIn && File.Exists(s.Path)), Is.True);
        File.WriteAllBytes(library.List()[0].Path, new byte[] { 9 });
        await library.EnsureBuiltInsAsync();                                     // a second call leaves the files
        Assert.That(File.ReadAllBytes(library.List()[0].Path), Is.EqualTo(new byte[] { 9 }));
    }

    [Test]
    public async Task UserSoundsAreAddedByTheirFileNameAndListedByName()
    {
        var library = await Library("Rain on the roof.mp3", "my_lullaby-v2.WAV");
        Assert.That(library.Names(), Is.EqualTo(new[] { "white noise", "lullaby", "my lullaby-v2", "Rain on the roof" }));
        AlertSound rain = library.List().Single(s => s.Name == "Rain on the roof");
        Assert.That(rain.BuiltIn, Is.False);
        Assert.That(File.ReadAllBytes(rain.Path).Length, Is.EqualTo(100));
        Assert.That(Path.GetExtension(rain.Path), Is.EqualTo(".mp3"));
        // A name that is taken gets a number, the built-in names included.
        AlertSound again = await library.AddAsync(Bytes(), "Rain on the roof.m4a");
        Assert.That(again.Name, Is.EqualTo("Rain on the roof 2"));
        Assert.That((await library.AddAsync(Bytes(), "lullaby.wav")).Name, Is.EqualTo("lullaby 2"));
        Assert.That((await library.AddAsync(Bytes(), "x.wav", "  Good night!! ")).Name, Is.EqualTo("Good night"));
        Assert.That((await library.AddAsync(Bytes(), "???.wav")).Name, Is.EqualTo("sound"));
    }

    [Test]
    public async Task OnlySoundFilesOfASensibleSizeAreAdded()
    {
        var library = await Library();
        Assert.ThrowsAsync<ArgumentException>(async () => await library.AddAsync(Bytes(), "notes.txt"));
        Assert.ThrowsAsync<ArgumentException>(async () => await library.AddAsync(Bytes(), "noextension"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await library.AddAsync(new MemoryStream(), "empty.wav"));
        var big = new ConstantStream(AlertSoundLibrary.MaximumBytes + 1);
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await library.AddAsync(big, "big.wav"));
        Assert.That(ex.Message, Does.Contain("25 MB"));
        Assert.That(library.List().Count, Is.EqualTo(2));                          // the failed ones left nothing behind
        Assert.That(Directory.GetFiles(Path.Combine(_folder, "user")), Is.Empty);
        for (int i = 0; i < AlertSoundLibrary.MaximumUserSounds; i++) await library.AddAsync(Bytes(), "s" + i + ".wav");
        Assert.ThrowsAsync<InvalidOperationException>(async () => await library.AddAsync(Bytes(), "one more.wav"));
    }

    // A stream of this many bytes that is never held in memory.
    private sealed class ConstantStream(long length) : Stream
    {
        private long _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = (int)Math.Min(count, length - _read);
            _read += n;
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Test]
    public async Task OnlyTheUsersSoundsAreDeleted()
    {
        var library = await Library("rain.wav");
        AlertSound rain = library.Find("rain");
        library.Delete(rain);
        Assert.That(File.Exists(rain.Path), Is.False);
        Assert.That(library.Names(), Is.EqualTo(new[] { "white noise", "lullaby" }));
        Assert.Throws<InvalidOperationException>(() => library.Delete(library.Find("lullaby")));
        Assert.That(library.Names(), Does.Contain("lullaby"));
    }

    [TestCase("lullaby", "lullaby")]
    [TestCase("the lullaby", "lullaby")]
    [TestCase("Play the Lullaby!", "lullaby")]
    [TestCase("my rain sound", "Rain on the roof")]
    [TestCase("white noise", "white noise")]
    [TestCase("noise", "white noise")]
    [TestCase("rain", "Rain on the roof")]
    [TestCase("rain on the roof", "Rain on the roof")]
    [TestCase("it's okay message", "its okay message")]
    public async Task TheNamesPeopleSayFindTheirSound(string said, string expected)
    {
        var library = await Library("Rain on the roof.mp3", "its okay message.wav");
        Assert.That(library.Find(said)?.Name, Is.EqualTo(expected));
    }

    [Test]
    public async Task ANameThatFitsNothingOrTwoThingsGivesAReasonTheModelCanUse()
    {
        var library = await Library("rain on the roof.mp3", "rain at sea.mp3");
        Assert.That(library.Find("whale song", out string none), Is.Null);
        Assert.That(none, Does.Contain("whale song").And.Contain("white noise").And.Contain("lullaby").And.Contain("rain at sea"));
        Assert.That(library.Find("rain", out string two), Is.Null);
        Assert.That(two, Does.Contain("rain on the roof").And.Contain("rain at sea").And.Contain("Say which one"));
        Assert.That(library.Find("rain sea")?.Name, Is.EqualTo("rain at sea"));
        Assert.That(library.Find("", out string blank), Is.Null);
        Assert.That(blank, Does.Contain("Say which sound"));
        Assert.That(library.Find("the sound", out _), Is.Null);               // only filler words
    }

    [Test]
    public void TheDescriptorListsTheSoundsAndIsUnavailableWithNone()
    {
        var some = VisualHostCatalog.PlayFile(new[] { "white noise", "lullaby" });
        Assert.That(some.Id, Is.EqualTo("play_file"));
        Assert.That(some.Available, Is.True);
        Assert.That(some.Description, Does.Contain("white noise, lullaby"));
        Assert.That(some.ParameterSchema.GetProperty("required")[0].GetString(), Is.EqualTo("sound"));
        Assert.That(some.ParameterSchema.GetProperty("properties").GetProperty("seconds").GetProperty("maximum").GetInt32(), Is.EqualTo(300));
        Assert.That(VisualHostCatalog.PlayFile(Array.Empty<string>()).Available, Is.False);
    }

    private static JsonElement Json(string json) => VisualActionParameters.Parse(json);

    [Test]
    public async Task TheActionIsBoundToARealSoundWithNormalizedParameters()
    {
        var library = await Library("rain.wav");
        var factory = new PlayFileFactory(library, (s, n, t) => Task.CompletedTask);
        var binding = factory.Bind(Json("{\"sound\":\"the Lullaby\",\"seconds\":45}"));
        Assert.That(binding.Action.Id, Is.EqualTo("play_file"));
        Assert.That(binding.Parameters.GetProperty("sound").GetString(), Is.EqualTo("lullaby"));
        Assert.That(binding.Parameters.GetProperty("seconds").GetInt32(), Is.EqualTo(45));
        Assert.That(factory.Bind(Json("{\"sound\":\"rain\"}")).Parameters.TryGetProperty("seconds", out _), Is.False);
        var unknown = Assert.Throws<ArgumentException>(() => factory.Bind(Json("{\"sound\":\"thunder\"}")));
        Assert.That(unknown.Message, Does.Contain("thunder").And.Contain("lullaby"));
        Assert.Throws<ArgumentException>(() => factory.Bind(Json("{\"sound\":\"rain\",\"seconds\":0}")));
        Assert.Throws<ArgumentException>(() => factory.Bind(Json("{\"sound\":\"rain\",\"seconds\":301}")));
        Assert.Throws<ArgumentException>(() => factory.Bind(Json("{\"sound\":\"rain\",\"seconds\":\"ten\"}")));
        Assert.Throws<ArgumentException>(() => factory.Bind(Json("{\"sound\":\"rain\",\"loud\":true}")));
        Assert.Throws<ArgumentException>(() => factory.Bind(Json("{}")));
        // Through the registry: the same sound said two ways is one rule's action, twice is refused.
        var registry = new VisualActionRegistry();
        registry.Register(factory);
        Assert.That(registry.Describe().Single().Description, Does.Contain("rain"));
    }

    [Test]
    public async Task TheActionPlaysTheSoundFoundWhenItFiresAndSaysIfItIsGone()
    {
        var library = await Library("rain.wav");
        var played = new List<(string Name, int Seconds)>();
        var factory = new PlayFileFactory(library, (s, n, t) => { played.Add((s.Name, n)); return Task.CompletedTask; });
        var action = factory.Bind(Json("{\"sound\":\"rain\",\"seconds\":30}")).Action;
        var occurrence = (VisualEvent)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(VisualEvent));
        await action.ExecuteAsync(occurrence, CancellationToken.None);
        Assert.That(played, Is.EqualTo(new[] { ("rain", 30) }));
        library.Delete(library.Find("rain"));
        var gone = Assert.ThrowsAsync<InvalidOperationException>(async () => await action.ExecuteAsync(occurrence, CancellationToken.None));
        Assert.That(gone.Message, Does.Contain("rain"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await factory.Bind(Json("{\"sound\":\"lullaby\"}")).Action.ExecuteAsync(occurrence, cancelled.Token));
    }
}
