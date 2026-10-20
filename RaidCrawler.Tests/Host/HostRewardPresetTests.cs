using RaidCrawler.Core.Host;
using Xunit;

namespace RaidCrawler.Tests.Host;

public sealed class HostRewardPresetTests
{
    private static readonly uint[] ExpectedIds =
    [
        492, 493, 494, 495, 496, 497, 498, 499,
        851, 576, 5, 795, 796, 1, 50, 1128, 581,
        1907, 1908, 1906, 1904, 1905, 1606, 2137, 2479
    ];

    [Fact]
    public void DefaultHas25ItemsExactOrderAndQuantity999()
    {
        var config = HostRewardPreset.CreateDefault();
        Assert.Equal(HostRewardPreset.Schema, config.Schema);
        Assert.Equal(5u, config.SubjectType);
        Assert.Equal(25, config.Items.Length);
        Assert.Equal(ExpectedIds, config.Items.Select(i => i.ItemId).ToArray());
        Assert.All(config.Items, i => Assert.Equal(999u, i.Quantity));
        Assert.Equal("净空年糕", config.Items[24].Name);
        Assert.Equal("锁财灵的金币", config.Items[23].Name);
        Assert.Equal("究极球", config.Items[8].Name);
        Assert.Null(HostRewardPreset.Validate(config));
        Assert.All(HostRewardPreset.BuildRows(config), r =>
        {
            Assert.Equal(999u, r.Quantity);
            Assert.Equal(0u, r.RareFlag);
            Assert.Equal(5u, r.SubjectType);
        });
    }

    [Fact]
    public void JsonRoundTripAndEditingDoNotMutateTheDefaults()
    {
        var config = HostRewardPreset.CreateDefault();
        config.Items[0] = new("自定义A", 42, 27);
        string json = HostRewardPreset.ToJson(config);
        var recovered = HostRewardPreset.FromJson(json);
        Assert.Equal(config, recovered with { Items = config.Items });
        Assert.Equal(42u, recovered.Items[0].ItemId);
        Assert.Equal(492u, HostRewardPreset.CreateDefault().Items[0].ItemId);
    }

    [Fact]
    public void RejectsMoreThan28DuplicateIdsOutOfRangeAndWrongSubject()
    {
        var original = HostRewardPreset.CreateDefault();
        var max28 = original with
        {
            Items = [.. original.Items,
                new("A", 7000, 1), new("B", 7001, 2), new("C", 7002, 3)]
        };
        Assert.Null(HostRewardPreset.Validate(max28));
        Assert.Equal("ITEM_COUNT_MUST_BE_1_TO_28",
            HostRewardPreset.Validate(max28 with { Items = [.. max28.Items, new("D", 7003, 1)] }));
        Assert.Equal("DUPLICATE_ITEM_ID",
            HostRewardPreset.Validate(original with { Items = [.. original.Items, new("Dup", 492, 1)] }));
        Assert.Equal("INVALID_ITEM_ID",
            HostRewardPreset.Validate(original with { Items = [new("A", 0, 1)] }));
        Assert.Equal("INVALID_ITEM_QUANTITY",
            HostRewardPreset.Validate(original with { Items = [new("A", 492, 1000)] }));
        Assert.Equal("SUBJECT_MUST_BE_FIVE",
            HostRewardPreset.Validate(original with { SubjectType = 0 }));
        Assert.Throws<InvalidDataException>(() =>
            HostRewardPreset.FromJson("{\"Schema\":\"evil\",\"SubjectType\":5,\"Items\":[]}"));
    }

    private static (byte[] Point, byte[] Bootstrap) MakeSource(int occupied)
    {
        var source = new byte[RaidPointLayout.RaidPointLength];
        source[0xE0] = 0x11;
        source[RaidPointLayout.SummaryStart] = 0x67;
        for (int i = 0; i < occupied; i++)
            new RewardRow((uint)(1000 + i), 1, i == 0 ? 1u : 0u, i % 6u)
                .Write(source.AsSpan(RaidPointLayout.RewardsInRaidPoint + i * 16, 16));
        var wire = new byte[RaidPointLayout.BootstrapLength];
        wire[0] = 0xFF;
        source.CopyTo(wire, RaidPointLayout.RaidPointInBootstrap);
        return (source, wire);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(19)]
    [InlineData(20)]
    public void Default25AppendsOnlyWhen45SlotsHaveEnoughSpace(int originalRows)
    {
        var (source, wire) = MakeSource(originalRows);
        byte[] original = wire.ToArray();
        var plan = HostRewardPreset.Plan(HostRewardPreset.CreateDefault(), source, wire, id => true);
        Assert.True(plan.Accepted, plan.Reason);
        Assert.Equal(originalRows, plan.OriginalCount);
        Assert.Equal(25, plan.AddedCount);
        int start = RaidPointLayout.RewardsInBootstrap + originalRows * 16;
        Assert.Equal(original.AsSpan(0, start).ToArray(),
            plan.CandidateBootstrap.AsSpan(0, start).ToArray());
        Assert.Equal(original.AsSpan(start + 25 * 16).ToArray(),
            plan.CandidateBootstrap.AsSpan(start + 25 * 16).ToArray());
        Assert.Equal(wire, original);
        Assert.Equal(25, Enumerable.Range(0, 25).Count(i =>
            RewardRow.Read(plan.CandidateBootstrap.AsSpan(start + i * 16, 16))
                == HostRewardPreset.BuildRows(HostRewardPreset.CreateDefault())[i]));
    }

    [Fact]
    public void Default25Rejects21OriginalRowsAndCustom28RequiresAtMost17()
    {
        var (source, wire) = MakeSource(21);
        byte[] unchanged = wire.ToArray();
        var no = HostRewardPreset.Plan(HostRewardPreset.CreateDefault(), source, wire, _ => true);
        Assert.Equal("INSUFFICIENT_REWARD_SLOTS", no.Reason);
        Assert.Equal(unchanged, no.CandidateBootstrap);

        var config = HostRewardPreset.CreateDefault() with
        {
            Items = [.. HostRewardPreset.CreateDefault().Items,
                new("A", 7000, 1), new("B", 7001, 2), new("C", 7002, 3)]
        };
        var (okSource, okWire) = MakeSource(17);
        Assert.True(HostRewardPreset.Plan(config, okSource, okWire, _ => true).Accepted);
        var (badSource, badWire) = MakeSource(18);
        Assert.Equal("INSUFFICIENT_REWARD_SLOTS",
            HostRewardPreset.Plan(config, badSource, badWire, _ => true).Reason);
    }

    [Fact]
    public void RejectsUnavailableCustomBagItemWithoutModifyingOutgoingBootstrap()
    {
        var (source, wire) = MakeSource(0);
        var plan = HostRewardPreset.Plan(HostRewardPreset.CreateDefault(), source, wire,
            id => id != 2479);
        Assert.False(plan.Accepted);
        Assert.Equal("ITEM_NOT_ALLOWLISTED", plan.Reason);
        Assert.Equal(wire, plan.CandidateBootstrap);
    }
}
