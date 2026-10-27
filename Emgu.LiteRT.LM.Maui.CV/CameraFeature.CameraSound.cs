// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID
using Emgu.LiteRT.LM.Agents.Audio;
using Emgu.LiteRT.LM.Maui;

namespace Emgu.LiteRT.LM.Maui.CV;

// A short shutter click when the camera takes a full-resolution photo (TODO item 50), so a photo taken without anyone touching the phone - by a sound or
// by something coming into view - doesn't go unnoticed. Settings > "Camera sounds" (on by default, Preferences litert_lm_chat_camera_sound) turns it off,
// for a baby's room. Clips stay silent: the preview says "Recording clip", and a sound at the start would be in the clip. The sound monitors don't
// listen while it plays (the alert mute), so the click can't be heard as a sound.
public sealed partial class CameraFeature
{
    private readonly CameraChimeAction _cameraClick = new CameraChimeAction("camera_click.wav");

    /// <summary>The "camera sounds" settings key.</summary>
    public const string CameraSoundKey = "camera_sound";

    private bool CameraSoundOn => Host?.Settings.Get(CameraSoundKey, true) ?? true;

    private View BuildCameraSoundSection()
    {
        var toggle = new Switch { IsToggled = CameraSoundOn, OnColor = ChatResources.FindColor("ChatAccent"), VerticalOptions = LayoutOptions.Center };
        toggle.Toggled += (_, e) => Host.Settings.Set(CameraSoundKey, e.Value);
        SemanticProperties.SetDescription(toggle, "Camera sounds");
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                SwitchRow("Camera sounds", toggle),
                Note("A short click when the camera takes a photo for an alert, so nobody is photographed without a sign. Turn it off where a sound would be a nuisance, such as a baby's room. Clips are always silent.")
            }
        };
    }

    // Starts the click and returns a task that ends when it has played (or failed: a click is never a reason to lose the photo).
    private Task PlayCameraClickAsync(CancellationToken token)
    {
        if (!CameraSoundOn) return Task.CompletedTask;
        BeginSoundMute();
        return Task.Run(async () =>
        {
            try { await _cameraClick.ExecuteAsync(null, token); }
            catch (Exception) { }
            finally { EndSoundMute(); }
        });
    }
}
#endif
