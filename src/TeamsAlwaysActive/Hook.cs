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
