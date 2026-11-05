//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if IOS
using Foundation;
using UIKit;
#endif

namespace Emgu.LiteRT.LM.Maui
{
    // The on-screen keyboard on iOS. MAUI's own handling (KeyboardAutoManagerScroll) moves the whole page up until the
    // message box clears the keyboard, which pushed the header and the reply being read off the top on an iPad. While
    // the page shows, it is off and the page makes room instead: its bottom padding is the keyboard's height, so the
    // conversation gets shorter and its end (ChatView keeps it in view) stays above the message box.
    public partial class ChatPage
    {
#if IOS
        private NSObject _keyboardFrameObserver, _keyboardHideObserver;
        private double _keyboardInset;

        private void StartKeyboardTracking()
        {
            if (_keyboardFrameObserver != null)
                return;
            Microsoft.Maui.Platform.KeyboardAutoManagerScroll.Disconnect();
            _keyboardFrameObserver = UIKeyboard.Notifications.ObserveWillChangeFrame((_, e) => SetKeyboardInset(KeyboardOverlap(e.FrameEnd)));
            _keyboardHideObserver = UIKeyboard.Notifications.ObserveWillHide((_, _) => SetKeyboardInset(0));
        }

        private void StopKeyboardTracking()
        {
            if (_keyboardFrameObserver == null)
                return;
            _keyboardFrameObserver.Dispose();
            _keyboardHideObserver.Dispose();
            _keyboardFrameObserver = _keyboardHideObserver = null;
            SetKeyboardInset(0);
            // Back on for the app's other pages.
            Microsoft.Maui.Platform.KeyboardAutoManagerScroll.Connect();
        }

        // How much of the page the keyboard covers, above the bottom safe area the page keeps clear anyway. None for the
        // iPad's floating keyboard, which isn't docked at the bottom.
        private double KeyboardOverlap(CoreGraphics.CGRect keyboard)
        {
            if (Handler?.PlatformView is not UIView view || view.Window == null)
                return 0;
            CoreGraphics.CGRect frame = view.ConvertRectFromCoordinateSpace(keyboard, view.Window.Screen.CoordinateSpace);
            CoreGraphics.CGRect bounds = view.Bounds;
            if (frame.Height <= 0 || frame.Bottom < bounds.Bottom - 1)
                return 0;
            return Math.Max(0, bounds.Bottom - frame.Top - view.SafeAreaInsets.Bottom);
        }

        private void SetKeyboardInset(double inset)
        {
            if (Math.Abs(inset - _keyboardInset) < 0.5)
                return;
            Thickness padding = Padding;
            Padding = new Thickness(padding.Left, padding.Top, padding.Right, Math.Max(0, padding.Bottom - _keyboardInset + inset));
            _keyboardInset = inset;
        }
#else
        private void StartKeyboardTracking() { }

        private void StopKeyboardTracking() { }
#endif
    }
}
