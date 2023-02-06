﻿using System.Runtime.InteropServices;
using Reloaded.Universal.Redirector.Lib.Interfaces;
using Reloaded.Universal.Redirector.Lib.Structures;
using Reloaded.Universal.Redirector.Lib.Structures.RedirectionTree;
using Reloaded.Universal.Redirector.Lib.Structures.RedirectionTreeManager;

namespace Reloaded.Universal.Redirector.Lib;

/// <summary>
/// Class responsible for building redirection trees at runtime,
/// maintaining list of sources for redirection and building the necessary trees as required.
/// </summary>
public class RedirectionTreeManager : IFolderRedirectionUpdateReceiver
{
    /// <summary>
    /// List of individual file redirections.
    /// These should be small and take priority over folder ones.
    /// </summary>
    public List<FileRedirection> FileRedirections { get; private set; } = new();

    /// <summary>
    /// List of folder redirections.
    /// </summary>
    public List<FolderRedirection> FolderRedirections { get; private set; } = new();
    
    /// <summary>
    /// The redirection tree currently being built.
    /// </summary>
    public RedirectionTree RedirectionTree { get; private set; }
    
    /// <summary>
    /// The current lookup tree.
    /// </summary>
    public LookupTree Lookup { get; private set; }

    /// <summary>
    /// True if the manager is currently using the lookup tree for lookups, else false.
    /// </summary>
    public bool UsingLookupTree { get; private set; }

    /*
       Events that trigger a full rebuild [& reason]:
       - Remove File                                  [We don't keep track of what maps to a file.]
       - Remove Folder Map                            [We don't keep track of what maps to a file.]
       - Remove Folder                                [We don't keep track of what maps to a file.]
       - Folder Mapping Update Event: Remove Item     [We don't keep track of what maps to a file.]
    
       Partial/Conditional Rebuild:
       - Add Folder Map                        [Partial Rebuild. Apply folder then re-apply files].
       - Folder Mapping Update Event: Add File [Rebuild if File Previously Mapped].
       
           Note: Update of existing lookup tree can only be done if the existing prefix is shared.
           If prefix of item does not map; full tree needs reconstruction.
    */
    
    // TODO: Implement Folder Mapping Add File Event w/o Rebuild.
    
    /// <summary>
    /// Adds an individual file redirection to the manager.
    /// </summary>
    /// <param name="fileRedirection">The file redirection to use.</param>
    public void AddFileRedirection(FileRedirection fileRedirection)
    {
        FileRedirections.Add(fileRedirection);
        
        // We can freely add files until we kick in the 'optimise' button because files are
        // supposed to take priority.
        if (UsingLookupTree)
            Rebuild();
        else
            ApplyFileRedirection(RedirectionTree, fileRedirection);
    }

    /// <summary>
    /// Removes a file redirection from the manager.
    /// </summary>
    /// <param name="fileRedirection">The file redirection to remove.</param>
    public void RemoveFileRedirection(FileRedirection fileRedirection)
    {
        if (!FileRedirections.Remove(fileRedirection)) 
            return;
        
        // We don't know what previously occupied file slot so full rebuild is needed.
        Rebuild();
    }

    /// <summary>
    /// Adds an individual folder redirection to the lookup tree.
    /// </summary>
    /// <param name="folderRedirection">The folder redirection to use.</param>
    public void AddFolderRedirection(FolderRedirection folderRedirection)
    {
        FolderRedirections.Add(folderRedirection);
        
        // We can freely add files until we kick in the 'optimise' button because files are
        // supposed to take priority.
        if (UsingLookupTree)
        {
            Rebuild();
        }
        else
        {
            ApplyFolderRedirection(RedirectionTree, folderRedirection);

            // We need to reapply file redirections just in case there is one that overlaps with our folder redirection.
            ApplyFileRedirections(RedirectionTree);
        }
    }
    
    /// <summary>
    /// Removes the folder redirection from the lookup tree.
    /// </summary>
    /// <param name="folderRedirection">
    ///     The folder redirection originally passed to <see cref="AddFolderRedirection"/>.
    /// </param>
    public void RemoveFolderRedirection(FolderRedirection folderRedirection)
    {
        // We don't know if this redirection overwrote anything, so a full rebuild is needed.
        if (FolderRedirections.Remove(folderRedirection))
            Rebuild();
    }

    /// <summary>
    /// Builds the redirection tree from scratch.
    /// </summary>
    private void Rebuild()
    {
        var tree = RedirectionTree.Create();
        
        ApplyFolderRedirections(tree);
        ApplyFileRedirections(tree);
        
        RedirectionTree = tree;
        if (UsingLookupTree)
            Optimise_Impl();
    }
    
    /// <inheritdoc />
    public void OnOtherUpdate(FolderRedirection sender)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public void OnFileAddition(FolderRedirection sender, string filePath)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Optimises the lookup operation by converting the redirection tree to the lookup tree.
    /// </summary>
    /// <remarks>
    ///    Any additions done past this point will require a full tree rebuild.
    ///    So this is only intended to be called after all mods initialise.
    /// </remarks>
    public void Optimise()
    {
        if (UsingLookupTree)
            return;

        Optimise_Impl();
    }

    private void Optimise_Impl()
    {
        Lookup = new LookupTree(RedirectionTree);
        UsingLookupTree = true;
        RedirectionTree = default;
    }
    
    private void ApplyFileRedirection(RedirectionTree tree, FileRedirection fileRedirection)
    {
        tree.AddPath(fileRedirection.OldPath, fileRedirection.NewPath);
    }
    
    private void ApplyFolderRedirection(RedirectionTree tree, FolderRedirection folderRedirection)
    {
        throw new NotImplementedException();
        //tree.AddFolderPaths(folderRedirection.SourceFolder, fileRedirection.NewPath, folderRedirection.TargetFolder);
    }
    
    private void ApplyFileRedirections(RedirectionTree tree)
    {
        foreach (var fileRedirection in CollectionsMarshal.AsSpan(FileRedirections))
            ApplyFileRedirection(tree, fileRedirection);
    }

    private void ApplyFolderRedirections(RedirectionTree tree)
    {
        foreach (var folder in CollectionsMarshal.AsSpan(FolderRedirections))
            ApplyFolderRedirection(tree, folder);
    }
}