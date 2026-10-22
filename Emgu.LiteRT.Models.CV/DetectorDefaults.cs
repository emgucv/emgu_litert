// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.IO;

namespace Emgu.LiteRT.Models;

internal static class DetectorDefaults
{
    // Used when an app doesn't pass its own folder for the downloaded weights.
    internal static string CacheDirectory => Path.Combine(Path.GetTempPath(), "Emgu.LiteRT.Models");
}
