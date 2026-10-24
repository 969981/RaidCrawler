using System.Buffers.Binary;
using RaidCrawler.Core.LiveRewardV4;
using Xunit;

namespace RaidCrawler.Tests.LiveRewardV4;

public sealed class MailboxV4LocatorTests
{
    private const string Manifest = """
    {
      "schema":"raidcrawler.live-reward.mailbox-locator.v4",
      "title_id":"01008F6008C5E000",
      "version_expected":"4.0.0",
      "build_id_expected":"709bfd66115298640155fcc4979dba151c7cc79a",
      "hook_rva":"0xE32984",
      "inline_entry_callback_pointer_offset":16,
      "callback_to_mailbox_delta":40960,
      "subsdk9_sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      "read_only":true,
      "native_write_capability":false
    }
    """;

    [Fact]
    public void AcceptsOnlyReadonlyViolet400Manifest()
    {
        var data = MailboxV4Locator.ParseManifest(Manifest);
        Assert.Equal(40960, data.CallbackToMailboxDelta);
        Assert.Equal(new string('a', 64), data.NsoSha256);
        Assert.Throws<InvalidDataException>(() =>
            MailboxV4Locator.ParseManifest(Manifest.Replace("\"read_only\":true", "\"read_only\":false")));
        Assert.Throws<InvalidDataException>(() =>
            MailboxV4Locator.ParseManifest(Manifest.Replace("0xE32984", "0xE32980")));
        Assert.Throws<InvalidDataException>(() =>
            MailboxV4Locator.ParseManifest(Manifest.Replace("01008F6008C5E000", "0100A3D008C5C000")));
    }

    [Fact]
    public void SupportsShortBranchAndLongBranchPatterns()
    {
        var code = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(code, 0x14000004);
        Assert.Equal(0x710000010UL,
            MailboxV4Locator.EntryFromHook(0x710000000UL, code));
        BinaryPrimitives.WriteUInt32LittleEndian(code, 0x58000051);
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(4), 0xD61F0220);
        BinaryPrimitives.WriteUInt64LittleEndian(code.AsSpan(8), 0x720010000UL);
        Assert.Equal(0x720010000UL,
            MailboxV4Locator.EntryFromHook(0x710000000UL, code));
    }

    [Fact]
    public void ResolvesCallbackAndPairedMailbox()
    {
        var code = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(4), 0x94000004);
        BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(12), 0x14000004);
        BinaryPrimitives.WriteUInt64LittleEndian(code.AsSpan(16), 0x710010000UL);
        ulong callback = MailboxV4Locator.CallbackFromEntry(0x720000000UL, code);
        Assert.Equal(0x710010000UL, callback);
        Assert.Equal(0x71001A000UL,
            MailboxV4Locator.AddressFromCallback(callback, 40960));
    }

    [Fact]
    public void UnknownHooksAreRejectedWithoutMemoryScan()
    {
        Assert.Throws<InvalidDataException>(() =>
            MailboxV4Locator.EntryFromHook(0x710000000UL, new byte[24]));
        Assert.Throws<InvalidDataException>(() =>
            MailboxV4Locator.CallbackFromEntry(0x720000000UL, new byte[24]));
        Assert.Throws<InvalidDataException>(() =>
            MailboxV4Locator.AddressFromCallback(0x710000000UL, long.MaxValue));
    }
}
