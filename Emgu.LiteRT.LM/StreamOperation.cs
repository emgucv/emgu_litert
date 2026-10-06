//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// The state of one streaming call (litert_lm_session_generate_content_stream, litert_lm_session_run_decode_async
    /// or litert_lm_conversation_send_message_stream). LiteRT-LM calls the stream callback on a background thread,
    /// once per chunk and then once more with a final chunk; this turns that into a Task.
    /// </summary>
    internal class StreamOperation
    {
        // The final chunk's error message for a cancelled stream, and for a stream that stopped because the engine's
        // maximum number of tokens was reached (see CreateCallback in LiteRT-LM's c/engine.cc).
        private const String CancelledMessage = "CANCELLED.";
        private const String MaxNumTokensReachedMessage = "Max number of tokens reached.";

        // A single delegate instance for every stream, kept alive by this static field so the garbage collector can't
        // free it while native code still holds the function pointer.
        private static readonly LiteRtLmInvoke.LiteRtLmStreamCallback NativeCallback = OnNativeChunk;

        private readonly TaskCompletionSource<String[]> _completion =
            new TaskCompletionSource<String[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<String> _chunks = new List<String>();
        private readonly Action<String> _onChunk;
        private readonly Action _cancel;
        private readonly CancellationToken _cancellationToken;
        // The session or conversation the stream runs on, kept alive until the stream completes.
        private readonly object _owner;
        private GCHandle _handle;
        private CancellationTokenRegistration _registration;
        private int _completed;
        // The exception thrown by the onChunk callback, if any. Only accessed on LiteRT-LM's callback thread.
        private Exception _callbackException;

        private StreamOperation(object owner, Action<String> onChunk, Action cancel, CancellationToken cancellationToken)
        {
            _owner = owner;
            _onChunk = onChunk;
            _cancel = cancel;
            _cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Start a streaming call.
        /// </summary>
        /// <param name="owner">The session or conversation the stream runs on</param>
        /// <param name="start">Calls the native streaming function with the callback and the callback data, and returns
        /// its status</param>
        /// <param name="functionName">The name of the native streaming function, for error messages</param>
        /// <param name="onChunk">Called for each chunk, on a LiteRT-LM background thread; may be null</param>
        /// <param name="cancel">Cancels the native operation</param>
        /// <param name="cancellationToken">Cancels the stream when triggered</param>
        /// <returns>A task that completes with the chunks when the final chunk arrives</returns>
        internal static Task<String[]> Start(
            object owner,
            Func<LiteRtLmInvoke.LiteRtLmStreamCallback, IntPtr, int> start,
            String functionName,
            Action<String> onChunk,
            Action cancel,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StreamOperation operation = new StreamOperation(owner, onChunk, cancel, cancellationToken);
            operation._handle = GCHandle.Alloc(operation);
            int status;
            try
            {
                status = start(NativeCallback, GCHandle.ToIntPtr(operation._handle));
            }
            catch
            {
                operation._handle.Free();
                throw;
            }
            if (status != 0)
            {
                // The stream didn't start, so the callback will never be called.
                if (Interlocked.Exchange(ref operation._completed, 1) == 0)
                    operation._handle.Free();
                LiteRtLmInvoke.CheckStatus(status, functionName);
            }
            if (cancellationToken.CanBeCanceled)
                operation._registration = cancellationToken.Register(operation.RequestCancel);
            return operation._completion.Task;
        }

        private void RequestCancel()
        {
            if (Volatile.Read(ref _completed) == 0)
                CancelNative();
        }

        // Cancel the native operation from a thread pool thread: the cancellation may be requested from inside a stream
        // callback (e.g. the user's onChunk cancelling its CancellationTokenSource), which runs on LiteRT-LM's own
        // thread, and LiteRT-LM's cancel takes locks its execution manager may hold while calling back.
        private void CancelNative()
        {
            Task.Run(() =>
            {
                try
                {
                    if (Volatile.Read(ref _completed) == 0)
                        _cancel();
                }
                catch
                {
                }
            });
        }

#if __IOS__
        [ObjCRuntime.MonoPInvokeCallback(typeof(LiteRtLmInvoke.LiteRtLmStreamCallback))]
#elif UNITY_ANDROID || UNITY_IOS || UNITY_EDITOR || UNITY_STANDALONE
        [AOT.MonoPInvokeCallback(typeof(LiteRtLmInvoke.LiteRtLmStreamCallback))]
#endif
        private static void OnNativeChunk(IntPtr callbackData, IntPtr chunk)
        {
            // An exception must never propagate back into LiteRT-LM's native frames.
            try
            {
                StreamOperation operation = GCHandle.FromIntPtr(callbackData).Target as StreamOperation;
                if (operation != null)
                    operation.HandleChunk(chunk);
            }
            catch
            {
            }
        }

        private void HandleChunk(IntPtr chunk)
        {
            if (Volatile.Read(ref _completed) != 0)
                return;

            if (!LiteRtLmInvoke.litert_lm_stream_chunk_is_final(chunk))
            {
                String text = LiteRtLmInvoke.PtrToStringUtf8(LiteRtLmInvoke.litert_lm_stream_chunk_get_text(chunk));
                if (text == null)
                    return;
                lock (_chunks)
                    _chunks.Add(text);
                if (_onChunk != null && _callbackException == null)
                {
                    try
                    {
                        _onChunk(text);
                    }
                    catch (Exception e)
                    {
                        // Stop generating; the task faults with the callback's exception once LiteRT-LM delivers the
                        // final chunk, so the session is idle by the time the caller sees the task complete.
                        _callbackException = e;
                        CancelNative();
                    }
                }
                return;
            }

            String error = LiteRtLmInvoke.PtrToStringUtf8(LiteRtLmInvoke.litert_lm_stream_chunk_get_error(chunk));
            String[] chunks;
            lock (_chunks)
                chunks = _chunks.ToArray();
            Exception callbackException = _callbackException;
            if (callbackException != null)
                Complete(() => _completion.TrySetException(callbackException));
            else if (error == null || error == MaxNumTokensReachedMessage)
                Complete(() => _completion.TrySetResult(chunks));
            else if (error == CancelledMessage)
                Complete(() => _completion.TrySetCanceled(_cancellationToken));
            else
                Complete(() => _completion.TrySetException(new LiteRtLmException(error)));
        }

        private void Complete(Action setResult)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
                return;
            _registration.Dispose();
            _handle.Free();
            setResult();
            GC.KeepAlive(_owner);
        }
    }

    public static partial class LiteRtLmInvoke
    {
        /// <summary>
        /// The stream callback (LiteRtLmStreamCallback)
        /// </summary>
        /// <param name="callbackData">The callback data passed to the streaming function</param>
        /// <param name="chunk">The chunk (LiteRtLmStreamChunk*), valid only during the call</param>
        [UnmanagedFunctionPointer(LiteRtLmCallingConvention)]
        internal delegate void LiteRtLmStreamCallback(IntPtr callbackData, IntPtr chunk);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_stream_chunk_get_text(IntPtr chunk);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        [return: MarshalAs(BoolMarshalType)]
        internal static extern bool litert_lm_stream_chunk_is_final(IntPtr chunk);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_stream_chunk_get_error(IntPtr chunk);
    }
}
