//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui;

/// <summary>
/// A saved chat to open when the app comes up from a notification (a scheduled task's answer): the chat page opens it when it
/// appears, or straight away if it is on the screen. The app shows the chat page if it isn't open
/// (<see cref="OpenChatRequested"/>); on Android its activity passes its intent on (<c>HandleIntent</c>, in OnCreate and OnNewIntent).
/// </summary>
public static class ChatLaunch
{
    /// <summary>The intent extra with the chat's id: "emgu_open_chat".</summary>
    public const string OpenChatExtra = "emgu_open_chat";

    private static int _pending;

    /// <summary>Raised (on the main thread) when a chat is to be opened: the app shows its chat page if it isn't open.</summary>
    public static event Action<int> OpenChatRequested;

    /// <summary>Ask for a saved chat to be opened.</summary>
    /// <param name="chatId">The chat</param>
    public static void RequestOpenChat(int chatId)
    {
        if (chatId <= 0)
            return;
        Interlocked.Exchange(ref _pending, chatId);
        MainThread.BeginInvokeOnMainThread(() => OpenChatRequested?.Invoke(chatId));
    }

    /// <summary>The chat to open, once (the page that takes it opens it).</summary>
    /// <param name="chatId">The chat</param>
    /// <returns>False if none was asked for</returns>
    public static bool TryTake(out int chatId)
    {
        chatId = Interlocked.Exchange(ref _pending, 0);
        return chatId > 0;
    }

#if ANDROID
    /// <summary>The chat a notification's intent asks for (<see cref="OpenChatExtra"/>), from the activity's OnCreate or OnNewIntent.</summary>
    /// <param name="intent">The intent</param>
    public static void HandleIntent(Android.Content.Intent intent)
    {
        int chatId = intent?.GetIntExtra(OpenChatExtra, 0) ?? 0;
        if (chatId <= 0)
            return;
        // Once: the activity keeps its intent when it is made again (a rotation).
        intent.RemoveExtra(OpenChatExtra);
        RequestOpenChat(chatId);
    }
#endif
}
