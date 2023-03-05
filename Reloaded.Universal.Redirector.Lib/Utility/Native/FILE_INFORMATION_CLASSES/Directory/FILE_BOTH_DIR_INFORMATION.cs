using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Reloaded.Universal.Redirector.Lib.Utility.Native.FileDirectoryInformationDerivativeExtensions;
// ReSharper disable InconsistentNaming
#pragma warning disable CS1591

// ReSharper disable once CheckNamespace
namespace Reloaded.Universal.Redirector.Lib.Utility.Native;

public partial class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct FILE_BOTH_DIR_INFORMATION : IFileDirectoryInformationDerivative
    {
        public uint NextEntryOffset;
        public uint FileIndex;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public long EndOfFile;
        public long AllocationSize;
        public FileAttributes FileAttributes;
        public uint FileNameLength;
        public uint EaSize;
        public byte ShortNameLength;

        // Short name
        public fixed char ShortName[12];

        /// <inheritdoc />
        public int GetNextEntryOffset() => (int)NextEntryOffset;
        /// <inheritdoc />
        public FileAttributes GetFileAttributes() => FileAttributes;

        /// <inheritdoc />
        public string GetFileName(void* thisPtr)
        {
            var casted = (FILE_BOTH_DIR_INFORMATION*)thisPtr;
            return Marshal.PtrToStringUni((nint)(casted + 1), (int)FileNameLength / 2);
        }

        /// <inheritdoc />
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryPopulate(void* thisPtr, int length, nint handle)
        {
            var thisItem = (FILE_BOTH_DIR_INFORMATION*)thisPtr;
            var result = NtQueryInformationFileHelper(handle);

            if (!SufficientSize<FILE_BOTH_DIR_INFORMATION>(length, result->NameInformation.FileNameLength))
                return false;

            thisItem->CreationTime = result->BasicInformation.CreationTime;
            thisItem->LastAccessTime = result->BasicInformation.LastAccessTime;
            thisItem->LastWriteTime = result->BasicInformation.LastWriteTime;
            thisItem->ChangeTime = result->BasicInformation.ChangeTime;
            thisItem->EndOfFile = result->StandardInformation.EndOfFile;
            thisItem->AllocationSize = result->StandardInformation.AllocationSize;
            thisItem->FileAttributes = result->BasicInformation.FileAttributes;
            thisItem->FileNameLength = result->NameInformation.FileNameLength;
            thisItem->EaSize = result->EaInformation.EaSize;
            thisItem->NextEntryOffset = (uint)(sizeof(FILE_BOTH_DIR_INFORMATION) + thisItem->FileNameLength * sizeof(char));

            // TODO: Short names are not supported [for performance reasons]
            // Unknowns
            thisItem->ShortNameLength = 0;
            thisItem->FileIndex = 0;
            
            CopyString((char*)(result + 1), thisItem, thisItem->FileNameLength);
            return true;
        }
    }
}