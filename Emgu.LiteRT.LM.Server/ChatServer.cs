//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Server
{
    /// <summary>A request the server answered, for the host's list of devices and requests.</summary>
    public sealed class ServerRequestInfo
    {
        internal ServerRequestInfo(string device, string remote, string path, int status, TimeSpan duration, long bytes, string error)
        {
            Device = device;
            Remote = remote;
            Path = path;
            Status = status;
            Duration = duration;
            Bytes = bytes;
            Error = error;
        }

        /// <summary>The paired device's name, or null (not paired, or pairing).</summary>
        public string Device { get; }

        /// <summary>The client's address.</summary>
        public string Remote { get; }

        /// <summary>The path, e.g. "/v1/chat/completions".</summary>
        public string Path { get; }

        /// <summary>The HTTP status.</summary>
        public int Status { get; }

        /// <summary>How long it took.</summary>
        public TimeSpan Duration { get; }

        /// <summary>The bytes of the request's body.</summary>
        public long Bytes { get; }

        /// <summary>What went wrong, or null.</summary>
        public string Error { get; }
    }

    /// <summary>
    /// An OpenAI-compatible HTTP server for a chat model on the local network (System.Net.HttpListener, so it runs inside a
    /// Mac Catalyst app): POST /v1/chat/completions (streamed as server-sent events, or whole), POST /v1/embeddings (a search
    /// model, when the host serves one), POST /v1/images/generations (an image model, likewise), GET /v1/models (and LM
    /// Studio's GET /api/v0/models), POST /pair (a device pairs with the code <see cref="ServerPairing"/> shows and gets a token), and
    /// GET / (the server's name, for a device that found it). Every other request carries a paired device's token
    /// (Authorization: Bearer ...); a client address with too many wrong tokens waits a minute. A client that goes away
    /// stops its reply. The tools a request lists are only declared: the model's tool calls go back to the client, which runs
    /// them.
    /// </summary>
    public sealed class ChatServer : IDisposable
    {
        /// <summary>The port it listens on by default: 11435 (Ollama's is 11434).</summary>
        public const int DefaultPort = 11435;

        /// <summary>The largest request body taken (images and audio are sent inline): 32 MB.</summary>
        public const long MaxRequestBytes = 32L * 1024 * 1024;

        private const int MaxWrongTokens = 10;
        private static readonly TimeSpan WrongTokenWindow = TimeSpan.FromMinutes(1);

        private readonly ServerPairing _pairing;
        private readonly Func<ServedChat> _served;
        private readonly Func<ServedEmbeddings> _embeddings;
        private readonly Func<ServedImages> _images;
        private readonly ConcurrentDictionary<string, (int Count, DateTime Since)> _wrongTokens = new ConcurrentDictionary<string, (int, DateTime)>();
        private readonly long _started = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        private HttpListener _listener;
        private CancellationTokenSource _stop;

        /// <summary>Serve a chat model.</summary>
        /// <param name="pairing">The paired devices and the pairing code</param>
        /// <param name="served">Gives the client and the model to serve for each request, or null when none is ready (503)</param>
        /// <param name="embeddings">Gives the search model to serve for each embeddings request, or null when there is none
        /// (503); or null for no search model</param>
        /// <param name="images">Gives the image model to serve for each image request, or null when there is none (503); or
        /// null for no image model</param>
        public ChatServer(ServerPairing pairing, Func<ServedChat> served, Func<ServedEmbeddings> embeddings = null, Func<ServedImages> images = null)
        {
            _pairing = pairing ?? throw new ArgumentNullException(nameof(pairing));
            _served = served ?? throw new ArgumentNullException(nameof(served));
            _embeddings = embeddings;
            _images = images;
        }

        /// <summary>The server's name, given at GET / and to a device that pairs (e.g. the Mac's name).</summary>
        public string Name { get; set; } = Environment.MachineName;

        /// <summary>The port it listens on, once started.</summary>
        public int Port { get; private set; }

        /// <summary>Whether it is listening.</summary>
        public bool IsRunning => _listener?.IsListening == true;

        /// <summary>Raised (on any thread) after each request.</summary>
        public event EventHandler<ServerRequestInfo> RequestCompleted;

        /// <summary>Start listening on every address of the device.</summary>
        /// <param name="port">The port</param>
        /// <exception cref="HttpListenerException">The port is taken, or listening isn't allowed</exception>
        public void Start(int port = DefaultPort)
        {
            if (IsRunning)
                return;
            var listener = new HttpListener();
            listener.Prefixes.Add("http://*:" + port + "/");
            listener.Start();
            _listener = listener;
            _stop = new CancellationTokenSource();
            Port = port;
            _ = Task.Run(() => AcceptAsync(listener, _stop.Token));
        }

        /// <summary>Stop listening; replies being written stop.</summary>
        public void Stop()
        {
            HttpListener listener = Interlocked.Exchange(ref _listener, null);
            _stop?.Cancel();
            if (listener == null)
                return;
            try { listener.Stop(); listener.Close(); }
            catch (Exception) { }
        }

        /// <inheritdoc/>
        public void Dispose() => Stop();

        /// <summary>
        /// The device's addresses a phone can reach it at: IPv4 addresses of the network interfaces that are up (the local
        /// network's, and a VPN's such as Tailscale's 100.x), not the loopback.
        /// </summary>
        public static IList<string> LocalAddresses()
        {
            var addresses = new List<string>();
            try
            {
                foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;
                    foreach (UnicastIPAddressInformation address in network.GetIPProperties().UnicastAddresses)
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address)
                            && !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                            addresses.Add(address.Address.ToString());
                }
            }
            catch (NetworkInformationException) { }
            return addresses.Distinct().ToList();
        }

        private async Task AcceptAsync(HttpListener listener, CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().ConfigureAwait(false); }
                catch (Exception) when (stop.IsCancellationRequested || !listener.IsListening) { return; }
                catch (Exception) { continue; }
                _ = Task.Run(() => HandleAsync(context, stop));
            }
        }

        private async Task HandleAsync(HttpListenerContext context, CancellationToken stop)
        {
            var clock = Stopwatch.StartNew();
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;
            string path = request.Url.AbsolutePath.TrimEnd('/');
            string remote = request.RemoteEndPoint?.Address.ToString() ?? "?";
            string device = null, error = null;
            int status = 200;
            try
            {
                if (request.HttpMethod == "GET" && path.Length == 0)
                    status = await WriteJsonAsync(response, 200, OpenAIChatProtocol.Write(w =>
                    {
                        w.WriteStartObject();
                        w.WriteString("server", "emgu-litert");
                        w.WriteString("name", Name);
                        w.WriteNumber("version", 1);
                        w.WriteEndObject();
                    })).ConfigureAwait(false);
                else if (request.HttpMethod == "POST" && path == "/pair")
                    (status, device) = await PairAsync(request, response, remote).ConfigureAwait(false);
                else
                {
                    PairedDevice paired = Authorize(request, remote, out status, out error);
                    if (paired == null)
                        await WriteErrorAsync(response, status, error, status == 429 ? "rate_limit_error" : "authentication_error",
                            status == 429 ? "rate_limit_exceeded" : "invalid_api_key").ConfigureAwait(false);
                    else
                    {
                        device = paired.Name;
                        status = await RouteAsync(request, response, path, device, stop).ConfigureAwait(false);
                    }
                }
            }
            catch (ChatProtocolException ex)
            {
                status = ex.Status;
                error = ex.Message;
                await WriteErrorAsync(response, status, ex.Message, "invalid_request_error", ex.Code).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsGone(ex))
            {
                // The client went away: nothing to answer.
                status = 499;
                error = "the client went away";
            }
            catch (Exception ex)
            {
                status = 500;
                error = ex.Message;
                await WriteErrorAsync(response, status, ex.Message, "server_error").ConfigureAwait(false);
            }
            finally
            {
                try { response.Close(); }
                catch (Exception) { }
            }
            try
            {
                RequestCompleted?.Invoke(this, new ServerRequestInfo(device, remote, path, status, clock.Elapsed,
                    request.ContentLength64 > 0 ? request.ContentLength64 : 0, error));
            }
            catch (Exception) { }
        }

        private async Task<(int, string)> PairAsync(HttpListenerRequest request, HttpListenerResponse response, string remote)
        {
            if (Blocked(remote))
            {
                await WriteErrorAsync(response, 429, "Too many wrong codes: try again in a minute.", "rate_limit_error", "rate_limit_exceeded").ConfigureAwait(false);
                return (429, null);
            }
            string body = await ReadBodyAsync(request).ConfigureAwait(false);
            string code = null, name = null;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(body))
                {
                    if (document.RootElement.TryGetProperty("code", out JsonElement c))
                        code = c.ValueKind == JsonValueKind.String ? c.GetString() : c.GetRawText();
                    if (document.RootElement.TryGetProperty("device", out JsonElement d) && d.ValueKind == JsonValueKind.String)
                        name = d.GetString();
                }
            }
            catch (JsonException) { throw new ChatProtocolException("The body must be {\"code\": \"123456\", \"device\": \"...\"}."); }
            string token = _pairing.TryPair(code, name);
            if (token == null)
            {
                CountWrong(remote);
                await WriteErrorAsync(response, 401, "Wrong or expired code: show a new one on the server.", "authentication_error", "invalid_code").ConfigureAwait(false);
                return (401, name);
            }
            await WriteJsonAsync(response, 200, OpenAIChatProtocol.Write(w =>
            {
                w.WriteStartObject();
                w.WriteString("token", token);
                w.WriteString("server", Name);
                w.WriteEndObject();
            })).ConfigureAwait(false);
            return (200, name);
        }

        private PairedDevice Authorize(HttpListenerRequest request, string remote, out int status, out string error)
        {
            if (Blocked(remote))
            {
                status = 429;
                error = "Too many wrong tokens: try again in a minute.";
                return null;
            }
            string header = request.Headers["Authorization"] ?? string.Empty;
            string token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header.Substring(7).Trim() : null;
            PairedDevice device = _pairing.Validate(token);
            if (device == null)
            {
                CountWrong(remote);
                status = 401;
                error = token == null ? "Pair with this server first: its requests need a token." : "This device isn't paired with the server (any more): pair it again.";
                return null;
            }
            status = 200;
            error = null;
            return device;
        }

        private bool Blocked(string remote) =>
            _wrongTokens.TryGetValue(remote, out var wrong) && wrong.Count >= MaxWrongTokens && DateTime.UtcNow - wrong.Since < WrongTokenWindow;

        private void CountWrong(string remote) => _wrongTokens.AddOrUpdate(remote, _ => (1, DateTime.UtcNow),
            (_, wrong) => DateTime.UtcNow - wrong.Since >= WrongTokenWindow ? (1, DateTime.UtcNow) : (wrong.Count + 1, wrong.Since));

        private async Task<int> RouteAsync(HttpListenerRequest request, HttpListenerResponse response, string path, string device, CancellationToken stop)
        {
            if (request.HttpMethod == "POST" && (path == "/v1/embeddings" || path == "/embeddings"))
                return await EmbedAsync(request, response, device, stop).ConfigureAwait(false);
            if (request.HttpMethod == "POST" && (path == "/v1/images/generations" || path == "/images/generations"))
                return await DrawAsync(request, response, device, stop).ConfigureAwait(false);
            ServedChat served = _served();
            if (request.HttpMethod == "GET" && (path == "/v1/models" || path == "/models"))
                return await WriteJsonAsync(response, 200, OpenAIChatProtocol.Models(served?.Model, _started, _embeddings?.Invoke()?.Model, _images?.Invoke()?.Model)).ConfigureAwait(false);
            if (request.HttpMethod == "GET" && path == "/api/v0/models")
                return await WriteJsonAsync(response, 200, OpenAIChatProtocol.LmStudioModels(served?.Model, _embeddings?.Invoke()?.Model, _images?.Invoke()?.Model)).ConfigureAwait(false);
            if (request.HttpMethod == "POST" && (path == "/v1/chat/completions" || path == "/chat/completions"))
            {
                if (served == null)
                {
                    await WriteErrorAsync(response, 503, "No model is loaded on the server right now.", "server_error", "model_not_loaded").ConfigureAwait(false);
                    return 503;
                }
                ChatCompletionRequest chat = OpenAIChatProtocol.ParseRequest(await ReadBodyAsync(request).ConfigureAwait(false));
                using (served.BeginRequest?.Invoke(device))
                    return await CompleteAsync(response, served, chat, stop).ConfigureAwait(false);
            }
            await WriteErrorAsync(response, 404, "Not found: " + request.HttpMethod + " " + path, "invalid_request_error").ConfigureAwait(false);
            return 404;
        }

        private async Task<int> EmbedAsync(HttpListenerRequest request, HttpListenerResponse response, string device, CancellationToken stop)
        {
            ServedEmbeddings served = _embeddings?.Invoke();
            if (served == null)
            {
                await WriteErrorAsync(response, 503, "The server has no search model.", "server_error", "model_not_loaded").ConfigureAwait(false);
                return 503;
            }
            EmbeddingRequest embed = OpenAIChatProtocol.ParseEmbeddingRequest(await ReadBodyAsync(request).ConfigureAwait(false));
            GeneratedEmbeddings<Embedding<float>> embeddings;
            using (served.BeginRequest?.Invoke(device))
            {
                try { embeddings = await served.Generator.GenerateAsync(embed.Inputs, embed.Options, stop).ConfigureAwait(false); }
                catch (ArgumentException ex) { throw new ChatProtocolException(ex.Message); }
            }
            return await WriteJsonAsync(response, 200, OpenAIChatProtocol.Embeddings(embeddings, served.Model.Id, embed.Base64)).ConfigureAwait(false);
        }

        // A picture takes seconds to minutes (the model's first load more): the client waits for the whole reply. A client that
        // gives up doesn't stop the picture (HttpListener only tells when the reply is written).
        private async Task<int> DrawAsync(HttpListenerRequest request, HttpListenerResponse response, string device, CancellationToken stop)
        {
            ServedImages served = _images?.Invoke();
            if (served == null)
            {
                await WriteErrorAsync(response, 503, "The server has no image model ready (image generation is off or the model isn't downloaded).", "server_error", "model_not_loaded").ConfigureAwait(false);
                return 503;
            }
            ImageRequest draw = OpenAIChatProtocol.ParseImageRequest(await ReadBodyAsync(request).ConfigureAwait(false), served.Model.ImageSize);
            ServedPicture picture;
            using (served.BeginRequest?.Invoke(device))
            {
                try { picture = await served.Draw(draw.Prompt, draw.Seed, stop).ConfigureAwait(false); }
                catch (ArgumentException ex) { throw new ChatProtocolException(ex.Message); }
            }
            return await WriteJsonAsync(response, 200, OpenAIChatProtocol.Image(picture, draw.Prompt, DateTimeOffset.UtcNow.ToUnixTimeSeconds())).ConfigureAwait(false);
        }

        private async Task<int> CompleteAsync(HttpListenerResponse response, ServedChat served, ChatCompletionRequest chat, CancellationToken stop)
        {
            string id = "chatcmpl-" + Guid.NewGuid().ToString("N").Substring(0, 24);
            string model = served.Model.Id;
            long created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(stop))
            {
                if (!chat.Stream)
                {
                    var updates = new List<ChatResponseUpdate>();
                    try
                    {
                        await foreach (ChatResponseUpdate update in served.Client.GetStreamingResponseAsync(chat.Messages, chat.Options, cancel.Token).ConfigureAwait(false))
                            updates.Add(update);
                    }
                    catch (Exception ex) when (IsContextFull(ex))
                    {
                        await WriteErrorAsync(response, 400, ex.Message, "invalid_request_error", "context_length_exceeded").ConfigureAwait(false);
                        return 400;
                    }
                    return await WriteJsonAsync(response, 200, OpenAIChatProtocol.Completion(updates, id, model, created)).ConfigureAwait(false);
                }
                IAsyncEnumerator<ChatResponseUpdate> stream = served.Client.GetStreamingResponseAsync(chat.Messages, chat.Options, cancel.Token).GetAsyncEnumerator(cancel.Token);
                try
                {
                    // The first update before the headers: a request the model refuses still gets an error status.
                    bool any;
                    try { any = await stream.MoveNextAsync().ConfigureAwait(false); }
                    catch (Exception ex) when (IsContextFull(ex))
                    {
                        await WriteErrorAsync(response, 400, ex.Message, "invalid_request_error", "context_length_exceeded").ConfigureAwait(false);
                        return 400;
                    }
                    response.StatusCode = 200;
                    response.ContentType = "text/event-stream";
                    response.SendChunked = true;
                    response.Headers["Cache-Control"] = "no-cache";
                    Stream output = response.OutputStream;
                    bool first = true;
                    int toolIndex = 0;
                    ChatFinishReason? finish = null;
                    while (any)
                    {
                        ChatResponseUpdate update = stream.Current;
                        if (update.FinishReason != null)
                            finish = update.FinishReason;
                        string chunk = OpenAIChatProtocol.Chunk(update, id, model, created, first, ref toolIndex);
                        if (chunk != null)
                        {
                            first = false;
                            // A client that went away makes the write fail: the reply stops (the model's cancellation).
                            try { await WriteEventAsync(output, chunk, cancel.Token).ConfigureAwait(false); }
                            catch (Exception ex) when (IsGone(ex))
                            {
                                cancel.Cancel();
                                throw;
                            }
                        }
                        any = await stream.MoveNextAsync().ConfigureAwait(false);
                    }
                    await WriteEventAsync(output, OpenAIChatProtocol.FinalChunk(id, model, created, finish ?? (toolIndex > 0 ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop)), cancel.Token).ConfigureAwait(false);
                    await WriteEventAsync(output, "[DONE]", cancel.Token).ConfigureAwait(false);
                    return 200;
                }
                finally
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        // LiteRT-LM's LiteRtLmContextException (Emgu.LiteRT.LM.Extensions.AI), known by name: this library doesn't reference LiteRT.
        private static bool IsContextFull(Exception ex) => ex.GetType().Name == "LiteRtLmContextException";

        private static bool IsGone(Exception ex) => ex is HttpListenerException || ex is IOException || ex is ObjectDisposedException
            || (ex is OperationCanceledException && !(ex.InnerException is TimeoutException));

        private static async Task WriteEventAsync(Stream output, string data, CancellationToken cancel)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("data: " + data + "\n\n");
            await output.WriteAsync(bytes, 0, bytes.Length, cancel).ConfigureAwait(false);
            await output.FlushAsync(cancel).ConfigureAwait(false);
        }

        private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
        {
            if (request.ContentLength64 > MaxRequestBytes)
                throw new ChatProtocolException("The request is larger than " + MaxRequestBytes / (1024 * 1024) + " MB.", 413);
            using (var memory = new MemoryStream())
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = await request.InputStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    memory.Write(buffer, 0, read);
                    if (memory.Length > MaxRequestBytes)
                        throw new ChatProtocolException("The request is larger than " + MaxRequestBytes / (1024 * 1024) + " MB.", 413);
                }
                return Encoding.UTF8.GetString(memory.ToArray());
            }
        }

        private static async Task<int> WriteJsonAsync(HttpListenerResponse response, int status, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            response.StatusCode = status;
            response.ContentType = "application/json";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            return status;
        }

        private static async Task WriteErrorAsync(HttpListenerResponse response, int status, string message, string type, string code = null)
        {
            try { await WriteJsonAsync(response, status, OpenAIChatProtocol.Error(message, type, code)).ConfigureAwait(false); }
            catch (Exception) { }
        }
    }
}
