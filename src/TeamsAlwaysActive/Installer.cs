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
