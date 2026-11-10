//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Server
{
    /// <summary>
    /// A phone's side of <see cref="ChatServer"/>'s POST /v1/images/generations: draws a picture with the server's image model.
    /// The address and the token are read for each request (a paired Mac found again at another address). A picture can take
    /// minutes (the model's first load on the server more), so a request may take up to <see cref="Timeout"/>; a server that
    /// isn't answering fails within seconds (<see cref="ServerReachability"/>).
    /// </summary>
    public sealed class ServerImageGenerator : IDisposable
    {
        /// <summary>The longest a picture may take: 20 minutes.</summary>
        public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(20);

        private readonly Func<string> _url;
        private readonly Func<string> _token;
        private readonly HttpClient _http;

        /// <summary>Draw on a server.</summary>
        /// <param name="url">Gives the server's API address, e.g. "http://192.168.1.20:11435/v1"</param>
        /// <param name="token">Gives the paired device's token, or null for none</param>
        /// <param name="model">The server's image model id</param>
        /// <param name="handler">The HTTP handler, or null for <see cref="ServerReachability.CreateHandler"/>'s; the generator
        /// doesn't dispose one given</param>
        public ServerImageGenerator(Func<string> url, Func<string> token, string model, HttpMessageHandler handler = null)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
            _token = token;
            if (string.IsNullOrEmpty(model))
                throw new ArgumentException("A model id is needed", nameof(model));
            Model = model;
            _http = handler == null ? new HttpClient(ServerReachability.CreateHandler()) : new HttpClient(handler, false);
            _http.Timeout = Timeout;
        }

        /// <summary>The server's image model id.</summary>
        public string Model { get; }

        /// <summary>Draw a picture.</summary>
        /// <param name="prompt">What to draw</param>
        /// <param name="seed">The seed, or null for one the server chooses</param>
        /// <param name="cancel">Stops waiting (the server finishes the picture)</param>
        /// <returns>The picture (PNG), its seed and how long the server took</returns>
        /// <exception cref="InvalidOperationException">The server refused (its message), or isn't answering</exception>
        public async Task<ServedPicture> GenerateAsync(string prompt, long? seed, CancellationToken cancel)
        {
            string body = OpenAIChatProtocol.Write(w =>
            {
                w.WriteStartObject();
                w.WriteString("model", Model);
                w.WriteString("prompt", prompt ?? string.Empty);
                w.WriteNumber("n", 1);
                w.WriteString("response_format", "b64_json");
                if (seed is long value)
                    w.WriteNumber("seed", value);
                w.WriteEndObject();
            });
            string url = (_url() ?? string.Empty).TrimEnd('/');
            if (url.Length == 0)
                throw new InvalidOperationException("The server's address isn't set.");
            using (var request = new HttpRequestMessage(HttpMethod.Post, url + "/images/generations") { Content = new StringContent(body, Encoding.UTF8, "application/json") })
            {
                string token = _token?.Invoke();
                if (!string.IsNullOrEmpty(token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                HttpResponseMessage sent;
                try { sent = await _http.SendAsync(request, cancel).ConfigureAwait(false); }
                catch (HttpRequestException ex) when (ServerReachability.IsUnreachable(ex))
                {
                    throw new InvalidOperationException("The server isn't answering - it may be asleep, stopped or on another network.", ex);
                }
                using (HttpResponseMessage response = sent)
                {
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using (JsonDocument document = JsonDocument.Parse(json))
                    {
                        JsonElement root = document.RootElement;
                        if (!response.IsSuccessStatusCode)
                        {
                            string message = root.TryGetProperty("error", out JsonElement error) && error.TryGetProperty("message", out JsonElement text)
                                ? text.GetString() : response.ReasonPhrase;
                            throw new InvalidOperationException(string.Format("The server answered {0}: {1}", (int)response.StatusCode, message));
                        }
                        JsonElement picture = root.GetProperty("data")[0];
                        return new ServedPicture(Convert.FromBase64String(picture.GetProperty("b64_json").GetString()),
                            picture.TryGetProperty("seed", out JsonElement s) && s.TryGetInt64(out long drawn) ? drawn : seed ?? 0,
                            TimeSpan.FromMilliseconds(picture.TryGetProperty("elapsed_ms", out JsonElement ms) && ms.TryGetInt64(out long elapsed) ? elapsed : 0));
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose() => _http.Dispose();
    }
}
