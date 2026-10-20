# YAMNet sound events (a "Sound events" page in the demo)

Prototype of audio event detection for the visual agents ("tell me when the doorbell rings"). The classes are `Emgu.LiteRT.Models/YamNet.cs`
(the model, WAV reading, resampling) and, in the demo, `YamNetDetector.cs`, `SoundSource.cs` and `SoundEventsPage.cs`. Tests are
`UnitTestYamNet` (WAV and resampling, model-free) and `UnitTestYamNetNative` (the model against Python, `yamnet_reference.json`).

## Provenance and licence

- File: Google's MediaPipe copy of YAMNet, `https://storage.googleapis.com/mediapipe-models/audio_classifier/yamnet/float32/1/yamnet.tflite`,
  4,126,810 bytes, SHA-256 `4D8B4A53282DC83EF04E3E7DBC4FBC98082E34E44ED798E16C3A0CDD4C584FAF`. It is downloaded from Google and checked, not copied to
  Emgu's S3.
- Licence: the model's own metadata says "Apache License. Version 2.0". The model cards for MediaPipe's hand models say nothing about a licence, so
  this one is better documented, but it is still only the metadata: Google has no separate licence file beside the model. YAMNet's code and
  training are in `tensorflow/models` (Apache-2.0). It was trained on AudioSet, whose class names come from the AudioSet ontology (CC BY-SA 4.0); the
  licences page credits both.

## Model contract

- Input: float32 `[15600]`, mono samples at 16 kHz in -1..1 (0.975 s). The log-mel spectrogram is inside the graph, so there is no preprocessing.
- Output: float32 `[1, 521]`, one probability per AudioSet class (sigmoid; they don't sum to 1 and overlap: a barking dog scores on "Dog" and "Bark").
  The scores come out in coarse steps (0.80, 0.83, 0.85...), so compare with a tolerance of a few hundredths.
- Labels: a `yamnet_label_list.txt` zip appended to the flatbuffer (`YamNet.ReadLabels` reads it).
- The shapes are static, so `CompiledModel` runs it as it is (no patching, unlike the hand classifier).
- Streaming: a window every 7,800 samples (half a window, as YAMNet's own demo) over the last 15,600. `YamNet.Resample` is a linear interpolation, the
  one `UnitTestYamNet` checks; the demo's microphone source on Apple platforms averages (`BoxResampler`) because a microphone runs at 44.1 or 48 kHz and
  plain interpolation would fold frequencies above 8 kHz into the band.

## Parity

The reference is a Python script (not kept in the repo) using the same file through the TFLite interpreter (ai-edge-litert) on four signals - silence, a 440 Hz
sine, white noise from a fixed generator (an LCG, defined in both languages) and the speech sample resampled to 16 kHz - and every 7,800-sample window of
that speech. C# gives the same top class with the score within 0.02 and the top five within 0.03. Python's results: silence -> Silence 0.80, sine ->
Sine wave 1.00, noise -> Static 0.67 / White noise 0.41, speech -> Speech 0.96.

On real sounds (macOS's alert sounds and `say`, checked in Python, not committed): speech -> Speech 1.00 (and Speech synthesizer), Glass -> Ding, Submarine
-> Sonar; the plain beeps (Basso, Funk) came out as Speech/Silence/Music, so don't expect every short synthetic sound to find its class.

## Timings (the page, CPU, 2 threads)

| Device | One window |
|---|---|
| Mac (M1, Mac Catalyst) | 3-5 ms |
| Galaxy S10 | 7-17 ms |

A window is scored twice a second, so the model uses well under 5% of one core.

## The page

"Sound events" in the TF Lite tab: Listen (microphone), "Speech sample" (LiteRT-LM's `audio_sample.wav`) and "Tone and noise sample" (2 s silence, a tone,
noise, silence) play through the same path at their own speed. It shows the best five sounds, the time per window and the level, and logs a line when
the best sound changes and scores at least 0.4. Microphone sources: `AudioRecord` at 16 kHz on Android, an `AVAudioEngine` input tap on iOS and Mac
Catalyst. Not on Windows.

Checked: on Mac Catalyst the tone sample gave Silence -> Sine wave 100% -> Static/Noise -> Pink noise/White noise -> Silence and the speech sample Speech
96-100%; on a Galaxy S10, the microphone heard `say` and a quiet room from the Mac next to it (Speech 85-89% while it spoke, Silence otherwise). The Apple
microphone path was not run (no permission prompt without the user); it is built.

## What is left for a visual agent

- Sound monitors in the live-camera agent: a class group (Dog + Bark + Growling; Doorbell + Ding-dong + Knock) with a threshold and a minimum duration, as
  the scores overlap.
- One microphone user at a time: a monitor and the voice message button / voice chat can't both record, so they must share the stream or pause it.
- Run together with the camera: the models are small, but phones heat up, and Gemma generating at the same time slows everything.
- Foreground service on Android for listening with the app in the background.
- The model licence, as above.
