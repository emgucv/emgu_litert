//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if MACCATALYST
using Foundation;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using ObjCRuntime;
using UIKit;
#endif

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// The chat's message box: an Editor where, with a keyboard, Enter sends the message and Shift+Enter (or Option+Enter)
    /// adds a new line - on Mac Catalyst through <c>ChatPromptEditorHandler</c>, which
    /// <c>builder.UseLiteRtChat()</c> registers, and on Windows through the text box's key events. Elsewhere it is an
    /// ordinary Editor. <see cref="ChatView"/>'s default template uses it as "PART_Input".
    /// </summary>
    public class ChatPromptEditor : Editor
    {
        /// <summary>Raised when the user pressed Enter to send.</summary>
        public event EventHandler SendRequested;

        /// <summary>Raise <see cref="SendRequested"/>.</summary>
        public void RequestSend() => SendRequested?.Invoke(this, EventArgs.Empty);

#if WINDOWS
        private Microsoft.UI.Xaml.Controls.TextBox _textBox;
        private bool _composing;

        /// <summary>Create the editor.</summary>
        public ChatPromptEditor()
        {
            HandlerChanging += (s, e) => Detach();
            HandlerChanged += (s, e) =>
            {
                Detach();
                _textBox = Handler?.PlatformView as Microsoft.UI.Xaml.Controls.TextBox;
                if (_textBox == null)
                    return;
                _textBox.PreviewKeyDown += OnKeyDown;
                _textBox.TextCompositionStarted += OnCompositionStarted;
                _textBox.TextCompositionEnded += OnCompositionEnded;
            };
        }

        private void Detach()
        {
            if (_textBox != null)
            {
                _textBox.PreviewKeyDown -= OnKeyDown;
                _textBox.TextCompositionStarted -= OnCompositionStarted;
                _textBox.TextCompositionEnded -= OnCompositionEnded;
                _textBox = null;
            }
            _composing = false;
        }

        private void OnCompositionStarted(Microsoft.UI.Xaml.Controls.TextBox sender, Microsoft.UI.Xaml.Controls.TextCompositionStartedEventArgs args) => _composing = true;

        private void OnCompositionEnded(Microsoft.UI.Xaml.Controls.TextBox sender, Microsoft.UI.Xaml.Controls.TextCompositionEndedEventArgs args) => _composing = false;

        private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
        {
            if (args.Key != Windows.System.VirtualKey.Enter || _composing || KeyDown(Windows.System.VirtualKey.Shift)
                || KeyDown(Windows.System.VirtualKey.Control) || KeyDown(Windows.System.VirtualKey.Menu))
                return;
            // Plain Enter never inserts a new line, even while a reply is being written.
            args.Handled = true;
            if (!args.KeyStatus.WasKeyDown)
                RequestSend();
        }

        private static bool KeyDown(Windows.System.VirtualKey key) =>
            (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
#endif
    }

#if MACCATALYST
    /// <summary>
    /// The Mac Catalyst handler of <see cref="ChatPromptEditor"/>: Enter sends, Shift+Enter and Option+Enter add a new
    /// line. Registered by <c>builder.UseLiteRtChat()</c>.
    /// </summary>
    public sealed class ChatPromptEditorHandler : EditorHandler
    {
        /// <inheritdoc/>
        protected override MauiTextView CreatePlatformView() => new ChatPromptTextView();

        /// <inheritdoc/>
        protected override void ConnectHandler(MauiTextView platformView)
        {
            base.ConnectHandler(platformView);
            ((ChatPromptTextView)platformView).SendRequested += OnSendRequested;
        }

        /// <inheritdoc/>
        protected override void DisconnectHandler(MauiTextView platformView)
        {
            ((ChatPromptTextView)platformView).SendRequested -= OnSendRequested;
            base.DisconnectHandler(platformView);
        }

        private void OnSendRequested(object sender, EventArgs args) => (VirtualView as ChatPromptEditor)?.RequestSend();
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

        // While an input method composes text (MarkedTextRange), Enter is its own.
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
}
