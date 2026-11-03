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

public class StateTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "taa-" + Guid.NewGuid());
    State S => new(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void Mode_defaults_to_Off_and_round_trips()
    {
        Assert.Equal(Mode.Off, S.ReadMode());
        S.WriteMode(Mode.Auto);
        Assert.Equal(Mode.Auto, S.ReadMode());
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("7")]
    public void Mode_invalid_file_reads_Off(string content)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "mode.txt"), content);
        Assert.Equal(Mode.Off, S.ReadMode());
    }

    [Fact]
    public void Touch_then_Remove_toggles_busy()
    {
        var now = DateTime.UtcNow;
        Assert.False(S.AnyBusy(now));
        S.Touch("abc-123");
        Assert.True(S.AnyBusy(now));
        S.Remove("abc-123");
        Assert.False(S.AnyBusy(now));
    }

    [Fact]
    public void Remove_without_dir_does_not_throw() => S.Remove("nope");

    [Fact]
    public void Stale_marker_is_ignored_and_deleted()
    {
        S.Touch("old");
        var file = Path.Combine(S.BusyDir, "old");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-13));
        Assert.False(S.AnyBusy(DateTime.UtcNow));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Session_id_is_sanitized_to_stay_inside_busy_dir()
    {
        S.Touch("../../evil");
        Assert.True(File.Exists(Path.Combine(S.BusyDir, "evil")));
        S.Touch(null);
        Assert.True(File.Exists(Path.Combine(S.BusyDir, "unknown")));
    }
}

public class HookTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "taa-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    static StringReader Json(string id) =>
        new($$"""{"session_id":"{{id}}","hook_event_name":"UserPromptSubmit"}""");

    [Fact]
    public void Busy_then_idle_round_trips()
    {
        var s = new State(root);
        Assert.Equal(0, Hook.Run("busy", Json("s1"), s));
        Assert.True(s.AnyBusy(DateTime.UtcNow));
        Assert.Equal(0, Hook.Run("idle", Json("s1"), s));
        Assert.False(s.AnyBusy(DateTime.UtcNow));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("")]
    public void Bad_input_returns_0_and_does_nothing(string stdin)
    {
        var s = new State(root);
        Assert.Equal(0, Hook.Run("busy", new StringReader(stdin), s));
        Assert.False(s.AnyBusy(DateTime.UtcNow));
    }

    [Fact]
    public void Unknown_verb_returns_0() =>
        Assert.Equal(0, Hook.Run("bogus", Json("s1"), new State(root)));
}

public class InstallerTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "taa-" + Guid.NewGuid());
    string Settings => Path.Combine(dir, "settings.json");
    const string Exe = @"C:\Users\A B\AppData\Local\TeamsAlwaysActive\app\TeamsAlwaysActive.exe";

    public InstallerTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, true);

    JsonObject Read() => JsonNode.Parse(File.ReadAllText(Settings))!.AsObject();

    [Fact]
    public void Merge_into_missing_file_adds_four_events()
    {
        Installer.MergeHooks(Settings, Exe);
        var hooks = Read()["hooks"]!.AsObject();
        Assert.Equal(
            "\"C:/Users/A B/AppData/Local/TeamsAlwaysActive/app/TeamsAlwaysActive.exe\" --hook busy",
            (string?)hooks["UserPromptSubmit"]![0]!["hooks"]![0]!["command"]);
        Assert.Equal("command", (string?)hooks["Stop"]![0]!["hooks"]![0]!["type"]);
        Assert.EndsWith("--hook idle", (string?)hooks["SessionEnd"]![0]!["hooks"]![0]!["command"]);
        Assert.Equal("idle_prompt", (string?)hooks["Notification"]![0]!["matcher"]);
        Assert.EndsWith("--hook idle", (string?)hooks["Notification"]![0]!["hooks"]![0]!["command"]);
    }

    [Fact]
    public void Merge_does_not_escape_existing_content()
    {
        File.WriteAllText(Settings, """{ "permissions": { "allow": ["Bash(a && b 'c' + <d>)"] } }""");
        Installer.MergeHooks(Settings, Exe);
        Assert.Contains("Bash(a && b 'c' + <d>)", File.ReadAllText(Settings));
    }

    [Fact]
    public void Merge_preserves_other_settings_and_hooks_and_is_idempotent()
    {
        File.WriteAllText(Settings, """
            { "model": "opus", "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "other.exe" } ] } ] } }
            """);
        Installer.MergeHooks(Settings, Exe);
        Installer.MergeHooks(Settings, Exe);
        var root = Read();
        Assert.Equal("opus", (string?)root["model"]);
        var stop = root["hooks"]!["Stop"]!.AsArray();
        Assert.Equal(2, stop.Count);
        Assert.Equal("other.exe", (string?)stop[0]!["hooks"]![0]!["command"]);
        Assert.Single(root["hooks"]!["UserPromptSubmit"]!.AsArray());
        Assert.True(File.Exists(Settings + ".bak"));
    }

    [Fact]
    public void Remove_strips_only_ours()
    {
        File.WriteAllText(Settings, """
            { "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "other.exe" } ] } ] } }
            """);
        Installer.MergeHooks(Settings, Exe);
        Installer.RemoveHooks(Settings);
        var hooks = Read()["hooks"]!.AsObject();
        Assert.Single(hooks["Stop"]!.AsArray());
        Assert.False(hooks.ContainsKey("UserPromptSubmit"));
        Assert.False(hooks.ContainsKey("SessionEnd"));
        Assert.False(hooks.ContainsKey("Notification"));
    }

    [Fact]
    public void Merge_refuses_non_object_settings()
    {
        File.WriteAllText(Settings, "[]");
        Assert.ThrowsAny<Exception>(() => Installer.MergeHooks(Settings, Exe));
        Assert.Equal("[]", File.ReadAllText(Settings));
    }
}
