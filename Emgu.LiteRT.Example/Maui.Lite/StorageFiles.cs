// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Maui.Demo.Lite
{
    internal static class StorageFiles
    {
        private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        public static bool IsWithin(string root, string path)
        {
            string parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(parent, Comparison);
        }

        public static List<string> ModelFiles(string model)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(model));
            if (!Directory.Exists(folder)) return new List<string>();
            string name = Path.GetFileName(model);
            return Directory.EnumerateFiles(folder).Where(file =>
            {
                string item = Path.GetFileName(file);
                if (item.Equals(name, Comparison)) return true;
                if (!item.StartsWith(name + ".", Comparison) && !item.StartsWith(name + "_", Comparison)) return false;
                return new[] { ".sha256ok", ".part", ".part.etag", ".part.sha256ok" }.Any(s => item.Equals(name + s, Comparison))
                    || item.EndsWith(".xnnpack_cache", Comparison)
                    || (item.Contains("mldrift", StringComparison.OrdinalIgnoreCase) && item.EndsWith(".bin", Comparison));
            }).Where(file => (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0).ToList();
        }

        public static long Size(string path)
        {
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path)) return 0;
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return 0;
                if (File.Exists(path)) return new FileInfo(path).Length;
                return Directory.EnumerateFileSystemEntries(path).Sum(Size);
            }
            catch (FileNotFoundException) { return 0; }
            catch (DirectoryNotFoundException) { return 0; }
        }

        public static void DeleteModel(string modelRoot, string model)
        {
            if (!IsWithin(modelRoot, model) || Path.GetDirectoryName(Path.GetFullPath(model)) != Path.GetFullPath(modelRoot))
                throw new InvalidOperationException("The model is outside its storage folder.");
            RejectLinkedParents(modelRoot, model);
            foreach (string file in ModelFiles(model))
            {
                if (!IsWithin(modelRoot, file)) throw new InvalidOperationException("Invalid model cache path.");
                File.Delete(file);
            }
        }

        public static void DeleteTemporaryFile(string cacheRoot, string file)
        {
            // Picker originals may be anywhere; only remove app-owned cache copies, never library/user originals.
            if (string.IsNullOrWhiteSpace(file) || !IsWithin(cacheRoot, file) || !File.Exists(file)) return;
            RejectLinkedParents(cacheRoot, file);
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) File.Delete(file);
        }

        public static void DeleteDirectory(string root, string target)
        {
            if (!IsWithin(root, target)) throw new InvalidOperationException("The folder is outside its storage root.");
            RejectLinkedParents(root, target);
            if (!Directory.Exists(target)) return;
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Storage deletion does not follow linked folders.");
            DeleteContents(target);
            Directory.Delete(target);
        }

        private static void DeleteContents(string folder)
        {
            foreach (string file in Directory.EnumerateFiles(folder)) File.Delete(file);
            foreach (string child in Directory.EnumerateDirectories(folder))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) DeleteContents(child);
                Directory.Delete(child);
            }
        }

        private static void RejectLinkedParents(string root, string target)
        {
            string parent = Path.GetDirectoryName(Path.GetFullPath(target));
            string fullRoot = Path.GetFullPath(root);
            while (parent != null)
            {
                if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Storage deletion does not traverse linked folders.");
                if (parent.Equals(fullRoot, Comparison)) break;
                parent = Path.GetDirectoryName(parent);
            }
        }
    }
}
