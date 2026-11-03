# Teams Always Active Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows tray app with Off / On / Auto modes that keeps Teams presence Available by sending an F15 key press when the user has been idle 4+ minutes; Auto mode is driven by Claude Code hooks.

**Architecture:** One .NET 10 WinForms exe. No args → single-instance tray app with a 30 s timer. `--hook busy|idle` → reads Claude Code hook JSON from stdin and touches/deletes a per-session marker file. `--install` / `--uninstall` → merges hooks into `~/.claude/settings.json` and sets the HKCU Run key. Pure logic (`Decision`, `State`, `Hook`, `Installer`) is unit-tested; Win32 + tray is verified manually.

**Tech Stack:** .NET 10 SDK (installed: 10.0.201), WinForms, System.Text.Json, xUnit (from `dotnet new xunit`).

Spec: [2026-11-03-teams-always-active-design.md](../specs/2026-11-03-teams-always-active-design.md)

## Global Constraints

- Target `net10.0-windows`, `UseWindowsForms`, `Nullable` enabled, `ImplicitUsings` enabled.
- Data root: `%LOCALAPPDATA%\TeamsAlwaysActive\` — `mode.txt`, `busy\<session_id>`.
- Installed exe: `%LOCALAPPDATA%\TeamsAlwaysActive\app\TeamsAlwaysActive.exe`.
- Idle threshold 4 min, timer 30 s, stale marker cutoff **12 h**.
- Synthetic input: F15 (VK `0x7E`) down+up via `SendInput`. Never sent when idle < 4 min.
- Hook mode must never fail Claude: swallow all exceptions, exit 0, write nothing to stdout.
- Hooks: `UserPromptSubmit` → `--hook busy`; `Stop`, `SessionEnd` → `--hook idle`. Command uses forward slashes and a quoted exe path (path contains spaces; Claude Code may run hooks through Git Bash).
- `settings.json` is merged, never overwritten wholesale; a `.bak` copy is written before each save.
- Repo is PUBLIC on GitHub — commit nothing personal.
- Use `System.Windows.Forms.Timer` fully qualified (`Timer` is ambiguous with ImplicitUsings).

## File Structure

```
.gitignore                                    dotnet gitignore
src/TeamsAlwaysActive/TeamsAlwaysActive.csproj
src/TeamsAlwaysActive/Decision.cs             Mode enum + pure decision rules
src/TeamsAlwaysActive/State.cs                mode.txt + busy markers on disk
src/TeamsAlwaysActive/Hook.cs                 --hook entry (stdin JSON → State)
src/TeamsAlwaysActive/Installer.cs            settings.json hook merge/remove + Run key
src/TeamsAlwaysActive/Native.cs               GetLastInputInfo + SendInput F15
src/TeamsAlwaysActive/TrayApp.cs              NotifyIcon, menu, timer
src/TeamsAlwaysActive/Program.cs              arg routing, single-instance mutex
tests/TeamsAlwaysActive.Tests/TeamsAlwaysActive.Tests.csproj
tests/TeamsAlwaysActive.Tests/Tests.cs        all unit tests (one file)
README.md                                     usage + install
```

All commands run from the repo root in PowerShell.

---

### Task 1: Scaffold projects + Decision rules

**Files:**
- Create: `.gitignore`, `src/TeamsAlwaysActive/TeamsAlwaysActive.csproj`, `src/TeamsAlwaysActive/Program.cs` (compile stub, replaced in Task 5), `src/TeamsAlwaysActive/Decision.cs`
- Create: `tests/TeamsAlwaysActive.Tests/TeamsAlwaysActive.Tests.csproj`, `tests/TeamsAlwaysActive.Tests/Tests.cs`

**Interfaces:**
- Produces: `public enum Mode { Off, On, Auto }`; `public static class Decision` with `IdleThreshold` (4 min), `StaleAfter` (12 h), `bool WantActive(Mode mode, bool claudeBusy)`, `bool ShouldNudge(bool wantActive, TimeSpan idle)`, `bool IsFresh(DateTime lastWriteUtc, DateTime nowUtc)`. Namespace `TeamsAlwaysActive`.

- [ ] **Step 1: Scaffold**

```powershell
dotnet new gitignore
New-Item -ItemType Directory -Force src/TeamsAlwaysActive | Out-Null
dotnet new xunit -o tests/TeamsAlwaysActive.Tests --framework net10.0
Remove-Item tests/TeamsAlwaysActive.Tests/UnitTest1.cs
```

Write `src/TeamsAlwaysActive/TeamsAlwaysActive.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

Write `src/TeamsAlwaysActive/Program.cs` (stub so the exe compiles; Task 5 replaces it):

```csharp
namespace TeamsAlwaysActive;

static class Program
{
    [STAThread]
    static int Main(string[] args) => 0;
}
```

In `tests/TeamsAlwaysActive.Tests/TeamsAlwaysActive.Tests.csproj`: change `<TargetFramework>net10.0</TargetFramework>` to `<TargetFramework>net10.0-windows</TargetFramework>`, add `<UseWindowsForms>true</UseWindowsForms>` to the same `PropertyGroup`, and add:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\TeamsAlwaysActive\TeamsAlwaysActive.csproj" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing tests**

`tests/TeamsAlwaysActive.Tests/Tests.cs`:

```csharp
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
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/TeamsAlwaysActive.Tests`
Expected: build FAIL — `The name 'Decision' does not exist` / `The type or namespace name 'Mode' could not be found`.

- [ ] **Step 4: Implement**

`src/TeamsAlwaysActive/Decision.cs`:

```csharp
namespace TeamsAlwaysActive;

public enum Mode { Off, On, Auto }

public static class Decision
{
    public static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(4);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(12);

    public static bool WantActive(Mode mode, bool claudeBusy) =>
        mode == Mode.On || (mode == Mode.Auto && claudeBusy);

    public static bool ShouldNudge(bool wantActive, TimeSpan idle) =>
        wantActive && idle >= IdleThreshold;

    public static bool IsFresh(DateTime lastWriteUtc, DateTime nowUtc) =>
        nowUtc - lastWriteUtc < StaleAfter;
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/TeamsAlwaysActive.Tests`
Expected: `Passed! - Failed: 0, Passed: 8`

- [ ] **Step 6: Commit**

```powershell
git add .gitignore src tests
git commit -m "feat: scaffold projects and add decision rules"
```

---

### Task 2: State (mode file + busy markers)

**Files:**
- Create: `src/TeamsAlwaysActive/State.cs`
- Modify: `tests/TeamsAlwaysActive.Tests/Tests.cs` (append class)

**Interfaces:**
- Consumes: `Mode`, `Decision.IsFresh`
- Produces: `public sealed class State(string root)` with `Mode ReadMode()`, `void WriteMode(Mode mode)`, `void Touch(string? sessionId)`, `void Remove(string? sessionId)`, `bool AnyBusy(DateTime nowUtc)`, `string BusyDir { get; }`.

- [ ] **Step 1: Write the failing tests** — append to `Tests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/TeamsAlwaysActive.Tests`
Expected: build FAIL — `The type or namespace name 'State' could not be found`.

- [ ] **Step 3: Implement** — `src/TeamsAlwaysActive/State.cs`:

```csharp
namespace TeamsAlwaysActive;

public sealed class State(string root)
{
    public string BusyDir => Path.Combine(root, "busy");
    string ModeFile => Path.Combine(root, "mode.txt");

    public Mode ReadMode()
    {
        try
        {
            return Enum.TryParse(File.ReadAllText(ModeFile).Trim(), out Mode m) && Enum.IsDefined(m) ? m : Mode.Off;
        }
        catch { return Mode.Off; }
    }

    public void WriteMode(Mode mode)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(ModeFile, mode.ToString());
    }

    public void Touch(string? sessionId)
    {
        Directory.CreateDirectory(BusyDir);
        File.WriteAllText(MarkerPath(sessionId), "");
    }

    public void Remove(string? sessionId)
    {
        var path = MarkerPath(sessionId);
        if (File.Exists(path)) File.Delete(path);
    }

    public bool AnyBusy(DateTime nowUtc)
    {
        if (!Directory.Exists(BusyDir)) return false;
        bool busy = false;
        foreach (var file in Directory.EnumerateFiles(BusyDir))
        {
            if (Decision.IsFresh(File.GetLastWriteTimeUtc(file), nowUtc)) busy = true;
            else try { File.Delete(file); } catch { } // ponytail: stale from a crash; retry next tick
        }
        return busy;
    }

    string MarkerPath(string? sessionId)
    {
        var safe = string.Concat((sessionId ?? "").Where(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
        return Path.Combine(BusyDir, safe.Length > 0 ? safe : "unknown");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/TeamsAlwaysActive.Tests`
Expected: `Passed! - Failed: 0, Passed: 15`

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "feat: persist mode and Claude busy markers"
```

---

### Task 3: Hook mode

**Files:**
- Create: `src/TeamsAlwaysActive/Hook.cs`
- Modify: `tests/TeamsAlwaysActive.Tests/Tests.cs` (append class)

**Interfaces:**
- Consumes: `State.Touch`, `State.Remove`, `State.AnyBusy`
- Produces: `public static class Hook` with `int Run(string verb, TextReader input, State state)` — always returns 0.

- [ ] **Step 1: Write the failing tests** — append to `Tests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/TeamsAlwaysActive.Tests`
Expected: build FAIL — `The name 'Hook' does not exist`.

- [ ] **Step 3: Implement** — `src/TeamsAlwaysActive/Hook.cs`:

```csharp
using System.Text.Json;

namespace TeamsAlwaysActive;

public static class Hook
{
    public static int Run(string verb, TextReader input, State state)
    {
        try
        {
            using var doc = JsonDocument.Parse(input.ReadToEnd());
            var id = doc.RootElement.GetProperty("session_id").GetString();
            if (verb == "busy") state.Touch(id);
            else if (verb == "idle") state.Remove(id);
        }
        catch { } // ponytail: a hook must never block or fail Claude
        return 0;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/TeamsAlwaysActive.Tests`
Expected: `Passed! - Failed: 0, Passed: 20`

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "feat: add Claude Code hook mode"
```

---

### Task 4: Installer (settings.json hooks + startup)

**Files:**
- Create: `src/TeamsAlwaysActive/Installer.cs`
- Modify: `tests/TeamsAlwaysActive.Tests/Tests.cs` (append class)

**Interfaces:**
- Produces: `public static class Installer` with `string DefaultSettingsPath`, `void MergeHooks(string settingsPath, string exePath)`, `void RemoveHooks(string settingsPath)`, `void RegisterStartup(string exePath)`, `void UnregisterStartup()`.

- [ ] **Step 1: Write the failing tests** — append to `Tests.cs`:

```csharp
public class InstallerTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "taa-" + Guid.NewGuid());
    string Settings => Path.Combine(dir, "settings.json");
    const string Exe = @"C:\Users\A B\AppData\Local\TeamsAlwaysActive\app\TeamsAlwaysActive.exe";

    public InstallerTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, true);

    JsonObject Read() => JsonNode.Parse(File.ReadAllText(Settings))!.AsObject();

    [Fact]
    public void Merge_into_missing_file_adds_three_events()
    {
        Installer.MergeHooks(Settings, Exe);
        var hooks = Read()["hooks"]!.AsObject();
        Assert.Equal(
            "\"C:/Users/A B/AppData/Local/TeamsAlwaysActive/app/TeamsAlwaysActive.exe\" --hook busy",
            (string?)hooks["UserPromptSubmit"]![0]!["hooks"]![0]!["command"]);
        Assert.Equal("command", (string?)hooks["Stop"]![0]!["hooks"]![0]!["type"]);
        Assert.EndsWith("--hook idle", (string?)hooks["SessionEnd"]![0]!["hooks"]![0]!["command"]);
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
    }

    [Fact]
    public void Merge_refuses_non_object_settings()
    {
        File.WriteAllText(Settings, "[]");
        Assert.ThrowsAny<Exception>(() => Installer.MergeHooks(Settings, Exe));
        Assert.Equal("[]", File.ReadAllText(Settings));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/TeamsAlwaysActive.Tests`
Expected: build FAIL — `The name 'Installer' does not exist`.

- [ ] **Step 3: Implement** — `src/TeamsAlwaysActive/Installer.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace TeamsAlwaysActive;

public static class Installer
{
    const string ExeName = "TeamsAlwaysActive.exe";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static readonly (string Event, string Verb)[] Events =
        [("UserPromptSubmit", "busy"), ("Stop", "idle"), ("SessionEnd", "idle")];

    public static string DefaultSettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static void MergeHooks(string settingsPath, string exePath)
    {
        var root = Load(settingsPath);
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = hooks;
        StripOurs(hooks);
        var exe = exePath.Replace('\\', '/'); // ponytail: forward slashes work in both Git Bash and cmd
        foreach (var (ev, verb) in Events)
        {
            var groups = hooks[ev] as JsonArray ?? new JsonArray();
            hooks[ev] = groups;
            groups.Add(new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = $"\"{exe}\" --hook {verb}",
                }),
            });
        }
        Save(settingsPath, root);
    }

    public static void RemoveHooks(string settingsPath)
    {
        if (!File.Exists(settingsPath)) return;
        var root = Load(settingsPath);
        if (root["hooks"] is JsonObject hooks) StripOurs(hooks);
        Save(settingsPath, root);
    }

    public static void RegisterStartup(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue("TeamsAlwaysActive", $"\"{exePath}\"");
    }

    public static void UnregisterStartup()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.DeleteValue("TeamsAlwaysActive", throwOnMissingValue: false);
    }

    static void StripOurs(JsonObject hooks)
    {
        foreach (var (ev, _) in Events)
        {
            if (hooks[ev] is not JsonArray groups) continue;
            for (int i = groups.Count - 1; i >= 0; i--)
                if (groups[i]?.ToJsonString().Contains(ExeName) == true) groups.RemoveAt(i);
            if (groups.Count == 0) hooks.Remove(ev);
        }
    }

    static JsonObject Load(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        return JsonNode.Parse(File.ReadAllText(path), documentOptions: options) as JsonObject
            ?? throw new InvalidDataException($"{path} is not a JSON object; refusing to modify it.");
    }

    static void Save(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/TeamsAlwaysActive.Tests`
Expected: `Passed! - Failed: 0, Passed: 24`

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "feat: install/uninstall Claude Code hooks and startup entry"
```

---

### Task 5: Native input, tray app, entry point

**Files:**
- Create: `src/TeamsAlwaysActive/Native.cs`, `src/TeamsAlwaysActive/TrayApp.cs`
- Replace: `src/TeamsAlwaysActive/Program.cs`

**Interfaces:**
- Consumes: `Mode`, `Decision.WantActive`, `Decision.ShouldNudge`, `State`, `Hook.Run`, `Installer.*`
- Produces: `static class Native` with `TimeSpan IdleTime()`, `void PressF15()`; `public sealed class TrayApp(State) : ApplicationContext`; CLI `TeamsAlwaysActive.exe [--hook busy|idle | --install | --uninstall]`.

- [ ] **Step 1: Write `src/TeamsAlwaysActive/Native.cs`**

```csharp
using System.Runtime.InteropServices;

namespace TeamsAlwaysActive;

static class Native
{
    const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002;
    const ushort VK_F15 = 0x7E;

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    // Union must include MOUSEINPUT (largest member) so sizeof(INPUT) matches Win32 (40 bytes on x64).
    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);

    public static TimeSpan IdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }

    public static void PressF15() =>
        SendInput(2, [Key(0), Key(KEYEVENTF_KEYUP)], Marshal.SizeOf<INPUT>());

    static INPUT Key(uint flags) =>
        new() { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_F15, dwFlags = flags } } };
}
```

- [ ] **Step 2: Write `src/TeamsAlwaysActive/TrayApp.cs`**

```csharp
using System.Drawing.Drawing2D;

namespace TeamsAlwaysActive;

public sealed class TrayApp : ApplicationContext
{
    readonly State state;
    readonly NotifyIcon tray = new() { Visible = true };
    readonly Icon green = Dot(Color.LimeGreen), grey = Dot(Color.Gray);
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30_000 };
    readonly Dictionary<Mode, ToolStripMenuItem> items = new();
    Mode mode;

    public TrayApp(State state)
    {
        this.state = state;
        var menu = new ContextMenuStrip();
        foreach (var m in Enum.GetValues<Mode>())
        {
            var item = new ToolStripMenuItem(Label(m));
            item.Click += (_, _) => SetMode(m);
            items[m] = item;
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        tray.ContextMenuStrip = menu;
        timer.Tick += (_, _) => Tick();
        timer.Start();
        SetMode(state.ReadMode());
    }

    static string Label(Mode m) => m switch
    {
        Mode.Off => "Off",
        Mode.On => "On",
        _ => "Auto (while Claude Code works)",
    };

    void SetMode(Mode m)
    {
        mode = m;
        state.WriteMode(m);
        foreach (var (k, item) in items) item.Checked = k == m;
        Tick();
    }

    void Tick()
    {
        bool busy = mode == Mode.Auto && state.AnyBusy(DateTime.UtcNow);
        bool want = Decision.WantActive(mode, busy);
        if (Decision.ShouldNudge(want, Native.IdleTime())) Native.PressF15();
        tray.Icon = want ? green : grey;
        tray.Text = $"Teams Always Active: {mode}" + (want ? " (keeping active)" : "");
    }

    protected override void ExitThreadCore()
    {
        timer.Stop();
        tray.Visible = false;
        base.ExitThreadCore();
    }

    static Icon Dot(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        using (var brush = new SolidBrush(color))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillEllipse(brush, 1, 1, 14, 14);
        }
        return Icon.FromHandle(bmp.GetHicon()); // ponytail: two icons for the app's lifetime, HICON never freed
    }
}
```

- [ ] **Step 3: Replace `src/TeamsAlwaysActive/Program.cs`**

```csharp
namespace TeamsAlwaysActive;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TeamsAlwaysActive");
        var state = new State(root);
        var exe = Environment.ProcessPath!;

        switch (args)
        {
            case ["--hook", var verb]:
                return Hook.Run(verb, Console.In, state);
            case ["--install"]:
                Installer.MergeHooks(Installer.DefaultSettingsPath, exe);
                Installer.RegisterStartup(exe);
                return 0;
            case ["--uninstall"]:
                Installer.RemoveHooks(Installer.DefaultSettingsPath);
                Installer.UnregisterStartup();
                return 0;
        }

        using var mutex = new Mutex(true, @"Local\TeamsAlwaysActive", out bool first);
        if (!first) return 0;
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp(state));
        return 0;
    }
}
```

- [ ] **Step 4: Build and run unit tests**

Run: `dotnet build src/TeamsAlwaysActive -c Release` → Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`
Run: `dotnet test tests/TeamsAlwaysActive.Tests` → Expected: `Passed! - Failed: 0, Passed: 24`

- [ ] **Step 5: Verify hook mode reads stdin as a WinExe**

```powershell
$exe = "src/TeamsAlwaysActive/bin/Release/net10.0-windows/TeamsAlwaysActive.exe"
'{"session_id":"manualtest"}' | Set-Content -Encoding ascii "$env:TEMP\taa-hook.json"
Start-Process $exe -ArgumentList '--hook','busy' -RedirectStandardInput "$env:TEMP\taa-hook.json" -Wait -NoNewWindow
Test-Path "$env:LOCALAPPDATA\TeamsAlwaysActive\busy\manualtest"
Start-Process $exe -ArgumentList '--hook','idle' -RedirectStandardInput "$env:TEMP\taa-hook.json" -Wait -NoNewWindow
Test-Path "$env:LOCALAPPDATA\TeamsAlwaysActive\busy\manualtest"
```

Expected: `True` then `False`.

- [ ] **Step 6: Verify F15 resets idle**

Write a one-off check in the scratchpad (not committed): start the tray exe, set mode **On**, keep hands off for 5 minutes, then confirm in PowerShell that the idle counter was reset by the app:

```powershell
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Idle {
  [StructLayout(LayoutKind.Sequential)] struct L { public uint cb; public uint t; }
  [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref L l);
  public static double Seconds() { var l = new L { cb = 8 }; GetLastInputInfo(ref l); return (unchecked((uint)Environment.TickCount - l.t)) / 1000.0; }
}
'@
1..12 | ForEach-Object { [Idle]::Seconds(); Start-Sleep 30 }
```

Expected: the value climbs to ~240–270 s and then drops back near 0 (the F15 nudge), never exceeding ~270 s. Tray icon is green. Then set **Off** and confirm it climbs past 300 s and the icon is grey. Exit the tray app from its menu.

- [ ] **Step 7: Commit**

```powershell
git add src
git commit -m "feat: tray app with F15 idle nudge and CLI entry"
```

---

### Task 6: Publish, install, end-to-end check, README

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: CLI from Task 5.

- [ ] **Step 1: Publish to the install folder** (exit any running tray instance first, the exe is locked while running)

```powershell
dotnet publish src/TeamsAlwaysActive -c Release -o "$env:LOCALAPPDATA\TeamsAlwaysActive\app"
```

Expected: `TeamsAlwaysActive -> C:\Users\...\AppData\Local\TeamsAlwaysActive\app\`

- [ ] **Step 2: Install hooks + startup, start the app**

```powershell
& "$env:LOCALAPPDATA\TeamsAlwaysActive\app\TeamsAlwaysActive.exe" --install
Select-String -Path "$HOME\.claude\settings.json" -Pattern 'TeamsAlwaysActive' | Measure-Object | Select-Object -ExpandProperty Count
(Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run').TeamsAlwaysActive
Start-Process "$env:LOCALAPPDATA\TeamsAlwaysActive\app\TeamsAlwaysActive.exe"
```

Expected: count `3`; Run value is the quoted exe path; grey dot appears in the tray.

- [ ] **Step 3: End-to-end Auto check (Claude desktop Code tab and VS Code)**

Set tray mode to **Auto**. In a new Claude Code session (desktop Code tab), send a prompt that runs a while (e.g. "count slowly to 60 using sleep 1 between numbers"). While it runs:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\TeamsAlwaysActive\busy"
```

Expected: one file named after the session id; tray icon turns green within 30 s. After Claude finishes, the file is gone and the icon turns grey within 30 s. Repeat once from the VS Code extension.

- [ ] **Step 4: Teams check**

Mode **On**, leave the PC untouched for 10 minutes with Teams open. Expected: Teams presence stays Available (check from phone or ask a colleague). Note result in README "Verified" line.

- [ ] **Step 5: Write `README.md`**

````markdown
# Teams-Always-Active

Tray app that keeps Microsoft Teams from flipping to **Away** while you (or Claude Code) are working.

Teams goes Away after ~5 min with no keyboard/mouse input. When active, this app sends one
F15 key press (a key no keyboard has) whenever Windows has seen 4+ minutes of no input.
It never sends anything while you're using the PC.

## Modes (right-click the tray dot)

- **Off** — does nothing.
- **On** — keeps you active.
- **Auto** — keeps you active only while a Claude Code session is working
  (desktop Code tab, VS Code extension, CLI; not plain desktop chat).

Green dot = keeping active, grey = not.

## Install

Requires the .NET 10 SDK.

```powershell
dotnet publish src/TeamsAlwaysActive -c Release -o "$env:LOCALAPPDATA\TeamsAlwaysActive\app"
& "$env:LOCALAPPDATA\TeamsAlwaysActive\app\TeamsAlwaysActive.exe" --install
Start-Process "$env:LOCALAPPDATA\TeamsAlwaysActive\app\TeamsAlwaysActive.exe"
```

`--install` adds three hooks to `~/.claude/settings.json` (backup at `settings.json.bak`)
and starts the app at logon. `--uninstall` removes both.

## Caveats

- While active, the screen will not auto-lock either. Check your employer's policy.
- Locking the PC or sleep still shows Away (by design).
````

- [ ] **Step 6: Commit**

```powershell
git add README.md
git commit -m "docs: README with install and usage"
```

---

## Out of scope (from spec)

Graph presence API, settings UI, schedules, configurable intervals.
