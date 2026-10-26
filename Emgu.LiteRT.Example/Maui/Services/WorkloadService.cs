// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

// When heavy background work may run (WorkloadPolicy: not while the device is too hot or its battery too low), the
// "Index photos only while charging" setting, and one embedding at a time on the shared search model.
internal sealed class WorkloadService
{
    private readonly WorkloadPolicy _policy = new();
    private readonly Func<SearchEmbedder> _embedder;
    private volatile bool _photosOnlyWhileCharging = AppSettings.Chat.PhotosOnlyWhileCharging;

    public WorkloadService(Func<SearchEmbedder> embedder)
    {
        _embedder = embedder;
    }

    public bool PhotosOnlyWhileCharging => _photosOnlyWhileCharging;

    // Waits while the device is too hot or its battery too low for heavy work, telling status why (null when it goes on).
    public Task WaitForWorkAsync(bool photos, Action<string> status, CancellationToken cancel) =>
        _policy.WaitAsync(DeviceWorkload.ReadAsync, () => photos && _photosOnlyWhileCharging, status, cancel);

    // One embedding for a background index: waits while the device is too busy, then embeds holding the model's gate -
    // without holding it while waiting, so a paused document or chat index doesn't block an interactive photo search.
    public async Task<T> RunIndexEmbeddingAsync<T>(Func<T> embed, Action<string> paused, CancellationToken cancel, bool photos = false)
    {
        SearchEmbedder embedder = _embedder();
        while (true)
        {
            await WaitForWorkAsync(photos, paused, cancel);
            await embedder.Gate.WaitAsync(cancel);
            try
            {
                if (_policy.PauseReason(await DeviceWorkload.ReadAsync(), photos && _photosOnlyWhileCharging) != null) continue;
                return await Task.Run(embed, cancel);
            }
            finally { embedder.Gate.Release(); }
        }
    }

    // The setting's row (phones only: a desktop is always plugged in).
    public View BuildPhotoChargingSetting()
    {
        var toggle = new Switch { IsToggled = _photosOnlyWhileCharging, OnColor = Theme.Accent };
        toggle.Toggled += (_, e) =>
        {
            _photosOnlyWhileCharging = e.Value;
            AppSettings.Chat.PhotosOnlyWhileCharging = e.Value;
        };
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        row.Add(new Label { Text = "Index photos only while charging", FontSize = 13, TextColor = Theme.PrimaryText, VerticalOptions = LayoutOptions.Center }, 0, 0);
        row.Add(toggle, 1, 0);
        row.IsVisible = DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS;
        return row;
    }
}
