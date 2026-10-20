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

## Sound monitors in the chat (TODO item 42)

"Tell me when the doorbell rings", "beep if the dog barks", "say 'someone is at the door' when you hear a knock", "alert me if a baby cries for
more than 10 seconds". The agent's `create_sound_monitor` tool adds a monitor; while any is enabled the chat page listens through the microphone and
scores the audio with YAMNet (the model runs once per window for all monitors), and no language model runs per sound.

- **Library** (`Emgu.LiteRT.LM.Agents`, namespace `Audio`, `SoundMonitors.cs`, pure managed): `SoundGroups` (30 sounds, each a group of YAMNet
  classes with the words people use for it - doorbell, knock, phone, smoke_alarm, alarm_clock, alarm, siren, dog, cat, baby_cry, speech, laughter,
  shouting, cough, sneeze, snoring, clapping, glass_breaking, running_water, microwave, car_horn, music, singing, footsteps, typing, thunder, rain,
  bird, vacuum, whistle - or any exact YAMNet class name; the longest matching phrase wins, so "smoke detector" is not just "alarm"),
  `SoundPresenceMonitor` (the temporal state machine), `SoundMonitorRuntime` (up to 8 session-only monitors, duplicates suppressed, alerts on a worker
  of their own, one at a time) and `SoundMonitorTools` (`create_sound_monitor`, `list_sound_monitors`, `control_sound_monitor`).
- **Semantics**: a window scores a group by its best class. The sound starts when the score is at or above the threshold (0.5 by default) for
  `minimumSeconds` (0 = the first window; a run goes on across gaps of up to 2 s, since a dog doesn't bark in every window), and ends - arming the monitor
  again - after 3 s without it, so a doorbell rung three times in a row is one alert. A listening gap over 5 s drops the episode without claiming anything.
- **Alerts**: the monitor's card shows when it heard the sound (always), plus optionally a chime (`playSound`) and fixed spoken words (`speakText`, at
  most 200 characters), the same chime and speech as the camera monitors. The page doesn't listen while its own alert audio plays and for 2 s after, or
  it would hear itself.
- **Host** (`LiteRtLmChatPage.Sound.cs`): a "sound_monitor" tool group (keywords: sound, hear, listen, doorbell, knock, bark, siren, smoke alarm, baby cry...),
  a "Sound monitors · session only" card with Pause / Resume / Remove, the microphone started and stopped to match the enabled monitors, and one
  microphone user at a time: `VoiceRecorder` goes through `MicrophoneGate`, so listening is released before a voice message or the voice chat records
  and taken back afterwards. New chat removes the monitors. Android, iOS and Mac Catalyst; not Windows (no microphone stream yet).
- **Model behaviour** (Gemma 4 E2B on Mac Catalyst, real model, the microphone replaced by a clip): every request made one `create_sound_monitor` call
  with the right sound - talking, a baby crying for 10 s, the doorbell with a chime, a dog with no alert, a knock with "someone is at the door", the smoke
  alarm with a chime - and "remove all the sound monitors" listed and removed them. Before the parameters were optional with "leave out unless..." texts,
  it made up spoken words nobody asked for and added chimes to "tell me"; with them it didn't. It sometimes still leaves out words the user did ask it to
  say ("say the words I hear voices"): a small model's limit.
- **Checked on devices**: Mac Catalyst - the clip's speech was heard at 98% and the alert ran, repeatedly across prompts; Galaxy S10 - a speech monitor
  with the real microphone heard three separate episodes of the Mac's `say` (50-67%), each running its chime. Tests: `UnitTestSoundMonitors` (groups,
  state machine, runtime, tools: 18 cases), `UnitTestYamNetNative` (every group's classes exist in the model; a monitor hears the speech sample and not
  silence), routing cases in `UnitTestToolRouting`.

## What is left

- Sound monitors in the live-camera agent's own panel and combined conditions ("a dog barks and a person is in view"), and a camera snapshot as an alert.
- The Windows microphone source, and listening with the app in the background (an Android foreground service; iOS background audio).
- A threshold per sound: the defaults are untuned - 0.5 for speech was fine, a smoke alarm or a baby's cry needs real recordings to tune.
- Run together with the camera loop and Gemma on a phone for a while (heat, battery): the models are small but nothing was timed together.
- The model licence, as above.
