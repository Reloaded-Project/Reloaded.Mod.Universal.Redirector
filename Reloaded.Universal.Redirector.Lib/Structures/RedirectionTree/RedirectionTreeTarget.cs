﻿using System.Diagnostics;
using Reloaded.Universal.Redirector.Lib.Utility;

namespace Reloaded.Universal.Redirector.Lib.Structures.RedirectionTree;

/// <summary>
/// Target for a file covered by the redirection tree.
/// </summary>
public struct RedirectionTreeTarget
{
    private static string _directorySeparatorCharStr = Path.DirectorySeparatorChar.ToString();
    
    /// <summary>
    /// Path to the directory storing the file.
    /// </summary>
    public string Directory; // (This is deduplicated, saving memory)
    
    /// <summary>
    /// Name of the file in the directory.
    /// </summary>
    public string FileName;

    /// <summary>
    /// True if this is a directory, else false.
    /// </summary>
    public bool IsDirectory;

    /// <summary/>
    /// <param name="directory">Directory path.</param>
    /// <param name="fileName">File name.</param>
    /// <param name="isDirectory">True if this is a directory else false.</param>
    public RedirectionTreeTarget(string directory, string fileName, bool isDirectory)
    {
        Directory = directory;
        FileName = fileName;
        IsDirectory = isDirectory;
    }

    /// <summary/>
    /// <param name="fullPath">Full path, must be canonical, i.e. use correct separator char..</param>
    /// <param name="isDirectory">True if this entry represents a directory, else false.</param>
    public RedirectionTreeTarget(string fullPath, bool isDirectory)
    {
        IsDirectory = isDirectory;
        var separatorIndex = fullPath.LastIndexOf(Path.DirectorySeparatorChar);
        Debug.Assert(separatorIndex != -1, "Must be a full path.");
        
        Directory = fullPath.Substring(0, separatorIndex);
        FileName = fullPath.Substring(separatorIndex + 1);
    }

    /// <summary>
    /// Returns the full path of the file.
    /// </summary>
    public string GetFullPath()
    {
        return string.Concat(Directory, _directorySeparatorCharStr, FileName);
    }
    
    /// <summary>
    /// Returns the full path of the file.
    /// </summary>
    public string GetFullPathWithDevicePrefix()
    {
        return string.Concat(Strings.PrefixLocalDeviceStr, Directory, _directorySeparatorCharStr, FileName);
    }
    
    // Test use only.
    
    /// <summary/>
    public static implicit operator RedirectionTreeTarget(string s) => new(s, true);
}