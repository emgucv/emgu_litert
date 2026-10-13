#if WINDOWS
using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Maui.Demo.Lite;

public partial class LiteRtLmChatPage
{
    private TextBox _windowsPrompt;
    private bool _promptComposing;

    private void ConfigurePromptKeyboard()
    {
        SemanticProperties.SetHint(_promptEditor, "Enter sends your message. Shift+Enter adds a new line.");
        _promptEditor.HandlerChanging += (sender, args) => DetachPromptKeyboard();
        _promptEditor.HandlerChanged += (sender, args) =>
        {
            DetachPromptKeyboard();
            _windowsPrompt = _promptEditor.Handler?.PlatformView as TextBox;
            if (_windowsPrompt == null)
                return;
            _windowsPrompt.PreviewKeyDown += OnPromptKeyDown;
            _windowsPrompt.TextCompositionStarted += OnPromptCompositionStarted;
            _windowsPrompt.TextCompositionEnded += OnPromptCompositionEnded;
        };
    }

    private void DetachPromptKeyboard()
    {
        if (_windowsPrompt != null)
        {
            _windowsPrompt.PreviewKeyDown -= OnPromptKeyDown;
            _windowsPrompt.TextCompositionStarted -= OnPromptCompositionStarted;
            _windowsPrompt.TextCompositionEnded -= OnPromptCompositionEnded;
            _windowsPrompt = null;
        }
        _promptComposing = false;
    }

    private void OnPromptCompositionStarted(TextBox sender, TextCompositionStartedEventArgs args) => _promptComposing = true;
    private void OnPromptCompositionEnded(TextBox sender, TextCompositionEndedEventArgs args) => _promptComposing = false;

    private void OnPromptKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || _promptComposing || KeyDown(VirtualKey.Shift)
            || KeyDown(VirtualKey.Control) || KeyDown(VirtualKey.Menu))
            return;
        // Consume plain Enter even while sending/reloading, so it never inserts an accidental newline.
        args.Handled = true;
        if (!args.KeyStatus.WasKeyDown)
            OnSendClicked(_promptEditor, EventArgs.Empty);
    }

    private static bool KeyDown(VirtualKey key) =>
        (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
}
#endif
