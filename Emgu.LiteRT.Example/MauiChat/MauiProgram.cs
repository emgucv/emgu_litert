//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Maui;

namespace MauiChat;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        // UseLiteRtChat registers the handlers the chat page needs (Enter sends on Mac Catalyst).
        builder.UseMauiApp<App>().UseLiteRtChat();
        return builder.Build();
    }
}
