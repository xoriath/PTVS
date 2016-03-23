﻿/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestUtilities {
    public static class FileUtils {
        public static IEnumerable<string> EnumerateDirectories(
            string root,
            bool recurse = true,
            bool fullPaths = true
        ) {
            var queue = new Queue<string>();
            if (!root.EndsWith("\\")) {
                root += "\\";
            }
            queue.Enqueue(root);

            while (queue.Any()) {
                var path = queue.Dequeue();
                if (!path.EndsWith("\\")) {
                    path += "\\";
                }

                IEnumerable<string> dirs = null;
                try {
                    dirs = Directory.GetDirectories(path);
                } catch (UnauthorizedAccessException) {
                } catch (IOException) {
                }
                if (dirs == null) {
                    continue;
                }

                foreach (var d in dirs) {
                    if (!fullPaths && !d.StartsWith(root, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }
                    if (recurse) {
                        queue.Enqueue(d);
                    }
                    yield return fullPaths ? d : d.Substring(root.Length);
                }
            }
        }

        public static IEnumerable<string> EnumerateFiles(
            string root,
            string pattern = "*",
            bool recurse = true,
            bool fullPaths = true
        ) {
            if (!root.EndsWith("\\")) {
                root += "\\";
            }

            foreach (var dir in Enumerable.Repeat(root, 1).Concat(EnumerateDirectories(root, recurse, fullPaths))) {
                var fullDir = (fullPaths || Path.IsPathRooted(dir)) ? dir : (root + dir);

                IEnumerable<string> files = null;
                try {
                    files = Directory.GetFiles(fullDir, pattern);
                } catch (UnauthorizedAccessException) {
                } catch (IOException) {
                }
                if (files == null) {
                    continue;
                }

                foreach (var f in files) {
                    if (!fullPaths && !f.StartsWith(root, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }
                    yield return fullPaths ? f : f.Substring(root.Length);
                }
            }
        }

        public static void CopyDirectory(string sourceDir, string destDir) {
            sourceDir = sourceDir.TrimEnd('\\');
            destDir = destDir.TrimEnd('\\');
            try {
                Directory.CreateDirectory(destDir);
            } catch (IOException) {
            }

            var newDirectories = new HashSet<string>(EnumerateDirectories(sourceDir, fullPaths: false), StringComparer.OrdinalIgnoreCase);
            newDirectories.ExceptWith(EnumerateDirectories(destDir, fullPaths: false));

            foreach (var newDir in newDirectories.OrderBy(i => i.Length).Select(i => Path.Combine(destDir, i))) {
                try {
                    Directory.CreateDirectory(newDir);
                } catch {
                    Debug.WriteLine("Failed to create directory " + newDir);
                }
            }

            var newFiles = new HashSet<string>(EnumerateFiles(sourceDir, fullPaths: false), StringComparer.OrdinalIgnoreCase);
            newFiles.ExceptWith(EnumerateFiles(destDir, fullPaths: false));

            foreach (var newFile in newFiles) {
                var copyFrom = Path.Combine(sourceDir, newFile);
                var copyTo = Path.Combine(destDir, newFile);
                try {
                    File.Copy(copyFrom, copyTo);
                    File.SetAttributes(copyTo, FileAttributes.Normal);
                } catch {
                    Debug.WriteLine("Failed to copy " + copyFrom + " to " + copyTo);
                }
            }
        }


        public static IDisposable Backup(string path) {
            var backup = Path.GetTempFileName();
            File.Delete(backup);
            File.Copy(path, backup);
            return new FileRestorer(path, backup);
        }

        public static IDisposable TemporaryTextFile(out string path, string content) {
            var tempPath = TestData.GetTempPath();
            for (int retries = 100; retries > 0; --retries) {
                path = Path.Combine(tempPath, Path.GetRandomFileName());
                try {
                    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(stream, Encoding.Default, 128, true)) {
                        writer.Write(content);
                        return new FileDeleter(path);
                    }
                } catch (IOException) {
                } catch (UnauthorizedAccessException) {
                }
            }
            Assert.Fail("Failed to create temporary file.");
            throw new InvalidOperationException();
        }

        private sealed class FileDeleter : IDisposable {
            private readonly string _path;

            public FileDeleter(string path) {
                _path = path;
            }
            
            public void Dispose() {
                for (int retries = 10; retries > 0; --retries) {
                    try {
                        File.Delete(_path);
                        return;
                    } catch (IOException) {
                    } catch (UnauthorizedAccessException) {
                        try {
                            File.SetAttributes(_path, FileAttributes.Normal);
                        } catch (IOException) {
                        } catch (UnauthorizedAccessException) {
                        }
                    }
                    Thread.Sleep(100);
                }
            }
        }


        private sealed class FileRestorer : IDisposable {
            private readonly string _original, _backup;

            public FileRestorer(string original, string backup) {
                _original = original;
                _backup = backup;
            }

            public void Dispose() {
                for (int retries = 10; retries > 0; --retries) {
                    try {
                        File.Delete(_original);
                        File.Move(_backup, _original);
                        return;
                    } catch (IOException) {
                    } catch (UnauthorizedAccessException) {
                        try {
                            File.SetAttributes(_original, FileAttributes.Normal);
                        } catch (IOException) {
                        } catch (UnauthorizedAccessException) {
                        }
                    }
                    Thread.Sleep(100);
                }

                Assert.Fail("Failed to restore {0} from {1}", _original, _backup);
            }
        }
    }
}
