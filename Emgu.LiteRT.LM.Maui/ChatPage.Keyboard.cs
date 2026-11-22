//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if IOS
using Foundation;
using UIKit;
#elif ANDROID
using Android.OS;
using Android.Views;
using AndroidX.Core.View;
#endif

namespace Emgu.LiteRT.LM.Maui
{
    // The on-screen keyboard on iOS and Android. On iOS MAUI's own handling (KeyboardAutoManagerScroll) moves the whole page up until the
    // message box clears the keyboard, which pushed the header and the reply being read off the top on an iPad. While
    // the page shows, it is off and the page makes room instead: its bottom padding is the keyboard's height, so the
    // conversation gets shorter and its end (ChatView keeps it in view) stays above the message box. On Android 15 and
    // later (edge to edge) the system moved the whole window up instead, off the top the same way, and the message box
    // was still half under the keyboard: the window no longer moves, and the page's bottom padding is how far the keyboard
    // covers it (the keyboard's size from the window's insets, API 30 and later; before that the system's own panning stays).
    public partial class ChatPage
    {
#if IOS || ANDROID
        private double _keyboardInset;

        private void SetKeyboardInset(double inset)
        {
            if (Math.Abs(inset - _keyboardInset) < 0.5)
                return;
            Thickness padding = Padding;
            Padding = new Thickness(padding.Left, padding.Top, padding.Right, Math.Max(0, padding.Bottom - _keyboardInset + inset));
            _keyboardInset = inset;
        }
#endif
#if IOS
        private NSObject _keyboardFrameObserver, _keyboardHideObserver;

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

        // How much of the page the keyboard covers, from the page's bottom: the bottom padding replaces the safe area the
        // page otherwise keeps clear, it doesn't add to it. With the safe area taken off first, the chat ended that much
        // too low - on an iPhone X (34 pt) the message box stayed half under the keyboard's Done bar: a 301 pt padding
        // made the chat only 267 pt shorter (2026-11-05). None for the iPad's floating keyboard, which isn't docked at the
        // bottom.
        private double KeyboardOverlap(CoreGraphics.CGRect keyboard)
        {
            if (Handler?.PlatformView is not UIView view || view.Window == null)
                return 0;
            CoreGraphics.CGRect frame = view.ConvertRectFromCoordinateSpace(keyboard, view.Window.Screen.CoordinateSpace);
            CoreGraphics.CGRect bounds = view.Bounds;
            if (frame.Height <= 0 || frame.Bottom < bounds.Bottom - 1)
                return 0;
            return Math.Max(0, bounds.Bottom - frame.Top);
        }

#elif ANDROID
        private Android.Views.View _keyboardView;
        private KeyboardLayoutListener _keyboardLayoutListener;
        private SoftInput _keyboardPreviousMode;

        private void StartKeyboardTracking()
        {
            if (_keyboardLayoutListener != null || Build.VERSION.SdkInt < BuildVersionCodes.R)
                return;
            Android.App.Activity activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            // The activity's content: the page fills it (the page's own view isn't there yet when the page first appears).
            Android.Views.View view = activity?.FindViewById(Android.Resource.Id.Content);
            if (activity?.Window == null || view == null)
                return;
            // No panning or resizing by the system (the state bits, whether the keyboard shows on its own, stay).
            _keyboardPreviousMode = activity.Window.Attributes.SoftInputMode;
            activity.Window.SetSoftInputMode((SoftInput)(((int)_keyboardPreviousMode & ~(int)SoftInput.MaskAdjust) | (int)SoftInput.AdjustNothing));
            _keyboardView = view;
            _keyboardLayoutListener = new KeyboardLayoutListener(UpdateKeyboardInset);
            view.ViewTreeObserver.AddOnGlobalLayoutListener(_keyboardLayoutListener);
        }

        private void StopKeyboardTracking()
        {
            if (_keyboardLayoutListener == null)
                return;
            _keyboardView.ViewTreeObserver.RemoveOnGlobalLayoutListener(_keyboardLayoutListener);
            _keyboardLayoutListener.Dispose();
            _keyboardLayoutListener = null;
            _keyboardView = null;
            SetKeyboardInset(0);
            Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window?.SetSoftInputMode(_keyboardPreviousMode);
        }

        // How far the keyboard covers the page, from the page's bottom edge, in device-independent units.
        private void UpdateKeyboardInset()
        {
            Android.Views.View view = _keyboardView;
            if (view == null || view.Handle == IntPtr.Zero)
                return;
            WindowInsetsCompat insets = ViewCompat.GetRootWindowInsets(view);
            int overlap = 0;
            if (insets != null && insets.IsVisible(WindowInsetsCompat.Type.Ime()))
            {
                int imeBottom = insets.GetInsets(WindowInsetsCompat.Type.Ime()).Bottom;
                int[] location = new int[2];
                view.GetLocationInWindow(location);
                overlap = Math.Max(0, location[1] + view.Height - (view.RootView.Height - imeBottom));
            }
            SetKeyboardInset(overlap / view.Context.Resources.DisplayMetrics.Density);
        }

        private sealed class KeyboardLayoutListener : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
        {
            private readonly Action _changed;

            public KeyboardLayoutListener(Action changed) { _changed = changed; }

            public void OnGlobalLayout() => _changed();
        }
#else
        private void StartKeyboardTracking() { }

        private void StopKeyboardTracking() { }
#endif
    }
}
