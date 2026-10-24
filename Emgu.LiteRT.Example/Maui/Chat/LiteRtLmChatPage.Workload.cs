namespace Maui.Demo.Lite;

public partial class LiteRtLmChatPage
{
    private readonly WorkloadPolicy _workloadPolicy = new();
    private volatile bool _photosOnlyWhileCharging;

    private Task WaitForWorkAsync(bool photos, Action<string> status, CancellationToken cancel) =>
        _workloadPolicy.WaitAsync(DeviceWorkload.ReadAsync, () => photos && _photosOnlyWhileCharging, status, cancel);

    private View BuildPhotoChargingSetting()
    {
        _photosOnlyWhileCharging = AppSettings.Chat.PhotosOnlyWhileCharging;
        var toggle = new Switch { IsToggled = _photosOnlyWhileCharging, OnColor = Theme.Accent };
        toggle.Toggled += (_, e) =>
        {
            _photosOnlyWhileCharging = e.Value;
            AppSettings.Chat.PhotosOnlyWhileCharging = e.Value;
        };
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        row.Add(new Label { Text = "Index photos only while charging", FontSize = 13, TextColor = Theme.PrimaryText,
            VerticalOptions = LayoutOptions.Center }, 0, 0);
        row.Add(toggle, 1, 0);
        row.IsVisible = DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS;
        return row;
    }

    // Wait without holding the shared model so a paused document/chat does not block interactive photo search.
    private async Task<T> RunIndexEmbeddingAsync<T>(Func<T> embed, Action<string> paused, CancellationToken cancel, bool photos = false)
    {
        while (true)
        {
            await WaitForWorkAsync(photos, paused, cancel);
            await Embedder.Gate.WaitAsync(cancel);
            try
            {
                if (_workloadPolicy.PauseReason(await DeviceWorkload.ReadAsync(), photos && _photosOnlyWhileCharging) != null) continue;
                return await Task.Run(embed, cancel);
            }
            finally { Embedder.Gate.Release(); }
        }
    }
}
