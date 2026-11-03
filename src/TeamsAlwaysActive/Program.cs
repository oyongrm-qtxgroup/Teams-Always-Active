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
                return Report(() =>
                {
                    Installer.MergeHooks(Installer.DefaultSettingsPath, exe);
                    Installer.RegisterStartup(exe);
                    return $"Installed. Claude Code hooks added to {Installer.DefaultSettingsPath}; starts at logon. Open new Claude Code sessions to pick up the hooks.";
                });
            case ["--uninstall"]:
                return Report(() =>
                {
                    Installer.RemoveHooks(Installer.DefaultSettingsPath);
                    Installer.UnregisterStartup();
                    return "Uninstalled. Hooks and logon entry removed.";
                });
        }

        using var mutex = new Mutex(true, @"Local\TeamsAlwaysActive", out bool first);
        if (!first) return 0;
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp(state));
        return 0;
    }

    // WinExe has no console, so surface the outcome in a dialog.
    static int Report(Func<string> action)
    {
        try
        {
            MessageBox.Show(action(), "Teams Always Active");
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Teams Always Active", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
