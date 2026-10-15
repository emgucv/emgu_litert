#if MACCATALYST
using Foundation;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using ObjCRuntime;
using UIKit;

namespace Maui.Demo.Lite;

// Only the chat composer uses these shortcuts; other multiline editors keep their normal behavior.
internal sealed class ChatPromptEditor : Editor
{
    public event EventHandler SendRequested;
    internal void RequestSend() => SendRequested?.Invoke(this, EventArgs.Empty);
}

internal sealed class ChatPromptEditorHandler : EditorHandler
{
    protected override MauiTextView CreatePlatformView() => new ChatPromptTextView();

    protected override void ConnectHandler(MauiTextView platformView)
    {
        base.ConnectHandler(platformView);
        ((ChatPromptTextView)platformView).SendRequested += OnSendRequested;
    }

    protected override void DisconnectHandler(MauiTextView platformView)
    {
        ((ChatPromptTextView)platformView).SendRequested -= OnSendRequested;
        base.DisconnectHandler(platformView);
    }

    private void OnSendRequested(object sender, EventArgs args) => ((ChatPromptEditor)VirtualView).RequestSend();
}

internal sealed class ChatPromptTextView : MauiTextView
{
    private static readonly Selector SendSelector = new Selector("sendChatMessage:");
    private static readonly Selector NewlineSelector = new Selector("insertChatNewline:");
    private readonly UIKeyCommand _send;
    private readonly UIKeyCommand _newline;
    private readonly UIKeyCommand _optionNewline;

    public event EventHandler SendRequested;

    public ChatPromptTextView()
    {
        _send = UIKeyCommand.Create(new NSString("\r"), (UIKeyModifierFlags)0, SendSelector);
        _newline = UIKeyCommand.Create(new NSString("\r"), UIKeyModifierFlags.Shift, NewlineSelector);
        _optionNewline = UIKeyCommand.Create(new NSString("\r"), UIKeyModifierFlags.Alternate, NewlineSelector);
        _send.WantsPriorityOverSystemBehavior = true;
        _newline.WantsPriorityOverSystemBehavior = true;
        _optionNewline.WantsPriorityOverSystemBehavior = true;
    }

    public override UIKeyCommand[] KeyCommands => MarkedTextRange != null
        ? base.KeyCommands
        : (base.KeyCommands ?? Array.Empty<UIKeyCommand>()).Concat(new[] { _send, _newline, _optionNewline }).ToArray();

    public override bool CanPerform(Selector action, NSObject sender)
    {
        if (action == SendSelector || action == NewlineSelector)
            return Editable && UserInteractionEnabled && MarkedTextRange == null;
        return base.CanPerform(action, sender);
    }

    [Export("sendChatMessage:")]
    private void SendMessage(UIKeyCommand command)
    {
        if (Editable && UserInteractionEnabled && MarkedTextRange == null)
            SendRequested?.Invoke(this, EventArgs.Empty);
    }

    [Export("insertChatNewline:")]
    private void InsertNewline(UIKeyCommand command)
    {
        if (Editable && UserInteractionEnabled && MarkedTextRange == null)
            InsertText("\n");
    }
}
#endif
