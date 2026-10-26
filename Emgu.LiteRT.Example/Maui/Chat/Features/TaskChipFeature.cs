// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

// The "Plan" chip above the message box: the next message is planned as a task and run step by step (TaskFeature's
// client), then the chip is off again. The page asks TakePlan() when it sends.
internal sealed class TaskChipFeature : DemoFeature
{
    private bool _planNext;

    public override string Name => "plan";

    public override string Title => "Plan";

    public override bool PersistEnabled => false;

    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    public override ChatChip BuildComposerChip() =>
        new ChatChip("\U0001F4CB Plan", () => _planNext, on => _planNext = on)
        {
            IsVisible = () => Host?.Model?.CanUseTools == true && Host.HandsFree != true
        };

    // Whether the message being sent is to be planned; the chip is off again afterwards.
    public bool TakePlan()
    {
        if (!_planNext)
            return false;
        _planNext = false;
        Host.RefreshChips();
        return true;
    }
}
