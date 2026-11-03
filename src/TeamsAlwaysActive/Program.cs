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
