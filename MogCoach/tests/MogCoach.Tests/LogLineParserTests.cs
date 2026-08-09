using MogCoach.Core.Model;
using MogCoach.Ingest.Iinact;
using Xunit;

namespace MogCoach.Tests;

public class LogLineParserTests
{
    [Fact]
    public void Parses_death_line_and_flags_self()
    {
        // 25|timestamp|targetId|targetName|sourceId|sourceName|checksum
        var line = "25|2026-01-01T12:00:30.0000000+00:00|10001234|Hero Adventurer|400009C0|Some Boss|abcd";
        var ok = IinactLogLineParser.TryParse(line, "10001234", "Hero Adventurer", out var ev);

        Assert.True(ok);
        Assert.Equal(CombatEventType.Death, ev.Type);
        Assert.True(ev.IsSelf);
        Assert.Equal("Some Boss", ev.SourceName);
    }

    [Fact]
    public void Parses_starts_casting_as_cast()
    {
        // 20|timestamp|srcId|srcName|abilityId|abilityName|tgtId|tgtName|castTime|...
        var line = "20|2026-01-01T12:00:05.0000000+00:00|10001234|Hero Adventurer|1CE8|Fell Cleave|400009C0|Boss|1.30|...|z";
        var ok = IinactLogLineParser.TryParse(line, "10001234", "Hero Adventurer", out var ev);

        Assert.True(ok);
        Assert.Equal(CombatEventType.Cast, ev.Type);
        Assert.Equal("Fell Cleave", ev.AbilityName);
        Assert.True(ev.IsSelf);
    }

    [Fact]
    public void Ignores_unmodelled_line_types()
    {
        var line = "99|2026-01-01T12:00:05.0000000+00:00|whatever|nope";
        Assert.False(IinactLogLineParser.TryParse(line, null, null, out _));
    }

    [Fact]
    public void Maps_director_wipe_command()
    {
        // 33|timestamp|instanceId|command|...
        var line = $"33|2026-01-01T12:05:00.0000000+00:00|8003759B|{IinactLogLineParser.DirectorCommand.Wipe}|00|00|00|00|xx";
        var ok = IinactLogLineParser.TryParse(line, null, null, out var ev);

        Assert.True(ok);
        Assert.Equal(CombatEventType.Wipe, ev.Type);
    }
}
