using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public class RaidRewardEditorTests
{
    [Fact]
    public void Accepts45RowsAndRoundTripsFields()
    {
        var rows = Enumerable.Range(0, RaidRewardEditor.MaxRows)
            .Select(i => new RaidRewardEntry(i + 1, 999, i % 2, i % 6)).ToArray();
        var bytes = RaidRewardEditor.Encode(rows);
        Assert.Equal(720, bytes.Length);
        Assert.Equal((byte)1, bytes[0]);
        Assert.Equal((byte)0xE7, bytes[4]);
        Assert.Equal((byte)0x03, bytes[5]);
        Assert.Equal((byte)5, bytes[5 * 16 + 12]);
        Assert.Equal(rows, RaidRewardEditor.Decode(bytes));
    }

    [Fact]
    public void ShortListsHaveZeroPadding()
    {
        var rows = new[] { new RaidRewardEntry(1, 999, 0, 5), new RaidRewardEntry(796, 12, 1, 0) };
        var bytes = RaidRewardEditor.Encode(rows);
        Assert.All(bytes.Skip(32), b => Assert.Equal((byte)0, b));
        Assert.Equal(rows, RaidRewardEditor.Decode(bytes));
    }

    [Fact]
    public void FortySixRowsAreRejected()
    {
        var rows = Enumerable.Repeat(new RaidRewardEntry(1, 1, 0, 0), 46).ToArray();
        Assert.Throws<ArgumentException>(() => RaidRewardEditor.Encode(rows));
    }

    [Theory]
    [InlineData(0, 1, 0, 0)]
    [InlineData(10000, 1, 0, 0)]
    [InlineData(20000, 1, 0, 0)]
    [InlineData(1, 0, 0, 0)]
    [InlineData(1, 1000, 0, 0)]
    [InlineData(1, 1, 2, 0)]
    [InlineData(1, 1, 0, 6)]
    public void InvalidFieldsAreRejected(int id, int quantity, int rare, int subject)
    {
        Assert.Throws<ArgumentException>(() =>
            RaidRewardEditor.Encode([new RaidRewardEntry(id, quantity, rare, subject)]));
    }

    [Fact]
    public void NonzeroRowAfterPaddingIsRejected()
    {
        var bytes = RaidRewardEditor.Encode([new RaidRewardEntry(1, 1, 0, 0)]);
        bytes[32] = 1;
        Assert.Throws<ArgumentException>(() => RaidRewardEditor.Decode(bytes));
    }

    [Fact]
    public void MaterialAndShardDisplayMarkersAreResolved()
    {
        var rows = RaidRewardEditor.FromCalculated(
            new (int, int, int)[] { (10000, 2, 0), (20000, 3, 5) },
            (int)PKHeX.Core.Species.Pikachu, (int)PKHeX.Core.MoveType.Fire);
        Assert.Equal(Rewards.GetMaterial((int)PKHeX.Core.Species.Pikachu), rows[0].ItemId);
        Assert.Equal(1863, rows[1].ItemId);
        Assert.Equal(5, rows[1].Subject);
    }
}
