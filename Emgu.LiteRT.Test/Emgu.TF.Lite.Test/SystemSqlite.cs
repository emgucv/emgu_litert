// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if SYSTEM_SQLITE
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Emgu.TF.Lite.Test
{
    /// <summary>
    /// On Linux, make sqlite-net use the system SQLite instead of SQLitePCLRaw's bundled e_sqlite3.
    /// Emgu CV's Ubuntu runtime loads libcvextern.so with RTLD_GLOBAL, and it depends on the system
    /// libsqlite3.so.0 (through libgeotiff -> libproj). e_sqlite3 then binds its own sqlite3_* calls to
    /// the system copy, so its allocator is never set up and the first SQLite test after an Emgu CV test
    /// crashes in sqlite3Malloc. With one SQLite in the process there is nothing to mix up.
    /// </summary>
    internal static class SystemSqlite
    {
        [ModuleInitializer]
        internal static void Init()
        {
            // sqlite-net's static constructor selects e_sqlite3; run it first, then replace the provider.
            RuntimeHelpers.RunClassConstructor(typeof(SQLite.SQLiteConnection).TypeHandle);
            // The provider imports "sqlite3", which .NET looks for as libsqlite3.so - only present with
            // libsqlite3-dev - so load the runtime package's libsqlite3.so.0 by its full name.
            NativeLibrary.SetDllImportResolver(typeof(SQLitePCL.SQLite3Provider_sqlite3).Assembly,
                (name, assembly, path) => name == "sqlite3" && NativeLibrary.TryLoad("libsqlite3.so.0", out var handle)
                    ? handle
                    : System.IntPtr.Zero);
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
        }
    }
}
#endif
