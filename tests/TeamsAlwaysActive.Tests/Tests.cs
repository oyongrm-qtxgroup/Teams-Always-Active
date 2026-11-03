using System.Text.Json.Nodes; // used by InstallerTests (Task 4)
using TeamsAlwaysActive;
using Xunit;

public class DecisionTests
{
    [Theory]
    [InlineData(Mode.Off, false, false)]
    [InlineData(Mode.Off, true, false)]
    [InlineData(Mode.On, false, true)]
    [InlineData(Mode.On, true, true)]
    [InlineData(Mode.Auto, false, false)]
    [InlineData(Mode.Auto, true, true)]
    public void WantActive_truth_table(Mode mode, bool busy, bool expected) =>
        Assert.Equal(expected, Decision.WantActive(mode, busy));

    [Fact]
    public void ShouldNudge_only_when_wanted_and_idle_past_threshold()
    {
        Assert.False(Decision.ShouldNudge(true, TimeSpan.FromMinutes(3.9)));
        Assert.True(Decision.ShouldNudge(true, TimeSpan.FromMinutes(4)));
        Assert.False(Decision.ShouldNudge(false, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void IsFresh_cuts_off_at_12_hours()
    {
        var now = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(Decision.IsFresh(now.AddHours(-11.9), now));
        Assert.False(Decision.IsFresh(now.AddHours(-12), now));
    }
}
