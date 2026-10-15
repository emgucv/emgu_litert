// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if WINDOWS || IOS || ANDROID || MACCATALYST
using System.IO;

namespace Maui.Demo.Lite
{
    internal sealed partial class ChatStore
    {
        public ChatStore() : this(Path.Combine(FileSystem.AppDataDirectory, "chats"))
        {
        }
    }
}
#endif
