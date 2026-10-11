//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Maui.Demo.Lite
{
    /// <summary>
    /// Debug builds only: logs when the UI thread stops responding for more than a second (Android shows "isn't
    /// responding" after 5), with what the app was doing - the breadcrumbs pages leave with Mark - and, on Android,
    /// the main thread's Java stack. Written to the debug output / logcat (tag "UISTALL") and appended to
    /// ui_stalls.log in the app's data folder, which survives the log being cleared
    /// (adb shell run-as com.emgu.tf.lite.maui.demo cat files/ui_stalls.log).
    /// </summary>
    internal static class UiStallWatchdog
    {
        private const int PingIntervalMs = 250;
        private const int StallThresholdMs = 1000;
        private const int MaxBreadcrumbs = 20;

        private static readonly object Sync = new object();
        private static readonly Queue<string> Breadcrumbs = new Queue<string>();
        private static long _lastResponseTicks;
        private static bool _pingPending;
        private static string _logPath;

        /// <summary>
        /// Record what the app is doing (kept in a short history and reported with a stall). No-op in Release builds.
        /// </summary>
        [Conditional("DEBUG")]
        public static void Mark(string activity)
        {
            lock (Sync)
            {
                Breadcrumbs.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + " " + activity);
                while (Breadcrumbs.Count > MaxBreadcrumbs)
                    Breadcrumbs.Dequeue();
            }
        }

        /// <summary>
        /// Start watching the UI thread. No-op in Release builds.
        /// </summary>
        [Conditional("DEBUG")]
        public static void Start()
        {
            try
            {
                _logPath = Path.Combine(FileSystem.AppDataDirectory, "ui_stalls.log");
            }
            catch (Exception)
            {
                _logPath = null;
            }
            Interlocked.Exchange(ref _lastResponseTicks, Stopwatch.GetTimestamp());
            Thread thread = new Thread(Run) { IsBackground = true, Name = "UiStallWatchdog" };
            thread.Start();
        }

        private static void Run()
        {
            bool stalled = false;
            long pingSent = 0;
            while (true)
            {
                Thread.Sleep(PingIntervalMs);
                if (Volatile.Read(ref _pingPending))
                {
                    // The last ping hasn't run yet: the UI thread is busy.
                    long waitedMs = ElapsedMs(pingSent, Stopwatch.GetTimestamp());
                    if (waitedMs >= StallThresholdMs && !stalled)
                    {
                        stalled = true;
                        Report(string.Format("UI thread not responding for {0} ms", waitedMs), true);
                    }
                    continue;
                }
                if (stalled)
                {
                    stalled = false;
                    Report(string.Format("UI thread responding again after {0} ms",
                        ElapsedMs(pingSent, Interlocked.Read(ref _lastResponseTicks))), false);
                }
                // Send the next ping.
                Volatile.Write(ref _pingPending, true);
                pingSent = Stopwatch.GetTimestamp();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Interlocked.Exchange(ref _lastResponseTicks, Stopwatch.GetTimestamp());
                    Volatile.Write(ref _pingPending, false);
                });
            }
        }

        private static long ElapsedMs(long from, long to) => (long)((to - from) * 1000.0 / Stopwatch.Frequency);

        private static void Report(string headline, bool withDetails)
        {
            StringBuilder report = new StringBuilder();
            report.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(" UISTALL ").AppendLine(headline);
            if (withDetails)
            {
                report.AppendLine("  Recent activity:");
                lock (Sync)
                {
                    foreach (string crumb in Breadcrumbs)
                        report.Append("    ").AppendLine(crumb);
                }
#if ANDROID
                try
                {
                    // The main thread's Java frames; .NET code shows as the n_* callbacks MAUI was dispatching.
                    report.AppendLine("  Main thread (Java):");
                    foreach (Java.Lang.StackTraceElement frame in Android.OS.Looper.MainLooper.Thread.GetStackTrace())
                        report.Append("    at ").AppendLine(frame.ToString());
                }
                catch (Exception e)
                {
                    report.Append("  (no main thread stack: ").Append(e.Message).AppendLine(")");
                }
#endif
            }
            string text = report.ToString();
            foreach (string line in text.Split('\n'))
            {
#if ANDROID
                Android.Util.Log.Warn("UISTALL", line.TrimEnd('\r'));
#else
                Debug.WriteLine(line.TrimEnd('\r'));
#endif
            }
            if (_logPath != null)
            {
                try
                {
                    File.AppendAllText(_logPath, text);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
