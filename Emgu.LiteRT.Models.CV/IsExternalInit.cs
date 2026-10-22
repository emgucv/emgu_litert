// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if !NET5_0_OR_GREATER
// Lets the netstandard2.0 build compile C# 9 records (CocoBackendTiming); the newer target frameworks have it built in.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
