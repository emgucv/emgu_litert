//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Shares the microphone between an app's always-on listener (e.g. a sound monitor) and recordings that need it now
    /// (<see cref="VoiceRecorder"/>: voice messages, a voice chat). A recording enters the gate before it starts and exits
    /// it when it stops; the first to enter makes the listener let go (<see cref="Pausing"/>), and the last to leave gives
    /// the microphone back (<see cref="Released"/>). Other code that records should go through it too.
    /// </summary>
    public static class MicrophoneGate
    {
        private static int _holders;

        /// <summary>Called when the first recording enters: the app's listener lets go of the microphone; or null.</summary>
        public static Func<Task> Pausing { get; set; }

        /// <summary>Called when the last recording has left: the app's listener may take the microphone again; or null.</summary>
        public static Action Released { get; set; }

        /// <summary>Take the microphone, after the app's listener has let go of it.</summary>
        public static async Task EnterAsync()
        {
            if (Interlocked.Increment(ref _holders) == 1)
            {
                Func<Task> pausing = Pausing;
                if (pausing != null) await pausing();
            }
        }

        /// <summary>Give the microphone back.</summary>
        public static void Exit()
        {
            if (Interlocked.Decrement(ref _holders) == 0) Released?.Invoke();
        }
    }
}
