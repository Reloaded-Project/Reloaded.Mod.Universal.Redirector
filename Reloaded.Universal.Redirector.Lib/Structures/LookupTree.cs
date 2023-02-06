﻿using System.Runtime.CompilerServices;
using System.Text;
using Reloaded.Universal.Redirector.Lib.Backports.System.Globalization;
using Reloaded.Universal.Redirector.Lib.Utility;
using RedirectionTreeTarget = Reloaded.Universal.Redirector.Lib.Structures.RedirectionTree.RedirectionTreeTarget;

namespace Reloaded.Universal.Redirector.Lib.Structures;

/// <summary>
/// A version of <see cref="RedirectionTree"/> optimised for faster lookups in the scenario of use with game folders.
/// </summary>
public struct LookupTree
{
    /*
        An O(3) lookup tree that uses the following strategy:  
            - Check common prefix.  
            - Check remaining path in dictionary.  
            - Check file name in dictionary.  
    
        Use of prefix is based on the idea that a game will have all of its files stored under a common folder path.  
        We use this to save memory in potentially huge games. 
        
        Note: I have tried O(2) as an, skipping the first step; there was in fact a very, very minor performance 
        regression; suggesting that keeping the O(3) method makes better use of CPU cache.
    */
    
    /// <summary>
    /// Prefix of all paths.
    /// Stored in upper case for faster performance.
    /// </summary>
    public string Prefix { get; private set; }
    
    /// <summary>
    /// Dictionary that maps individual subfolders to map of files.
    /// </summary>
    public SpanOfCharDict<SpanOfCharDict<RedirectionTreeTarget>> SubfolderToFiles { get; private set; }

    /// <summary>
    /// Creates a lookup tree given an existing redirection tree.
    /// </summary>
    /// <param name="tree">The tree to create the lookup tree from.</param>
#pragma warning disable CS8618
    public LookupTree(RedirectionTree.RedirectionTree tree) => CreateFromRedirectionTree(tree);
#pragma warning restore CS8618

    /// <summary>
    /// Tries to get files for a specific folder.
    /// </summary>
    /// <param name="folderPath">The folder to find.</param>
    /// <param name="value">The returned folder instance.</param>
    /// <returns>True if found, else false.</returns>
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetFolder(ReadOnlySpan<char> folderPath, out SpanOfCharDict<RedirectionTreeTarget> value)
    {
        // TODO: This can be optimised for long paths.
        // Convert to uppercase, on stack if possible.
        var folderPathUpper = folderPath.Length <= 512 
                ? stackalloc char[folderPath.Length]
                : GC.AllocateUninitializedArray<char>(folderPath.Length); // super cold path, basically never hit
        
        TextInfo.ChangeCase<TextInfo.ToUpperConversion>(folderPath, folderPathUpper);
        return TryGetFolderUpper(folderPathUpper, out value);
    }
    
    /// <summary>
    /// Tries to get files for a specific folder, assuming the input path is already in upper case.
    /// </summary>
    /// <param name="folderPath">The folder to find. Already lowercase.</param>
    /// <param name="value">The returned folder instance.</param>
    /// <returns>True if found, else false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetFolderUpper(ReadOnlySpan<char> folderPath, out SpanOfCharDict<RedirectionTreeTarget> value)
    {
        // Must be O(1)
        value = default!;        
        
        // Compare equality.
        // Note to devs: Do not invert branches, we optimise for hot paths here.
        if (folderPath.StartsWith(Prefix))
        {
            // Check for subfolder in branchless way.
            // In CLR, bool is length 1, so conversion to byte should be safe.
            // Even suppose it is not; as long as code is little endian; truncating int/4 bytes to byte still results 
            // in correct answer.
            var hasSubfolder = Prefix.Length != folderPath.Length;
            var hasSubfolderByte = Unsafe.As<bool, byte>(ref hasSubfolder);
            var nextFolder = folderPath.Slice(Prefix.Length + hasSubfolderByte);
            
            return SubfolderToFiles.TryGetValue(nextFolder, out value!);
        }
        
        return false;
    }
    
    /// <summary>
    /// Tries to get file from the lookup tree.
    /// </summary>
    /// <param name="filePath">The file to find.</param>
    /// <param name="value">The returned file instance.</param>
    /// <returns>True if found, else false.</returns>
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetFile(ReadOnlySpan<char> filePath, out RedirectionTreeTarget value)
    {
        value = default!;   
        
        // TODO: This can be optimised for long paths.
        // Convert to uppercase, on stack if possible.
        var filePathUpper = filePath.Length <= 512
            ? stackalloc char[filePath.Length]
            : GC.AllocateUninitializedArray<char>(filePath.Length); // super cold path, basically never hit
        
        TextInfo.ChangeCase<TextInfo.ToUpperConversion>(filePath, filePathUpper);
        return TryGetFileUpper(filePathUpper, out value);
    }
    
    /// <summary>
    /// Tries to get file from the lookup tree, assuming the file path is already in upper case.
    /// </summary>
    /// <param name="filePath">The file to find.</param>
    /// <param name="value">The returned file instance.</param>
    /// <returns>True if found, else false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetFileUpper(ReadOnlySpan<char> filePath, out RedirectionTreeTarget value)
    {
        value = default!;

        // Note: All input directories are expected to be canonized; so should use correct
        // directory separator char; therefore a char search is faster than the Path API
        
        // Note to devs: Do not invert branches. Setting hot path here. We don't have PGO yet.
        var directoryIndex = filePath.LastIndexOf(Path.DirectorySeparatorChar);
        if (directoryIndex != -1)
        {
            if (TryGetFolderUpper(filePath.Slice(0, directoryIndex), out var files))
            {
                var fileName = filePath.Slice(directoryIndex + 1);
                return files.TryGetValue(fileName, out value);
            }
        }
        
        return false;
    }
    
    private void CreateFromRedirectionTree(RedirectionTree.RedirectionTree tree)
    {
        // Find longest prefix path.
        var prefixBuilder = new StringBuilder(128);
        var currentNode = tree.RootNode;
        while (currentNode.Children.Count == 1 && currentNode.Files.Count == 0)
        {
            currentNode = currentNode.Children.GetFirstItem(out var key);
            prefixBuilder.Append(key);
            prefixBuilder.Append(Path.DirectorySeparatorChar);
        }

        // Remove directory separator char.
        if (prefixBuilder.Length > 0)
            prefixBuilder.Length -= 1;

        Prefix = prefixBuilder.ToString();

        // Get subdir count.
        int subdirectoryCount = LookupTreeUtilities.CountSubdirectoriesRecursive(currentNode);
        SubfolderToFiles = new SpanOfCharDict<SpanOfCharDict<RedirectionTreeTarget>>(subdirectoryCount);

        // Now walk all child nodes. 
        // Note: Generous initial capacity, since it's hard to tell without walking children.
        var subfolderBuilder = new StringBuilder(256);
        LookupTreeUtilities.BuildSubfolderToFilesRecursive(currentNode, subfolderBuilder, SubfolderToFiles);
    }
}