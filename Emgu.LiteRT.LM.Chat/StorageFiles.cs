//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>
    /// The files a <see cref="StorageCatalog"/> measures and deletes: a model's file with its partial downloads and
    /// CPU/GPU caches, a folder's contents. Every deletion stays inside the root it is given and never follows links.
    /// </summary>
    public static class StorageFiles
    {
        private static StringComparison Comparison => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>True when the path is inside the root (not the root itself).</summary>
        /// <param name="root">The root folder</param>
        /// <param name="path">The path</param>
        public static bool IsWithin(string root, string path)
        {
            string parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(parent, Comparison);
        }

        /// <summary>A model's files: the model, its partial download and markers, and its CPU/GPU caches beside it.</summary>
        /// <param name="model">The model file's path</param>
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
                    || (item.IndexOf("mldrift", StringComparison.OrdinalIgnoreCase) >= 0 && item.EndsWith(".bin", Comparison));
            }).Where(file => (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0).ToList();
        }

        /// <summary>The size of a file, or of a folder's contents; 0 for a link or nothing there.</summary>
        /// <param name="path">The file or folder</param>
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

        /// <summary>Delete a model's files (<see cref="ModelFiles"/>), which must be right inside the model root.</summary>
        /// <param name="modelRoot">The models' folder</param>
        /// <param name="model">The model file's path</param>
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

        /// <summary>Delete a temporary file of the app's own (a picker's copy in the cache); nothing outside the cache root.</summary>
        /// <param name="cacheRoot">The cache folder</param>
        /// <param name="file">The file</param>
        public static void DeleteTemporaryFile(string cacheRoot, string file)
        {
            // Picker originals may be anywhere; only remove app-owned cache copies, never library/user originals.
            if (string.IsNullOrWhiteSpace(file) || !IsWithin(cacheRoot, file) || !File.Exists(file)) return;
            RejectLinkedParents(cacheRoot, file);
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) File.Delete(file);
        }

        /// <summary>Delete a folder inside a root, with its contents (links are neither followed nor allowed on the way).</summary>
        /// <param name="root">The root folder</param>
        /// <param name="target">The folder to delete</param>
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
