//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Collections.Concurrent;

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID || UNITY_STANDALONE
using UnityEngine;
#endif

namespace Emgu.LiteRT.Util
{
    /// <summary>
    /// Use to download files (e.g. models) from the internet
    /// </summary>
    public class FileDownloadManager
    {
        /// <summary>
        /// Create a file download manager
        /// </summary>
        public FileDownloadManager()
        {
        }

        /// <summary>
        /// The call back when download progress has been changed.
        /// </summary>
        /// <param name="totalBytesToReceive">The total number of bytes to receive. If null, it is unknown.</param>
        /// <param name="bytesReceived">The total number of bytes currently received.</param>
        /// <param name="progressPercentage">The progress percentage. If null, it is unknown.</param>
        public delegate void DownloadProgressChangedEventHandler(long? totalBytesToReceive, long bytesReceived, double? progressPercentage);

        /// <summary>
        /// This event will be fired when the download progress is changed
        /// </summary>
        public event DownloadProgressChangedEventHandler OnDownloadProgressChanged;

        private List<DownloadableFile> _files = new List<DownloadableFile>();

        /// <summary>
        /// Clear the list of files
        /// </summary>
        public void Clear()
        {
            _files.Clear();
        }

        /// <summary>
        /// Add a file to download
        /// </summary>
        /// <param name="url">The url of the file to be downloaded</param>
        /// <param name="localSubfolder">The local subfolder name to download the model to.</param>
        /// <param name="sha256Hash">The sha256 hash value for the file</param>
        public void AddFile(String url, String localSubfolder, String sha256Hash = null)
        {
            _files.Add(new DownloadableFile(url, localSubfolder, sha256Hash));
        }

        /// <summary>
        /// Add a file to download
        /// </summary>
        /// <param name="downloadableFile">The file to be downloaded</param>
        public void AddFile(DownloadableFile downloadableFile)
        {
            _files.Add(downloadableFile);
        }

        /// <summary>
        /// Return true if all files has been downloaded and are valid.
        /// </summary>
        public bool AllFilesDownloaded
        {
            get
            {
                bool allDownloaded = true;
                foreach (DownloadableFile file in _files)
                {
                    allDownloaded &= file.IsLocalFileValid;
                }

                return allDownloaded;
            }
        }

        /// <summary>
        /// Get the files that will be downloaded by this download manager.
        /// </summary>
        public DownloadableFile[] Files
        {
            get
            {
                return _files.ToArray();
            }
        }

#if UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID || UNITY_STANDALONE || UNITY_WEBGL
        public UnityEngine.Networking.UnityWebRequest CurrentWebClient = null;

        public IEnumerator Download()
        {
            foreach (DownloadableFile df in _files)
            {
                //Uncomment the following to force re-download every time
                //File.Delete(localFileName);
                if (!df.IsLocalFileValid)
                {
                    String localFileName = df.LocalFile;
                    using (UnityEngine.Networking.UnityWebRequest webclient = new UnityEngine.Networking.UnityWebRequest(df.Url))
                    {
                        CurrentWebClient = webclient;
                        UnityEngine.Debug.Log(String.Format("Downloading file from '{0}' to '{1}'", df.Url, localFileName));

                        webclient.downloadHandler = new UnityEngine.Networking.DownloadHandlerFile(localFileName);
                        yield return webclient.SendWebRequest();
                        if (webclient.result == UnityEngine.Networking.UnityWebRequest.Result.ConnectionError 
                            || webclient.result == UnityEngine.Networking.UnityWebRequest.Result.ProtocolError)
                        {
                            UnityEngine.Debug.LogError(webclient.error);
                        }

                        if (df.IsLocalFileValid)
                        {
                            UnityEngine.Debug.Log("File successfully downloaded and saved to " + localFileName);
                        }
                        else
                        {
                            UnityEngine.Debug.LogError(String.Format("Failed to download file {0}.", localFileName));
                        }
                        CurrentWebClient = null;

                    }
                }
            }

            UnityEngine.Debug.Log("All download completed.");
        }
#else
        /// <summary>
        /// Download the files. 
        /// </summary>
        /// <param name="retry">The number of retries.</param>
        /// <returns>The async Task</returns>
        public Task Download(int retry = 1)
        {
            return Download(retry, CancellationToken.None);
        }

        /// <summary>
        /// Download the files, with cancellation. Interrupted downloads retain a .part file for resuming. The work
        /// never resumes on the caller's synchronization context (e.g. a UI thread), and the progress event is raised
        /// on a background thread.
        /// </summary>
        /// <param name="retry">The number of retries.</param>
        /// <param name="cancellationToken">Cancels the download</param>
        /// <returns>The async Task</returns>
        public async Task Download(int retry, CancellationToken cancellationToken)
        {
            await DownloadHelperMultiple(_files.ToArray(), retry, this.OnDownloadProgressChanged, cancellationToken).ConfigureAwait(false);
        }

        private static async Task DownloadHelperMultiple(
            DownloadableFile[] downloadableFiles,
            int retry,
            DownloadProgressChangedEventHandler onDownloadProgressChanged,
            CancellationToken cancellationToken)
        {
            if (downloadableFiles == null || downloadableFiles.Length == 0)
            {
                return;
            }
            else if (downloadableFiles.Length == 1)
            {
                await DownloadHelper(downloadableFiles[0], retry, onDownloadProgressChanged, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                DownloadableFile currentFile = downloadableFiles[0];
                DownloadableFile[] remainingFiles = new DownloadableFile[downloadableFiles.Length - 1];
                Array.Copy(downloadableFiles, 1, remainingFiles, 0, remainingFiles.Length);
                await DownloadHelper(currentFile, retry, onDownloadProgressChanged, cancellationToken).ConfigureAwait(false);

                await DownloadHelperMultiple(remainingFiles, retry, onDownloadProgressChanged, cancellationToken).ConfigureAwait(false);

            }
        }

        /// <summary>
        /// An http client with progress based on source code from https://stackoverflow.com/questions/20661652/progress-bar-with-httpclient with modification
        /// </summary>
        public class HttpClientWithProgress : HttpClient
        {
            //private string _downloadUrl;
            //private string _destinationFilePath;

            //private HttpClient _httpClient;


            /// <summary>
            /// Handle download download progress change.
            /// </summary>
            public event DownloadProgressChangedEventHandler DownloadProgressChanged;

            /// <summary>
            /// Download file asynchronously
            /// </summary>
            /// <param name="downloadUrl">The download url</param>
            /// <param name="destinationFilePath">The destination file path</param>
            /// <returns>The task</returns>
            public Task DownloadFileTaskAsync(string downloadUrl, string destinationFilePath)
            {
                return DownloadFileTaskAsync(downloadUrl, destinationFilePath, CancellationToken.None);
            }

            /// <summary>
            /// Download file asynchronously, with cancellation
            /// </summary>
            /// <param name="downloadUrl">The download url</param>
            /// <param name="destinationFilePath">The destination file path</param>
            /// <param name="cancellationToken">Cancels the download</param>
            /// <returns>The task</returns>
            public Task DownloadFileTaskAsync(string downloadUrl, string destinationFilePath, CancellationToken cancellationToken)
            {
                return DownloadFileTaskAsync(downloadUrl, destinationFilePath, null, cancellationToken);
            }

            internal async Task DownloadFileTaskAsync(string downloadUrl, string destinationFilePath, string sha256, CancellationToken cancellationToken)
            {
                DownloadableFile file = new DownloadableFile(downloadUrl, null, sha256) { LocalFile = destinationFilePath };
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationFilePath)));
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string[] metadata = file.ReadResumeMetadata();
                    EntityTagHeaderValue validator;
                    long offset = file.PartialDownloadBytes;
                    bool resume = offset > 0 && metadata != null
                        && EntityTagHeaderValue.TryParse(metadata[1], out validator) && !validator.IsWeak;
                    if (!resume) offset = 0;
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, downloadUrl))
                    {
                        request.Headers.AcceptEncoding.ParseAdd("identity");
                        if (resume)
                        {
                            request.Headers.Range = new RangeHeaderValue(offset, null);
                            request.Headers.IfRange = new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(metadata[1]));
                        }
                        using (HttpResponseMessage response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                        {
                            ContentRangeHeaderValue range = response.Content.Headers.ContentRange;
                            string etag = response.Headers.ETag != null && !response.Headers.ETag.IsWeak ? response.Headers.ETag.ToString() : "";
                            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && resume)
                            {
                                // A cancellation can leave all bytes present but not yet verified/published.
                                if (range != null && range.Unit == "bytes" && range.Length == offset
                                    && (sha256 != null || etag == metadata[1]))
                                {
                                    PublishDownload(file, cancellationToken);
                                    TriggerProgressChanged(offset, offset);
                                    return;
                                }
                                DiscardPartial(file);
                                throw new IOException("The server rejected the saved download range. Retry starts a fresh download.");
                            }
                            response.EnsureSuccessStatusCode();
                            foreach (string encoding in response.Content.Headers.ContentEncoding)
                                if (!String.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase))
                                    throw new IOException("Encoded download responses cannot be safely resumed.");
                            long? total = response.Content.Headers.ContentLength;
                            long? segmentLength = total;
                            if (response.StatusCode == HttpStatusCode.PartialContent)
                            {
                                long savedLength;
                                bool changedLength = resume && long.TryParse(metadata[2], out savedLength)
                                    && range != null && range.Length.HasValue && savedLength != range.Length.Value;
                                if (range == null || range.Unit != "bytes" || range.From != offset || !range.To.HasValue || range.To.Value < offset
                                    || !range.Length.HasValue || range.To.Value >= range.Length.Value || changedLength
                                    || (resume && etag != "" && etag != metadata[1])
                                    || (segmentLength.HasValue && segmentLength.Value != range.To.Value - offset + 1))
                                {
                                    DiscardPartial(file);
                                    throw new IOException("The server returned an inconsistent download range.");
                                }
                                total = range.Length;
                                segmentLength = range.To.Value - offset + 1;
                                // A compliant If-Range 206 may omit ETag; retain the strong validator we sent.
                                if (etag == "" && resume) etag = metadata[1];
                            }
                            else if (response.StatusCode == HttpStatusCode.OK)
                            {
                                // Range ignored or If-Range did not match: replace, never append, the body.
                                offset = 0;
                            }
                            else throw new IOException("Unexpected download response: " + response.StatusCode);

                            long received = offset;
                            using (FileStream output = new FileStream(file.PartialFile, offset == 0 ? FileMode.Create : FileMode.Append,
                                FileAccess.Write, FileShare.Read, 81920, true))
                            {
                                File.WriteAllLines(file.ResumeMetadataFile, new[] { file.ResumeIdentity, etag,
                                    total.HasValue ? total.Value.ToString(CultureInfo.InvariantCulture) : "" });
                                TriggerProgressChanged(total, received);
                                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                {
                                    byte[] buffer = new byte[81920];
                                    long lastProgress = received;
                                    int count;
                                    while ((count = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) != 0)
                                    {
                                        if (segmentLength.HasValue && received - offset + count > segmentLength.Value)
                                        {
                                            // Do not retain bytes outside the response's declared range.
                                            throw new IOException("The server sent more data than the declared download range.");
                                        }
                                        await output.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
                                        received += count;
                                        if (received - lastProgress >= 819200)
                                        {
                                            TriggerProgressChanged(total, received);
                                            lastProgress = received;
                                        }
                                    }
                                }
                            }
                            TriggerProgressChanged(total, received);
                            cancellationToken.ThrowIfCancellationRequested();
                            if (segmentLength.HasValue && received - offset != segmentLength.Value)
                                throw new IOException("The download ended before the declared response length.");
                            if (total.HasValue && received < total.Value)
                            {
                                if (etag == "") throw new IOException("An incomplete response has no strong ETag for resuming.");
                                continue; // The server may send a bounded subset of the requested range.
                            }
                            PublishDownload(file, cancellationToken);
                            return;
                        }
                    }
                }
            }

            private static void PublishDownload(DownloadableFile file, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DownloadableFile candidate = new DownloadableFile(file.Url, null, file.Sha256Hash) { LocalFile = file.PartialFile };
                if (!candidate.IsLocalFileValid)
                {
                    DiscardPartial(file);
                    throw new InvalidDataException("The completed download did not match its SHA-256 checksum.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                // The old API replaced an existing destination too; never expose an incomplete replacement.
                if (File.Exists(file.LocalFile)) File.Delete(file.LocalFile);
                File.Move(file.PartialFile, file.LocalFile);
                File.Delete(file.LocalFile + ".sha256ok");
                if (File.Exists(file.PartialFile + ".sha256ok"))
                    File.Move(file.PartialFile + ".sha256ok", file.LocalFile + ".sha256ok");
                File.Delete(file.ResumeMetadataFile);
            }

            private static void DiscardPartial(DownloadableFile file)
            {
                File.Delete(file.PartialFile);
                File.Delete(file.PartialFile + ".sha256ok");
                File.Delete(file.ResumeMetadataFile);
            }

            private void TriggerProgressChanged(long? totalDownloadSize, long totalBytesRead)
            {
                if (DownloadProgressChanged == null)
                    return;

                double? progressPercentage = null;
                if (totalDownloadSize.HasValue)
                    progressPercentage = Math.Round((double)totalBytesRead / totalDownloadSize.Value * 100, 2);

                DownloadProgressChanged(totalDownloadSize, totalBytesRead, progressPercentage);
            }

        }

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> DownloadLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

        private static async Task DownloadHelper(
            DownloadableFile downloadableFile,
            int retry,
            DownloadProgressChangedEventHandler onDownloadProgressChanged,
            CancellationToken cancellationToken)
        {
            if (downloadableFile.Url == null) return;
            SemaphoreSlim gate = DownloadLocks.GetOrAdd(Path.GetFullPath(downloadableFile.LocalFile), _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (downloadableFile.IsLocalFileValid) return;
                // A legacy partial/corrupt final file must not look downloaded in the UI.
                File.Delete(downloadableFile.LocalFile);
                File.Delete(downloadableFile.LocalFile + ".sha256ok");
                for (int attempt = 0; ; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using (HttpClientWithProgress client = new HttpClientWithProgress())
                        {
                            if (onDownloadProgressChanged != null) client.DownloadProgressChanged += onDownloadProgressChanged;
                            await client.DownloadFileTaskAsync(downloadableFile.Url, downloadableFile.LocalFile,
                                downloadableFile.Sha256Hash, cancellationToken).ConfigureAwait(false);
                        }
                        return;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception) when (attempt < retry) { /* Retain bytes and retry with Range/If-Range. */ }
                }
            }
            finally { gate.Release(); }
        }

#endif
    }

}
