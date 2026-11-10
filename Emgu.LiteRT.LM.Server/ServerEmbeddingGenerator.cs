//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Server
{
    /// <summary>
    /// A phone's side of <see cref="ChatServer"/>'s POST /v1/embeddings: an <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>
    /// that embeds texts with the server's search model. Unlike the OpenAI SDK's, it sends the options' additional property
    /// "input_type" ("query" or "document"), so EmbeddingGemma on the server adds its prefixes. The address and the token are
    /// read for each request (a paired Mac found again at another address); more than
    /// <see cref="OpenAIChatProtocol.MaxEmbeddingInputs"/> texts go in several requests.
    /// </summary>
    public sealed class ServerEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly Func<string> _url;
        private readonly Func<string> _token;
        private readonly HttpClient _http;
        private readonly EmbeddingGeneratorMetadata _metadata;

        /// <summary>Embed on a server.</summary>
        /// <param name="url">Gives the server's API address, e.g. "http://192.168.1.20:11435/v1"</param>
        /// <param name="token">Gives the paired device's token, or null for none</param>
        /// <param name="model">The server's search model id</param>
        /// <param name="handler">The HTTP handler, or null for the default; the generator doesn't dispose it</param>
        public ServerEmbeddingGenerator(Func<string> url, Func<string> token, string model, HttpMessageHandler handler = null)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
            _token = token;
            if (string.IsNullOrEmpty(model))
                throw new ArgumentException("A model id is needed", nameof(model));
            // A search model that isn't loaded yet loads on the first request.
            _http = handler == null ? new HttpClient() : new HttpClient(handler, false);
            _http.Timeout = TimeSpan.FromMinutes(2);
            _metadata = new EmbeddingGeneratorMetadata("emgu-litert", null, model);
        }

        /// <summary>The server's search model id.</summary>
        public string Model => _metadata.DefaultModelId;

        /// <inheritdoc/>
        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
            EmbeddingGenerationOptions options = null, CancellationToken cancellationToken = default)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            List<string> texts = values.ToList();
            var embeddings = new List<Embedding<float>>(texts.Count);
            for (int start = 0; start < texts.Count; start += OpenAIChatProtocol.MaxEmbeddingInputs)
                embeddings.AddRange(await RequestAsync(texts.Skip(start).Take(OpenAIChatProtocol.MaxEmbeddingInputs).ToList(), options, cancellationToken).ConfigureAwait(false));
            return new GeneratedEmbeddings<Embedding<float>>(embeddings);
        }

        private async Task<List<Embedding<float>>> RequestAsync(List<string> texts, EmbeddingGenerationOptions options, CancellationToken cancel)
        {
            string body = OpenAIChatProtocol.Write(w =>
            {
                w.WriteStartObject();
                w.WriteString("model", Model);
                w.WriteStartArray("input");
                foreach (string text in texts)
                    w.WriteStringValue(text ?? string.Empty);
                w.WriteEndArray();
                w.WriteString("encoding_format", "base64");
                if (options?.Dimensions is int dimensions)
                    w.WriteNumber("dimensions", dimensions);
                if (options?.AdditionalProperties != null && options.AdditionalProperties.TryGetValue("input_type", out object type) && type != null)
                    w.WriteString("input_type", type.ToString());
                w.WriteEndObject();
            });
            string url = (_url() ?? string.Empty).TrimEnd('/');
            if (url.Length == 0)
                throw new InvalidOperationException("The server's address isn't set.");
            using (var request = new HttpRequestMessage(HttpMethod.Post, url + "/embeddings") { Content = new StringContent(body, Encoding.UTF8, "application/json") })
            {
                string token = _token?.Invoke();
                if (!string.IsNullOrEmpty(token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using (HttpResponseMessage response = await _http.SendAsync(request, cancel).ConfigureAwait(false))
                {
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException(string.Format("The server answered {0}: {1}", (int)response.StatusCode, ErrorMessage(json) ?? response.ReasonPhrase));
                    return ReadEmbeddings(json, texts.Count);
                }
            }
        }

        private List<Embedding<float>> ReadEmbeddings(string json, int count)
        {
            var vectors = new float[count][];
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                foreach (JsonElement item in document.RootElement.GetProperty("data").EnumerateArray())
                {
                    int index = item.GetProperty("index").GetInt32();
                    if (index < 0 || index >= count)
                        throw new InvalidOperationException("The server gave an embedding for text " + index + " of " + count + ".");
                    JsonElement embedding = item.GetProperty("embedding");
                    if (embedding.ValueKind == JsonValueKind.String)
                    {
                        byte[] bytes = Convert.FromBase64String(embedding.GetString());
                        float[] vector = new float[bytes.Length / sizeof(float)];
                        for (int i = 0; i < vector.Length; i++)
                        {
                            if (!BitConverter.IsLittleEndian)
                                Array.Reverse(bytes, i * sizeof(float), sizeof(float));
                            vector[i] = BitConverter.ToSingle(bytes, i * sizeof(float));
                        }
                        vectors[index] = vector;
                    }
                    else
                        vectors[index] = embedding.EnumerateArray().Select(e => e.GetSingle()).ToArray();
                }
            }
            if (vectors.Any(v => v == null))
                throw new InvalidOperationException("The server gave fewer embeddings than texts.");
            DateTimeOffset created = DateTimeOffset.UtcNow;
            return vectors.Select(v => new Embedding<float>(v) { ModelId = Model, CreatedAt = created }).ToList();
        }

        // An OpenAI-shaped error's message, or null.
        private static string ErrorMessage(string json)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                    return document.RootElement.TryGetProperty("error", out JsonElement error) && error.TryGetProperty("message", out JsonElement message)
                        ? message.GetString() : null;
            }
            catch (JsonException) { return null; }
        }

        /// <inheritdoc/>
        public object GetService(Type serviceType, object serviceKey = null)
        {
            if (serviceType == null)
                throw new ArgumentNullException(nameof(serviceType));
            if (serviceKey != null)
                return null;
            if (serviceType == typeof(EmbeddingGeneratorMetadata))
                return _metadata;
            return serviceType.IsInstanceOfType(this) ? this : null;
        }

        /// <inheritdoc/>
        public void Dispose() => _http.Dispose();
    }
}
