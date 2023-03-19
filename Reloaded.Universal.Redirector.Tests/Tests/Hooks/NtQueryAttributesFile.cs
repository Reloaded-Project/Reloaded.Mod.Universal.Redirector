using Reloaded.Universal.Redirector.Tests.Tests.Hooks.Base;
using static Reloaded.Universal.Redirector.Tests.Utility.WinApiHelpers;

namespace Reloaded.Universal.Redirector.Tests.Tests.Hooks;

public class NtQueryAttributesFile : BaseHookTest
{
    [Fact]
    public void NtQueryAttributesFile_Baseline()
    {
        Api.Enable();
        
        // Setup.
        var notExpected = NtQueryAttributesFileHelper(GetBaseFilePrefixed("usvfs-poem.txt"));   // original
        var expected = NtQueryAttributesFileHelper(GetOverride1FilePrefixed("usvfs-poem.txt")); // expected
        
        // Attach Overlay 1
        Api.AddRedirectFolder(GetOverride1Path(), GetBasePath());
        var actual = NtQueryAttributesFileHelper(GetBaseFilePrefixed("usvfs-poem.txt")); // redirected
        
        Assert.Equal(expected, actual);
        Assert.NotEqual(notExpected, actual);
        
        // Disable API.
        Api.Disable();
        actual = NtQueryAttributesFileHelper(GetBaseFilePrefixed("usvfs-poem.txt")); // no longer redirected
        Assert.Equal(notExpected, actual);
        Api.Enable();
    }
}