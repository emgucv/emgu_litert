// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Agents.Audio;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

// Reads replies aloud through the camera monitors' AlertAudioCoordinator (so a reply and an alert don't talk over each
// other), for Emgu.LiteRT.LM.Maui's ChatView (IReplySpeaker): the reply is read while it is written, sentence by sentence.
// The voice chat (VoiceModeFeature) follows the text being spoken (Speaking) and waits for the whole reply to have been
// read (Completion).
internal sealed class AlertAudioReplySpeaker : IReplySpeaker
{
    private readonly Func<string, CancellationToken, Task> _speak;
    private ReplyReader _reader;

    public AlertAudioReplySpeaker(Func<string, CancellationToken, Task> speak)
    {
        _speak = speak;
    }

    public AlertAudioReplySpeaker(AlertAudioCoordinator audio) : this(audio.SpeakReplyAsync)
    {
    }

    // The text about to be spoken.
    public event Action<string> Speaking;

    // Completes when the reply being read has been read, or reading was stopped; completed when nothing is read.
    public Task Completion => _reader?.Completion ?? Task.CompletedTask;

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

        public event Action<string> Speaking;

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
