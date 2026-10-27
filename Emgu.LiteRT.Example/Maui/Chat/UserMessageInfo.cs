// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
namespace Maui.Demo.Lite;

// The page's own data for a user message, in its ChatMessageItem.Tag (the demo's message template binds to it): its
// id in the store, and the divider above the first message the model still sees after compacting.
internal sealed class UserMessageInfo : System.ComponentModel.INotifyPropertyChanged
{
    private string _dividerText;

    // Its id in the ChatStore (0 if not saved).
    public int MessageId;

    // Above the first message the model still sees after compacting: whether the earlier ones were summarized.
    public string DividerText
    {
        get => _dividerText ?? "Earlier messages summarized · tap to read";
        set { _dividerText = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DividerText))); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
}
