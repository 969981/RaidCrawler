using System.Buffers.Binary;
using RaidCrawler.Core.Host;
using Xunit;

namespace RaidCrawler.Tests.Host;

public class HostProbeLocatorTests
{
    private const string Manifest = """
    {
      "schema":"raidcrawler.host.mailbox-locator.v1",
      "title_id":"01008F6008C5E000",
      "build_id_expected":"709bfd66115298640155fcc4979dba151c7cc79a",
      "main_hook_rva":"0xE32984",
      "jit_callback_pointer_offset":16,
      "callback_to_mailbox_delta":83536
    }
    """;

    [Fact]
    public void LoadsPairedBuildManifest()
    {
        Assert.Equal(83536, HostProbeLocator.ParseManifest(Manifest).CallbackToMailboxDelta);
        Assert.Throws<InvalidDataException>(() =>
            HostProbeLocator.ParseManifest(Manifest.Replace("0xE32984", "0xE32980")));
    }

    [Fact]
    public void ResolvesShortBranchToJitEntry()
    {
        byte[] code = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(code, 0x14000004); // +0x10
        Assert.Equal(0x710000010UL, HostProbeLocator.ParseEntryTarget(0x710000000UL, code));
    }

    [Fact]
    public void ResolvesLongBranchToJitEntry()
    {
        byte[] code = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(code, 0x58000051);
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(4), 0xD61F0220);
        BinaryPrimitives.WriteUInt64LittleEndian(code.AsSpan(8), 0x720010000UL);
        Assert.Equal(0x720010000UL, HostProbeLocator.ParseEntryTarget(0x710000000UL, code));
    }

    [Fact]
    public void VerifiesExlaunchEntryAndComputesMailbox()
    {
        byte[] code = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(4), 0x94000004); // BL
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(12), 0x14000004); // B
        BinaryPrimitives.WriteUInt64LittleEndian(code.AsSpan(16), 0x710010000UL);
        ulong callback = HostProbeLocator.ParseCallback(0x720000000UL, code);
        Assert.Equal(0x710010000UL, callback);
        Assert.Equal(0x710024650UL, HostProbeLocator.ResolveMailbox(callback, 83536));
    }

    [Fact]
    public void RejectsUnknownHookAndCorruptEntry()
    {
        Assert.Throws<InvalidDataException>(() => HostProbeLocator.ParseEntryTarget(0x710000000UL, new byte[24]));
        Assert.Throws<InvalidDataException>(() => HostProbeLocator.ParseCallback(0x720000000UL, new byte[24]));
    }
}
