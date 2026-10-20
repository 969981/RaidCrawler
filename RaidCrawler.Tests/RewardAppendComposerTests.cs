using System.Buffers.Binary;
using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public sealed class RewardAppendComposerTests
{
    private static byte[] Frame(int originalRows, bool bootstrap = false)
    {
        int offset = bootstrap ? 0x6B8 : 0;
        byte[] bytes = new byte[bootstrap ? 0xAA0 : 0x3E8];
        "RaidPoint_"u8.CopyTo(bytes.AsSpan(offset));
        // Non-reward fields contain recognizable sentinel, must stay untouched.
        bytes[offset + 0x20] = 0x5A;
        bytes[offset + 0x3D8] = 0x7B;
        if (bootstrap) bytes[0x20] = 0xA1;
        for (int i = 0; i < originalRows; i++)
        {
            int start = offset + 0xE4 + i * 16;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(start), (uint)(200 + i));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(start + 4), (uint)(1 + i));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(start + 8), i % 2 == 0 ? 1u : 0u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(start + 12), (uint)(i % 6));
        }
        return bytes;
    }

    private static CharmBonusProfile Profile(int count = 20, bool enabled = true) =>
        CharmBonusProfile.Create(enabled, Enumerable.Range(0, count)
            .Select(i => new CharmBonusItem(1200 + (i % 4), 999 - i)));

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 24)]
    [InlineData(false, 25)]
    [InlineData(true, 25)]
    public void Appends20WithAllOriginalBytesAndMetadataPreserved(bool bootstrap, int n)
    {
        var original = Frame(n, bootstrap);
        var before = original.ToArray();
        var result = bootstrap
            ? RewardAppendComposer.ComposeBootstrap(original, Profile())
            : RewardAppendComposer.ComposeRaidPoint(original, Profile());
        Assert.Equal(RewardAppendStatus.Applied, result.Status);
        Assert.Equal(20, result.AddedCount);
        Assert.Equal(n, result.OriginalCount);
        int offset = (bootstrap ? 0x6B8 : 0) + 0xE4;
        Assert.Equal(before.AsSpan(offset, n*16).ToArray(),
            result.Bytes.AsSpan(offset, n*16).ToArray());
        Assert.True(RaidPointOfflinePatch.UntouchedFieldsEqual(before,
            result.Bytes, bootstrap ? 0x6B8 : 0));
        Assert.Equal(5, BinaryPrimitives.ReadUInt32LittleEndian(
            result.Bytes.AsSpan(offset + n*16 + 12)));
        Assert.Equal(999, BinaryPrimitives.ReadUInt32LittleEndian(
            result.Bytes.AsSpan(offset + n*16 + 4)));
        Assert.Equal(before, original); // input is immutable
        Assert.All(RaidRewardEditor.Decode(result.Bytes.AsSpan(offset, 720))
            .Skip(n), row => Assert.Equal(5, row.Subject));
    }

    [Theory]
    [InlineData(26)]
    [InlineData(44)]
    [InlineData(45)]
    public void InsufficientCapacityNeverRemovesOriginalRow(int n)
    {
        var original = Frame(n);
        var composed = RewardAppendComposer.ComposeRaidPoint(original, Profile());
        Assert.Equal(RewardAppendStatus.InsufficientCapacity, composed.Status);
        Assert.Equal(0, composed.AddedCount);
        Assert.Equal(original, composed.Bytes);
        Assert.Equal(original, Frame(n));
    }

    [Fact]
    public void DisabledOrEmptyAlwaysPreservesCompleteFrame()
    {
        var original = Frame(25, true);
        var off = RewardAppendComposer.ComposeBootstrap(original, Profile(enabled:false));
        Assert.Equal(RewardAppendStatus.Disabled, off.Status);
        Assert.Equal(original, off.Bytes);
        var none = RewardAppendComposer.ComposeBootstrap(original, Profile(0));
        Assert.Equal(RewardAppendStatus.EmptyBonus, none.Status);
        Assert.Equal(original, none.Bytes);
    }

    [Fact]
    public void SparseOrNoncanonicalOriginalFailsClosedInsteadOfPacking()
    {
        var original = Frame(5);
        Array.Clear(original, 0xE4 + 2 * 16, 16);
        var result = RewardAppendComposer.ComposeRaidPoint(original, Profile(1));
        Assert.Equal(RewardAppendStatus.InvalidOriginalRows, result.Status);
        Assert.Equal(original, result.Bytes);

        var unexpected = Frame(3);
        BinaryPrimitives.WriteUInt32LittleEndian(
            unexpected.AsSpan(0xE4 + 12), 9u);
        var bad = RewardAppendComposer.ComposeRaidPoint(unexpected, Profile(1));
        Assert.Equal(RewardAppendStatus.InvalidOriginalRows, bad.Status);
        Assert.Equal(unexpected, bad.Bytes);
    }

    [Fact]
    public void RejectsWrongPacketAndBadMagicUnchanged()
    {
        var shortPacket = new byte[10];
        var wrong = RewardAppendComposer.ComposeBootstrap(shortPacket, Profile(1));
        Assert.Equal(RewardAppendStatus.InvalidFrame, wrong.Status);
        Assert.Equal(shortPacket, wrong.Bytes);
        var wrongMagic = new byte[0x3E8];
        var rejected = RewardAppendComposer.ComposeRaidPoint(wrongMagic, Profile(1));
        Assert.Equal(RewardAppendStatus.InvalidFrame, rejected.Status);
        Assert.Equal(wrongMagic, rejected.Bytes);
    }

    [Fact]
    public void DuplicateRowsRemainSeparateAndSubject4SequenceIsUntouched()
    {
        var original = Frame(3);
        var extras = CharmBonusProfile.Create(true,
            [new CharmBonusItem(800, 7), new CharmBonusItem(800, 7)]);
        var composed = RewardAppendComposer.ComposeRaidPoint(original, extras);
        Assert.Equal(RewardAppendStatus.Applied, composed.Status);
        var all = RaidRewardEditor.Decode(composed.Bytes.AsSpan(0xE4, 720));
        Assert.Equal(5, all.Count);
        Assert.Equal(2, all[2].Subject); // original index 2
        Assert.Equal(800, all[3].ItemId);
        Assert.Equal(all[3], all[4]);
        Assert.Equal(5, all[4].Subject);
    }

    [Fact]
    public void LogicalArrayHasNoUnproven45CapAndNeverMutatesCaller()
    {
        var original = Enumerable.Range(0, 42).Select(i =>
            new RaidRewardEntry(101+i, 1, 0, i%6)).ToArray();
        var bonus = CharmBonusProfile.Create(true,
            [new CharmBonusItem(800, 5), new CharmBonusItem(801, 6)]);
        var output = RewardAppendComposer.ComposeLogical(original, bonus);
        Assert.Equal(44, output.Count);
        Assert.True(original.SequenceEqual(output.Take(42)));
        Assert.Equal(5, output[^1].Subject);
        Assert.Equal(42, original.Length);
        Assert.NotSame(original, output);
    }
}
