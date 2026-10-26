// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

// The settings sections of the features that still take the old IChatHost (goals, photo search, past-chat search, image
// generation), and their background indexing: started when the page appears or the app comes back, so photos and chats
// added meanwhile are indexed. Their tools are DemoToolsFeature's groups.
internal sealed class DemoSectionsFeature : DemoFeature
{
    private bool _started;

    public override string Name => "demo_sections";

    public override string Title => "Goals, photo search, past chats, image generation";

    public override bool PersistEnabled => false;

    private StorageFeature Storage => Host.Services.Get<StorageFeature>();

    private ModelSettingsFeature ModelSettings => Host.Services.Get<ModelSettingsFeature>();

    public override View BuildSettingsSection()
    {
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                Demo.PhotoSearch.BuildSection(Storage.CreateSearchModelNotice(), Demo.Workload.BuildPhotoChargingSetting()),
                Theme.Divider(),
                Demo.PastChats.BuildSection(Storage.CreateSearchModelNotice()),
                Theme.Divider(),
                Demo.Images.BuildSection(ModelSettings.BuildImageModelSelector()),
                Theme.Divider(),
                Demo.Goals.BuildSection()
            }
        };
    }

    public override void OnAppearing()
    {
        base.OnAppearing();
        Demo.PastChats.StartIndexing();
        if (_started)
        {
            Demo.PhotoSearch.RefreshIndex();
            return;
        }
        _started = true;
        // Photo search: index photos added since the app last ran.
        Demo.PhotoSearch.StartIndexing();
        // Saved document indexes no chat uses (attached, then the chat was never saved).
        _ = Demo.Store.DeleteUnusedDocumentsAsync();
    }

    protected override void OnAppResumed()
    {
        if (!PageVisible)
            return;
        Demo.PastChats.StartIndexing();
        Demo.PhotoSearch.RefreshIndex();
    }

    // The agent may have saved or deleted a goal.
    public override void OnReplyCompleted(Emgu.LiteRT.LM.Chat.ChatExchangeEventArgs e) => _ = Demo.Goals.RefreshCountAsync();
}
