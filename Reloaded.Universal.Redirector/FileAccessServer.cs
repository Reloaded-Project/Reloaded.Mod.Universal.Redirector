using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Universal.Redirector.Utility;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace Reloaded.Universal.Redirector;

/// <summary>
/// Intercepts I/O access on the Win32
/// </summary>
public unsafe class FileAccessServer
{
    private static Redirector _redirector = null!;
    private static RedirectorController _redirectorController = null!;
    private static IHook<Native.NtCreateFile> _ntCreateFileHook = null!;
    private static IHook<Native.NtQueryAttributesFile> _ntQueryAttributesFileHook = null!;
    private const string _prefix = "\\??\\";

    [ThreadStatic]
    private static bool _inQueryAttributesFile;

    public static void Initialize(IReloadedHooks hooks, Redirector redirector,
        RedirectorController redirectorController)
    {
        _redirector = redirector;
        _redirectorController = redirectorController;

        // Get Hooks
        var ntdllHandle = Native.LoadLibraryW("ntdll");
        var ntCreateFilePointer = Native.GetProcAddress(ntdllHandle, "NtCreateFile");
        var ntQueryAttributesFilePointer = Native.GetProcAddress(ntdllHandle, "NtQueryAttributesFile");

        // Kick off the server
        if (ntCreateFilePointer != IntPtr.Zero)
            _ntCreateFileHook = hooks.CreateHook<Native.NtCreateFile>(
                (delegate* unmanaged[Stdcall]<IntPtr*, FileAccess, Native.OBJECT_ATTRIBUTES*, Native.IO_STATUS_BLOCK*,
                    long*, uint, FileShare, uint, uint, IntPtr, uint, int>) &NtCreateFileHookFn,
                (long) ntCreateFilePointer).Activate();

        if (ntQueryAttributesFilePointer != IntPtr.Zero)
            _ntQueryAttributesFileHook = hooks.CreateHook<Native.NtQueryAttributesFile>(
                (delegate* unmanaged[Stdcall]<Native.OBJECT_ATTRIBUTES*, IntPtr, int>) &NtQueryAttributesFileHookFn,
                (long) ntQueryAttributesFilePointer).Activate();
    }

    /* Hooks */

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int NtQueryAttributesFileHookFn(Native.OBJECT_ATTRIBUTES* objectAttributes, IntPtr fileAttributes)
    {
        if (_inQueryAttributesFile)
            return _ntQueryAttributesFileHook.OriginalFunction.Value.Invoke(objectAttributes, fileAttributes);

        _inQueryAttributesFile = true;
        try
        {
            var attributes = objectAttributes;
            if (attributes->ObjectName == null)
                return _ntQueryAttributesFileHook.OriginalFunction.Value.Invoke(objectAttributes, fileAttributes);

            if (!TryGetNewPath(attributes->ObjectName->ToString(), out string newFilePath))
                return _ntQueryAttributesFileHook.OriginalFunction.Value.Invoke(objectAttributes, fileAttributes);

            var newObjectPath = _prefix + newFilePath;
            fixed (char* address = newObjectPath)
            {
                // Backup original string.
                var originalObjectName = attributes->ObjectName;
                var originalDirectory = attributes->RootDirectory;

                // Set new file path
                var newObjectName = new Native.UNICODE_STRING(address, newObjectPath.Length);
                attributes->ObjectName = &newObjectName;
                attributes->RootDirectory = IntPtr.Zero;

                // Call function with new file path.
                var returnValue = _ntQueryAttributesFileHook.OriginalFunction.Value.Invoke(objectAttributes, fileAttributes);;

                // Reset original string.
                attributes->ObjectName = originalObjectName;
                attributes->RootDirectory = originalDirectory;
                return returnValue;
            }
        }
        finally
        {
            _inQueryAttributesFile = false;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int NtCreateFileHookFn(IntPtr* fileHandle, FileAccess access,
        Native.OBJECT_ATTRIBUTES* objectAttributes, Native.IO_STATUS_BLOCK* ioStatus, long* allocSize,
        uint fileattributes, FileShare share, uint createDisposition, uint createOptions, IntPtr eaBuffer,
        uint eaLength)
    {
        // Get name of file to be loaded.
        var attributes = objectAttributes;
        if (attributes->ObjectName == null)
            return _ntCreateFileHook.OriginalFunction.Value.Invoke(fileHandle, access, objectAttributes, ioStatus,
                allocSize, fileattributes, share, createDisposition, createOptions, eaBuffer, eaLength);

        if (!TryGetNewPath(attributes->ObjectName->ToString(), out string newFilePath))
            return _ntCreateFileHook.OriginalFunction.Value.Invoke(fileHandle, access, objectAttributes, ioStatus,
                allocSize, fileattributes, share, createDisposition, createOptions, eaBuffer, eaLength);

        var newObjectPath = _prefix + newFilePath;
        fixed (char* address = newObjectPath)
        {
            // Backup original string.
            var originalObjectName = attributes->ObjectName;
            var originalDirectory = attributes->RootDirectory;

            // Set new file path
            var newObjectName = new Native.UNICODE_STRING(address, newObjectPath.Length);
            attributes->ObjectName = &newObjectName;
            attributes->RootDirectory = IntPtr.Zero;

            // Call function with new file path.
            var returnValue = _ntCreateFileHook.OriginalFunction.Value.Invoke(fileHandle, access, objectAttributes,
                ioStatus, allocSize, fileattributes, share, createDisposition, createOptions, eaBuffer, eaLength);

            // Reset original string.
            attributes->ObjectName = originalObjectName;
            attributes->RootDirectory = originalDirectory;
            return returnValue;
        }
    }

    private static bool TryGetNewPath(string oldFilePath, out string newFilePath)
    {
        oldFilePath = oldFilePath.TrimStart(_prefix);
        if (!String.IsNullOrEmpty(oldFilePath))
        {
            oldFilePath = Path.GetFullPath(oldFilePath);

            // Get redirected path.
            _redirectorController.Loading?.Invoke(oldFilePath);

            if (_redirector.TryRedirect(oldFilePath, out newFilePath))
            {
                string newPath = newFilePath;
                _redirectorController.Redirecting?.Invoke(oldFilePath, newPath);

                return true;
            }
        }

        newFilePath = oldFilePath;
        return false;
    }

    /* Mod loader interface. */
    public static void Enable() => _ntCreateFileHook.Enable();
    public static void Disable() => _ntCreateFileHook.Disable();
}
