// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Agents.Audio;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui;

namespace Emgu.LiteRT.LM.Maui.CV;

/// <summary>
/// Reads replies aloud through the alert audio (<see cref="AlertAudioCoordinator"/>), so a reply doesn't talk over a
/// monitor's spoken alert, sentence by sentence as the reply streams; the voice chat follows <see cref="Speaking"/> and
/// waits for <see cref="Completion"/>.
/// </summary>
public sealed class AlertAudioReplySpeaker : IReplySpeaker
{
    private readonly Func<string, CancellationToken, Task> _speak;
    private ReplyReader _reader;

    /// <summary>Create a speaker over a function that speaks a sentence.</summary>
    public AlertAudioReplySpeaker(Func<string, CancellationToken, Task> speak)
    {
        _speak = speak;
    }

    /// <summary>Create a speaker over the alert audio, so replies wait for an alert to finish.</summary>
    public AlertAudioReplySpeaker(AlertAudioCoordinator audio) : this(audio.SpeakReplyAsync)
    {
    }

    // The text about to be spoken.
    /// <summary>Raised with each sentence as it is spoken (the voice chat follows it).</summary>
    public event Action<string> Speaking;

    // Completes when the reply being read has been read, or reading was stopped; completed when nothing is read.
    /// <summary>Ends when the reply has been read out, or was stopped.</summary>
    public Task Completion => _reader?.Completion ?? Task.CompletedTask;

    /// <summary>Read a reply aloud as it streams.</summary>
    public void Start(ChatMessageItem reply)
    {
        Stop();
        if (reply == null)
            return;
        ReplyReader reader = new ReplyReader(_speak);
        reader.Speaking += text => Speaking?.Invoke(text);
        _reader = reader;
        reader.Follow(reply);
    }

    /// <summary>Stop reading.</summary>
    public void Stop()
    {
        _reader?.Stop();
        _reader = null;
    }

    // Reads one reply while it is being written: SpeechSegmenter cuts it into sentences and lines (no thinking - only
    // the answer's text is given to it - code or tables), which are spoken in order, everything that arrived during one
    // piece together as the next. Used on the UI thread only.
    private sealed class ReplyReader
    {
        private readonly SpeechSegmenter _segmenter = new SpeechSegmenter();
        private readonly List<string> _queue = new List<string>();
        private readonly Func<string, CancellationToken, Task> _speak;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private ChatMessageItem _reply;
        private bool _speaking, _finished;

        public ReplyReader(Func<string, CancellationToken, Task> speak)
        {
            _speak = speak;
        }

        /// <summary>Raised with each sentence as it is spoken (the voice chat follows it).</summary>
        public event Action<string> Speaking;

        /// <summary>Ends when the reply has been read out, or was stopped.</summary>
        public Task Completion => _done.Task;

        public void Follow(ChatMessageItem reply)
        {
            _reply = reply;
            reply.PropertyChanged += OnReplyChanged;
            Read();
        }

        private void OnReplyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatMessageItem.Text) || e.PropertyName == nameof(ChatMessageItem.State))
                Read();
        }

        private void Read()
        {
            if (_stop.IsCancellationRequested)
                return;
            switch (_reply.State)
            {
                case ChatMessageState.Streaming:
                    _queue.AddRange(_segmenter.Add(_reply.Text));
                    break;
                case ChatMessageState.Done:
                    _queue.AddRange(_segmenter.Finish(_reply.Text));
                    _reply.PropertyChanged -= OnReplyChanged;
                    _finished = true;
                    break;
                default:
                    // Interrupted or failed: no more.
                    Stop();
                    return;
            }
            Pump();
        }

        /// <summary>Stop reading.</summary>
        public void Stop()
        {
            if (_reply != null)
                _reply.PropertyChanged -= OnReplyChanged;
            _stop.Cancel();
            _queue.Clear();
            _finished = true;
            if (!_speaking)
                _done.TrySetResult(true);
        }

        private async void Pump()
        {
            if (_speaking)
                return;
            _speaking = true;
            try
            {
                while (_queue.Count > 0 && !_stop.IsCancellationRequested)
                {
                    string text = string.Join(" ", _queue);
                    _queue.Clear();
                    Speaking?.Invoke(text);
                    try
                    {
                        await _speak(text, _stop.Token);
                    }
                    catch (Exception)
                    {
                        // Stopped, or no speech engine.
                    }
                }
            }
            finally
            {
                _speaking = false;
            }
            if (_finished)
                _done.TrySetResult(true);
        }
    }
}
