// TreeCompare.cs
// Byte-for-byte tree comparison for the E: Disk Migration Kit.
// MIT License. See LICENSE in the repository root.
//
// Design:
//   - enumerate relative paths on both sides
//   - skip System Volume Information, $RECYCLE.BIN and reparse points
//   - compare type, length and content (1 MiB buffer)
//   - write a progress CSV every N checked files
//   - keep up to 100000 mismatch descriptions for diagnosis
//
// This file is intentionally standalone so it can be compiled with Add-Type.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public sealed class TreeCompareResult
{
    public bool Success;
    public long SourceFiles;
    public long DestinationFiles;
    public long SourceDirectories;
    public long DestinationDirectories;
    public long SourceBytes;
    public long DestinationBytes;
    public int MismatchCount;
    public List<string> Mismatches = new List<string>();
}

public static class TreeCompare
{
    private sealed class EntryInfo
    {
        public bool IsDirectory;
        public long Length;
    }

    private static Dictionary<string, EntryInfo> EnumerateTree(
        string root,
        string excludedDirectoryName,
        out long directoryCount,
        out long fileCount,
        out long totalBytes)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }

        var entries = new Dictionary<string, EntryInfo>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(root);
        directoryCount = 0;
        fileCount = 0;
        totalBytes = 0;

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(current);
            }
            catch
            {
                continue;
            }

            foreach (var child in children)
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(child);
                }
                catch
                {
                    continue;
                }

                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var name = Path.GetFileName(child);
                if (isDirectory &&
                    (name.Equals(excludedDirectoryName, StringComparison.OrdinalIgnoreCase) ||
                     name.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var relativePath = child.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var info = new EntryInfo { IsDirectory = isDirectory };
                if (isDirectory)
                {
                    directoryCount++;
                    stack.Push(child);
                }
                else
                {
                    fileCount++;
                    try
                    {
                        info.Length = new FileInfo(child).Length;
                        totalBytes += info.Length;
                    }
                    catch
                    {
                        info.Length = -1;
                    }
                }
                entries[relativePath] = info;
            }
        }

        return entries;
    }

    private static bool FilesAreEqual(string sourcePath, string destinationPath)
    {
        const int bufferSize = 1024 * 1024;
        using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize, FileOptions.SequentialScan))
        using (var destination = new FileStream(destinationPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize, FileOptions.SequentialScan))
        {
            if (source.Length != destination.Length)
            {
                return false;
            }

            var sourceBuffer = new byte[bufferSize];
            var destinationBuffer = new byte[bufferSize];
            while (true)
            {
                var sourceRead = source.Read(sourceBuffer, 0, sourceBuffer.Length);
                var destinationRead = destination.Read(destinationBuffer, 0, destinationBuffer.Length);
                if (sourceRead != destinationRead)
                {
                    return false;
                }
                if (sourceRead == 0)
                {
                    return true;
                }

                for (var i = 0; i < sourceRead; i++)
                {
                    if (sourceBuffer[i] != destinationBuffer[i])
                    {
                        return false;
                    }
                }
            }
        }
    }

    public static TreeCompareResult Compare(string sourceRoot, string destinationRoot, string excludedDirectoryName, string progressPath)
    {
        var result = new TreeCompareResult();
        var sourceEntries = EnumerateTree(
            sourceRoot,
            excludedDirectoryName,
            out result.SourceDirectories,
            out result.SourceFiles,
            out result.SourceBytes);
        var destinationEntries = EnumerateTree(
            destinationRoot,
            excludedDirectoryName,
            out result.DestinationDirectories,
            out result.DestinationFiles,
            out result.DestinationBytes);

        var allPaths = new HashSet<string>(sourceEntries.Keys, StringComparer.OrdinalIgnoreCase);
        allPaths.UnionWith(destinationEntries.Keys);
        var sortedPaths = allPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        long verifiedBytes = 0;
        var verifiedFiles = 0;

        using (var progress = new StreamWriter(progressPath, false, new UTF8Encoding(false)))
        {
            progress.AutoFlush = true;
            progress.WriteLine("CheckedAt,SourceBytesChecked,FilesChecked,Mismatches");

            foreach (var relativePath in sortedPaths)
            {
                EntryInfo sourceEntry;
                EntryInfo destinationEntry;
                sourceEntries.TryGetValue(relativePath, out sourceEntry);
                destinationEntries.TryGetValue(relativePath, out destinationEntry);

                if (sourceEntry == null || destinationEntry == null)
                {
                    result.MismatchCount++;
                    if (result.Mismatches.Count < 100000)
                    {
                        result.Mismatches.Add("Missing on one side: " + relativePath);
                    }
                    continue;
                }

                if (sourceEntry.IsDirectory != destinationEntry.IsDirectory)
                {
                    result.MismatchCount++;
                    if (result.Mismatches.Count < 100000)
                    {
                        result.Mismatches.Add("Type mismatch: " + relativePath);
                    }
                    continue;
                }

                if (sourceEntry.IsDirectory)
                {
                    continue;
                }

                if (sourceEntry.Length != destinationEntry.Length)
                {
                    result.MismatchCount++;
                    if (result.Mismatches.Count < 100000)
                    {
                        result.Mismatches.Add("Length mismatch: " + relativePath);
                    }
                    continue;
                }

                var sourcePath = Path.Combine(sourceRoot, relativePath);
                var destinationPath = Path.Combine(destinationRoot, relativePath);
                bool equal;
                try
                {
                    equal = FilesAreEqual(sourcePath, destinationPath);
                }
                catch (Exception ex)
                {
                    equal = false;
                    if (result.Mismatches.Count < 100000)
                    {
                        result.Mismatches.Add("Read error: " + relativePath + " :: " + ex.Message);
                    }
                }

                if (!equal)
                {
                    result.MismatchCount++;
                    if (result.Mismatches.Count < 100000)
                    {
                        result.Mismatches.Add("Content mismatch: " + relativePath);
                    }
                }

                verifiedFiles++;
                verifiedBytes += sourceEntry.Length;
                if (verifiedFiles % 2000 == 0)
                {
                    progress.WriteLine(
                        DateTimeOffset.Now.ToString("o") + "," +
                        verifiedBytes + "," +
                        verifiedFiles + "," +
                        result.MismatchCount);
                }
            }

            result.Success =
                result.MismatchCount == 0 &&
                result.SourceFiles == result.DestinationFiles &&
                result.SourceDirectories == result.DestinationDirectories &&
                result.SourceBytes == result.DestinationBytes;

            progress.WriteLine(
                DateTimeOffset.Now.ToString("o") + "," +
                verifiedBytes + "," +
                verifiedFiles + "," +
                result.MismatchCount);
        }

        return result;
    }
}
