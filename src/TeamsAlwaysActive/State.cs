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
