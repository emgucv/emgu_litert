using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.Util;
using NUnit.Framework;
using Maui.Demo.Lite;

namespace Emgu.TF.Lite.Test;

[TestFixture]
public class UnitTestDownloads
{
    private string _directory;
    private static readonly byte[] Data = CreateData(3 * 1024 * 1024, 123);
    private static byte[] CreateData(int length, int seed)
    {
        byte[] bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    [SetUp]
    public void SetUp()
    {
        _directory = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.WorkDirectory, "download-test-" + Guid.NewGuid()));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        // Delete only this test's explicitly created scratch directory.
        if (Path.GetDirectoryName(_directory) != Path.GetFullPath(TestContext.CurrentContext.WorkDirectory)
            || !Path.GetFileName(_directory).StartsWith("download-test-", StringComparison.Ordinal))
            throw new Exception("Unexpected test scratch path");
        Directory.Delete(_directory, true);
    }

    private DownloadableFile FileFor(Server server, byte[] expected = null) => new DownloadableFile(server.Url, "", Hash(expected ?? Data))
    { LocalFile = Path.Combine(_directory, "model.bin") };
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static FileDownloadManager Manager(DownloadableFile file)
    {
        var manager = new FileDownloadManager();
        manager.AddFile(file);
        return manager;
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static async Task Interrupted(FileDownloadManager manager, CancellationToken token = default)
    {
        if (!token.CanBeCanceled) token = TestContext.CurrentContext.CancellationToken;
        bool failed = false;
        try { await manager.Download(0, token); }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.Net.Http.HttpRequestException || ex is OperationCanceledException) { failed = true; }
        Check(failed, "Expected interrupted download");
    }
    private static void Complete(DownloadableFile file, byte[] expected = null)
    {
        Check(System.IO.File.ReadAllBytes(file.LocalFile).SequenceEqual(expected ?? Data), "Downloaded bytes differ");
        Check(file.IsLocalFileVerified, "Completed file was not checksum-verified");
        Check(!System.IO.File.Exists(file.PartialFile) && !System.IO.File.Exists(file.PartialFile + ".etag"), "Partial artifacts remain");
    }

    [Test, CancelAfter(20000)]
    public async Task TestFirstDownloadCreatesMissingModelDirectory()
    {
        using var server = new Server((r, n) => Response.Full(Data));
        var file = FileFor(server);
        file.LocalFile = Path.Combine(_directory, "new-model", "nested", "model.tflite");
        Check(!Directory.Exists(file.LocalFolder), "Test must start without the model directory");
        await Manager(file).Download(0, TestContext.CurrentContext.CancellationToken);
        Complete(file);
    }

    [Test, CancelAfter(20000)]
    public async Task TestDownloadPublishesOnlyAfterVerification()
    {
        using var server = new Server((r, n) => Response.Full(Data));
        var file = FileFor(server);
        var manager = Manager(file);
        manager.OnDownloadProgressChanged += (_, _, _) => Check(!System.IO.File.Exists(file.LocalFile), "Final file exposed before verification");
        await manager.Download(0, TestContext.CurrentContext.CancellationToken);
        Complete(file);
    }

    [Test, CancelAfter(20000)]
    public async Task TestDownloadResumesAfterDisconnectWithNewManager()
    {
        using var server = new Server((r, n) => n == 1 ? Response.Full(Data, send: 524288) : Response.Range(Data, r.Offset));
        var file = FileFor(server);
        await Interrupted(Manager(file));
        long saved = file.PartialDownloadBytes;
        Check(saved > 0 && saved < Data.Length && file.PartialDownloadTotalBytes == Data.Length, "Partial state missing");
        Check(!System.IO.File.Exists(file.LocalFile), "Partial final file exposed");
        file = FileFor(server); // No in-memory download state survives this step.
        var manager = Manager(file);
        var progress = new List<long>();
        manager.OnDownloadProgressChanged += (_, received, _) => progress.Add(received);
        await manager.Download(0, TestContext.CurrentContext.CancellationToken);
        Check(server.Requests[1].Offset == saved && server.Requests[1].IfRange == "\"v1\"", "Missing Range/If-Range");
        Check(progress[0] == saved, "Progress did not include retained bytes");
        Complete(file);
    }

    [Test, CancelAfter(20000)]
    public async Task TestBackgroundPauseRetainsVerifiedResumeState()
    {
        using var server = new Server((r, n) => r.Offset > 0 ? Response.Range(Data, r.Offset) : Response.Full(Data));
        var file = FileFor(server);
        var registry = new DownloadActivityRegistry();
        using (var activity = registry.Begin("model", TestContext.CurrentContext.CancellationToken))
        {
            var manager = Manager(file);
            manager.OnDownloadProgressChanged += (total, received, percent) =>
            {
                activity.Report(total, received);
                if (received >= 524288) registry.PauseAll();
            };
            await Interrupted(manager, activity.Token);
        }
        long saved = file.PartialDownloadBytes;
        Check(saved >= 524288 && saved < Data.Length, "Background pause lost partial bytes");
        using (var activity = registry.Begin("model", TestContext.CurrentContext.CancellationToken))
            await Manager(file).Download(0, activity.Token);
        Check(server.Requests.Last().Offset == saved && server.Requests.Last().IfRange == "\"v1\"", "Resume after background expiration did not use validators");
        Complete(file);
        Check(registry.Snapshot().Length == 0, "Completed background download still active");
    }

    [Test, CancelAfter(20000)]
    public async Task TestDownloadCancellationRetainsPartialAndResumes()
    {
        using var server = new Server((r, n) => n == 1 ? Response.Full(Data) : Response.Range(Data, r.Offset));
        var file = FileFor(server);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CurrentContext.CancellationToken);
        var manager = Manager(file);
        manager.OnDownloadProgressChanged += (_, received, _) => { if (received >= 819200) cancel.Cancel(); };
        await Interrupted(manager, cancel.Token);
        long saved = file.PartialDownloadBytes;
        Check(saved > 0 && !System.IO.File.Exists(file.LocalFile), "Cancellation lost partial or published it");
        await Manager(FileFor(server)).Download(0, TestContext.CurrentContext.CancellationToken);
        Check(server.Requests[1].Offset == saved, "Cancellation restarted from zero");
        Complete(file);
    }

    [Test, CancelAfter(20000)]
    public async Task TestDownloadIgnoredRangeReplacesInsteadOfAppending()
    {
        using var server = new Server((r, n) => Response.Full(Data, send: n == 1 ? 524288 : null));
        var file = FileFor(server);
        await Interrupted(Manager(file));
        await Manager(file).Download(0, TestContext.CurrentContext.CancellationToken);
        Check(server.Requests[1].Offset > 0, "Resume was not attempted");
        Complete(file);
    }

    [Test, CancelAfter(20000)]
    public async Task TestDownloadChangedEtagRestartsSafely()
    {
        byte[] old = CreateData(Data.Length, 456);
        using var server = new Server((r, n) => n == 1 ? Response.Full(old, send: 524288) : Response.Full(Data, "\"v2\""));
        var file = FileFor(server);
        await Interrupted(Manager(file));
        await Manager(file).Download(0, TestContext.CurrentContext.CancellationToken);
        Check(server.Requests[1].IfRange == "\"v1\"", "Old validator was not sent");
        Complete(file);
    }

    [Test, CancelAfter(20000)]
    public async Task TestDownloadWithoutStrongEtagRestarts()
    {
        using var server = new Server((r, n) => Response.Full(Data, "W/\"weak\"", n == 1 ? 524288 : null));
        var file = FileFor(server);
        await Interrupted(Manager(file));
        await Manager(file).Download(0, TestContext.CurrentContext.CancellationToken);
        Check(!server.Requests[1].Headers.ContainsKey("Range"), "Unsafe weak-validator resume");
        Complete(file);
    }

    [Test, CancelAfter(20000)]
    public async Task TestDownloadRejectsIncorrectRangeThenRetriesFresh()
    {
        using var server = new Server((r, n) => n == 1 ? Response.Full(Data, send: 524288)
            : n == 2 ? Response.Range(Data, r.Offset + 1) : Response.Full(Data));
        var file = FileFor(server);
        await Interrupted(Manager(file));
        await Manager(file).Download(1, TestContext.CurrentContext.CancellationToken);
        Check(server.Requests.Count == 3 && !server.Requests[2].Headers.ContainsKey("Range"), "Invalid range not discarded");
        Complete(file);
    }

    [Test, CancelAfter(20000)]
    public async Task TestDownloadChecksumMismatchRetriesFromZero()
    {
        using var server = new Server((r, n) => Response.Full(n == 1 ? CreateData(Data.Length, 789) : Data));
        var file = FileFor(server);
        await Manager(file).Download(1, TestContext.CurrentContext.CancellationToken);
        Check(server.Requests.Count == 2 && !server.Requests[1].Headers.ContainsKey("Range"), "Bad checksum retained");
        Complete(file);
    }

    [Test, CancelAfter(20000)]
    public async Task TestDownloadCompletePartialHandles416()
    {
        using var server = new Server((r, n) => n == 1 ? Response.Full(Data)
            : new Response { Status = 416, Body = Array.Empty<byte>(), ContentRange = "bytes */" + Data.Length });
        var file = FileFor(server);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CurrentContext.CancellationToken);
        var manager = Manager(file);
        manager.OnDownloadProgressChanged += (total, received, _) => { if (received == total) cancel.Cancel(); };
        await Interrupted(manager, cancel.Token);
        Check(file.PartialDownloadBytes == Data.Length, "Expected complete unpublished partial");
        await Manager(file).Download(0, TestContext.CurrentContext.CancellationToken);
        Check(server.Requests[1].Offset == Data.Length, "Complete range not requested");
        Complete(file);
    }

    [Test, CancelAfter(20000)]
    public async Task TestDownloadChangedChecksumDoesNotReuseOldPartial()
    {
        byte[] replacement = CreateData(Data.Length, 789);
        using var server = new Server((r, n) => n == 1 ? Response.Full(Data, send: 524288) : Response.Full(replacement));
        await Interrupted(Manager(FileFor(server)));
        var file = FileFor(server, replacement);
        await Manager(file).Download(0, TestContext.CurrentContext.CancellationToken);
        Check(!server.Requests[1].Headers.ContainsKey("Range"), "Partial from another checksum reused");
        Complete(file, replacement);
    }

    [Test, CancelAfter(20000)]
    public async Task TestDownloadRejectsChangedValidatorOn206()
    {
        using var server = new Server((r, n) => n == 1 ? Response.Full(Data, send: 524288)
            : n == 2 ? new Response { Status = 206, Etag = "\"v2\"", Body = Data.Skip((int)r.Offset).ToArray(), ContentRange = $"bytes {r.Offset}-{Data.Length - 1}/{Data.Length}" }
            : Response.Full(Data));
        var file = FileFor(server);
        await Interrupted(Manager(file));
        await Manager(file).Download(1, TestContext.CurrentContext.CancellationToken);
        Check(server.Requests.Count == 3 && !server.Requests[2].Headers.ContainsKey("Range"), "Changed validator accepted for append");
        Complete(file);
    }

    [Test, CancelAfter(20000)]
    public async Task TestDownloadCombinesBoundedRangesWithOmittedRepeatedEtag()
    {
        using var server = new Server((r, n) =>
        {
            long end = Math.Min(r.Offset + 1024 * 1024, Data.Length);
            return new Response { Status = 206, Etag = n == 1 ? "\"v1\"" : null,
                Body = Data.Skip((int)r.Offset).Take((int)(end - r.Offset)).ToArray(),
                ContentRange = $"bytes {r.Offset}-{end - 1}/{Data.Length}" };
        });
        var file = FileFor(server);
        await Manager(file).Download(0, TestContext.CurrentContext.CancellationToken);
        Check(server.Requests.Count == 3 && server.Requests[2].IfRange == "\"v1\"", "Bounded ranges lost strong validator");
        Complete(file);
    }

    [Test, CancelAfter(20000)]
    public async Task TestDownloadExhaustedChecksumFailureNeverPublishes()
    {
        using var server = new Server((r, n) => Response.Full(CreateData(Data.Length, 456)));
        var file = FileFor(server);
        await Interrupted(Manager(file));
        Check(!System.IO.File.Exists(file.LocalFile) && file.PartialDownloadBytes == 0
            && !System.IO.File.Exists(file.PartialFile + ".etag"), "Checksum failure was published or left resumable");
    }

    private sealed class Request
    {
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public long Offset => Headers.TryGetValue("Range", out string range) ? long.Parse(range.Substring(6).TrimEnd('-')) : 0;
        public string IfRange => Headers.TryGetValue("If-Range", out string value) ? value : null;
    }
    private sealed class Response
    {
        public int Status = 200;
        public byte[] Body;
        public string Etag = "\"v1\"";
        public string ContentRange;
        public int? Send;
        public static Response Full(byte[] body, string etag = "\"v1\"", int? send = null) => new Response { Body = body, Etag = etag, Send = send };
        public static Response Range(byte[] body, long offset) => new Response
        {
            Status = 206, Body = body.Skip((int)offset).ToArray(), ContentRange = $"bytes {offset}-{body.Length - 1}/{body.Length}"
        };
    }
    // Real loopback HTTP, including premature EOF. No public network, model files, or native libraries.
    private sealed class Server : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CurrentContext.CancellationToken);
        private readonly Task _loop;
        public readonly List<Request> Requests = new List<Request>();
        public string Url { get; }
        public Server(Func<Request, int, Response> respond)
        {
            _listener.Start();
            Url = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/model";
            _loop = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                    catch (Exception) when (_stop.IsCancellationRequested) { return; }
                    using (client)
                    using (NetworkStream stream = client.GetStream())
                    {
                        try
                        {
                            var request = new Request();
                            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                            await reader.ReadLineAsync().ConfigureAwait(false);
                            string line;
                            while (!String.IsNullOrEmpty(line = await reader.ReadLineAsync().ConfigureAwait(false)))
                            {
                                int colon = line.IndexOf(':');
                                request.Headers[line.Substring(0, colon)] = line.Substring(colon + 1).Trim();
                            }
                            Requests.Add(request);
                            Response response = respond(request, Requests.Count);
                            string headers = $"HTTP/1.1 {response.Status} Test\r\nConnection: close\r\nContent-Length: {response.Body.Length}\r\n";
                            if (response.Etag != null) headers += "ETag: " + response.Etag + "\r\n";
                            if (response.ContentRange != null) headers += "Content-Range: " + response.ContentRange + "\r\n";
                            byte[] head = Encoding.ASCII.GetBytes(headers + "\r\n");
                            await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
                            int send = response.Send ?? response.Body.Length;
                            for (int start = 0; start < send; start += 65536)
                            {
                                await stream.WriteAsync(response.Body, start, Math.Min(65536, send - start)).ConfigureAwait(false);
                                await Task.Delay(1).ConfigureAwait(false);
                            }
                        }
                        catch (IOException) { } // Cancellation/disconnection by the client is intentional.
                    }
                }
            });
        }
        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _loop.GetAwaiter().GetResult();
            _stop.Dispose();
        }
    }
}

