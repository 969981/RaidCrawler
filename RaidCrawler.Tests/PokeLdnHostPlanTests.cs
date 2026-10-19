using RaidCrawler.Core.Structures;
using Xunit;

namespace RaidCrawler.Tests;

public class PokeLdnHostPlanTests
{
    private static readonly PokeLdnRaidSelection Target =
        new(0xBD13FB43, "violet", "kitakami", "standard", false, 25, 4, 3, 9);
    private static readonly RaidRewardEntry[] Rows = [
        new(1, 999, 0, 5), new(796, 20, 0, 0),
    ];

    [Fact]
    public void BuildsSelectedRaidAndEligibleRowsWithoutShellQuoting()
    {
        var argv = PokeLdnHostPlan.BuildArguments(Target, Rows,
            "/home/develop/pokemon/host pokemon.pk9", "4star", players: 2, seconds: 300);
        Assert.Contains("BD13FB43", argv);
        Assert.Contains("kitakami", argv);
        Assert.Contains("--violet", argv);
        Assert.Contains("/home/develop/pokemon/host pokemon.pk9", argv);
        Assert.Equal(2, argv.Count(a => a == "--raid-reward"));
        Assert.Contains("1:999:5", argv);
        Assert.Contains("796:20:0", argv);
    }

    [Fact]
    public void RejectsEventRaidsRatherThanSubstitutingWrongEncounter()
    {
        Assert.Throws<ArgumentException>(() => PokeLdnHostPlan.BuildArguments(
            Target with { IsEvent = true }, Rows, "/tmp/host.pk9", "4star"));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 4)]
    [InlineData(1, 6)]
    public void RejectsUnsupportedRewardConditionsOrRare(int item, int subject)
    {
        Assert.Throws<ArgumentException>(() => PokeLdnHostPlan.BuildArguments(
            Target, [new(item, 1, 1, subject)], "/tmp/host.pk9", "4star"));
    }

    [Fact]
    public void RejectsUnspecifiedLinuxFileAndTooManyPlayers()
    {
        Assert.Throws<ArgumentException>(() => PokeLdnHostPlan.BuildArguments(
            Target, Rows, "C:\\game\\host.pk9", "4star"));
        Assert.Throws<ArgumentException>(() => PokeLdnHostPlan.BuildArguments(
            Target, Rows, "/tmp/host.pk9", "4star", players: 4));
    }

    [Fact]
    public void OptionalIpHostIsExplicitOnly()
    {
        var standard = PokeLdnHostPlan.BuildArguments(Target, Rows, "/tmp/host.pk9", "4star");
        Assert.DoesNotContain("--ip-host", standard);
        var ipHost = PokeLdnHostPlan.BuildArguments(Target, Rows, "/tmp/host.pk9", "4star", ipHost: true);
        Assert.Contains("--ip-host", ipHost);
    }

    [Fact]
    public void All45RowsSupportedAnd46Rejected()
    {
        var all = Enumerable.Repeat(new RaidRewardEntry(1, 1, 0, 5), 45).ToArray();
        var arguments = PokeLdnHostPlan.BuildArguments(Target, all, "/tmp/host.pk9", "6star");
        Assert.Equal(45, arguments.Count(a => a == "--raid-reward"));
        Assert.Throws<ArgumentException>(() => PokeLdnHostPlan.BuildArguments(
            Target, [..all, new RaidRewardEntry(2, 1, 0, 5)], "/tmp/host.pk9", "6star"));
    }
}
