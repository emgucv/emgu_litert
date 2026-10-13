//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// Downloads byte ranges of files over HTTP into local files, resuming from a ".part" file after an interruption;
    /// used for ZImageTurbo's graphs (whole files) and for single tensors of multi-GB safetensors checkpoints.
    /// </summary>
    internal sealed class RangeDownloader : IDisposable
    {
        private readonly HttpClient _client;

        public RangeDownloader()
        {
            _client = new HttpClient();
            _client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("Emgu.LiteRT.Models");
        }

        // Download bytes [start, end) of url to file, through file + ".part"; onBytes is called with the bytes of this
        // file done, now and then.
        public async Task Download(String url, long start, long end, String file, Action<long> onBytes, CancellationToken cancellationToken)
        {
            String part = file + ".part";
            long want = end - start;
            long have = File.Exists(part) ? Math.Min(new FileInfo(part).Length, want) : 0;
            if (have < want)
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Range = new RangeHeaderValue(start + have, end - 1);
                    using (HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new IOException(String.Format("HTTP {0} for {1}", (int)response.StatusCode, url));
                        // A small file may come back whole (200) despite the range: start it over.
                        bool append = response.StatusCode == HttpStatusCode.PartialContent;
                        if (!append)
                        {
                            if (start != 0)
                                throw new IOException("The server ignored the range request for " + url);
                            have = 0;
                        }
                        using (Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (FileStream target = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                        {
                            byte[] buffer = new byte[1 << 20];
                            long lastReport = have;
                            while (have < want)
                            {
                                int read = await source.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, want - have), cancellationToken).ConfigureAwait(false);
                                if (read <= 0)
                                    break;
                                await target.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                                have += read;
                                if (have - lastReport >= (8 << 20))
                                {
                                    lastReport = have;
                                    if (onBytes != null)
                                        onBytes(have);
                                }
                            }
                        }
                    }
                }
            }
            long got = new FileInfo(part).Length;
            if (got != want)
                throw new IOException(String.Format("{0}: got {1} of {2} bytes", Path.GetFileName(file), got, want));
            if (File.Exists(file))
                File.Delete(file);
            File.Move(part, file);
            if (onBytes != null)
                onBytes(want);
        }

        private async Task<byte[]> ReadRange(String url, long start, long end, CancellationToken cancellationToken)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Range = new RangeHeaderValue(start, end - 1);
                using (HttpResponseMessage response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    if (response.StatusCode != HttpStatusCode.PartialContent)
                        throw new IOException(String.Format("HTTP {0} for a range of {1}", (int)response.StatusCode, url));
                    byte[] data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    if (data.Length != end - start)
                        throw new IOException(String.Format("Got {0} of {1} bytes of {2}", data.Length, end - start, url));
                    return data;
                }
            }
        }

        /// <summary>
        /// Read the header of a safetensors file: an 8-byte little-endian length, then that much JSON with each tensor's
        /// dtype, shape and data offsets (relative to the end of the header).
        /// </summary>
        public async Task<SafetensorsHeader> ReadSafetensorsHeader(String url, CancellationToken cancellationToken)
        {
            byte[] lengthBytes = await ReadRange(url, 0, 8, cancellationToken).ConfigureAwait(false);
            long length = BitConverter.ToInt64(lengthBytes, 0);
            if (length <= 0 || length > (100 << 20))
                throw new IOException("Not a safetensors header: " + url);
            byte[] json = await ReadRange(url, 8, 8 + length, cancellationToken).ConfigureAwait(false);
            return new SafetensorsHeader(Encoding.UTF8.GetString(json), 8 + length);
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }

    /// <summary>
    /// The tensors of a safetensors file, with their absolute byte ranges
    /// </summary>
    internal sealed class SafetensorsHeader
    {
        public sealed class Tensor
        {
            public String DType;
            public long Start;
            public long End;
        }

        private readonly Dictionary<String, Tensor> _tensors = new Dictionary<String, Tensor>();

        public SafetensorsHeader(String json, long dataStart)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                {
                    if (property.Name == "__metadata__")
                        continue;
                    JsonElement offsets = property.Value.GetProperty("data_offsets");
                    _tensors[property.Name] = new Tensor
                    {
                        DType = property.Value.GetProperty("dtype").GetString(),
                        Start = dataStart + offsets[0].GetInt64(),
                        End = dataStart + offsets[1].GetInt64()
                    };
                }
            }
        }

        public Tensor Get(String name, String dtype)
        {
            Tensor tensor;
            if (!_tensors.TryGetValue(name, out tensor))
                throw new IOException("The checkpoint has no tensor " + name);
            if (tensor.DType != dtype)
                throw new IOException(String.Format("{0} is {1}, expected {2}", name, tensor.DType, dtype));
            return tensor;
        }
    }
}
