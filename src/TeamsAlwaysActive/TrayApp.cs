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
        try { state.WriteMode(m); }
        catch (IOException) { } // ponytail: transient disk errors shouldn't kill the tray; mode still applies in memory
        catch (UnauthorizedAccessException) { }
        foreach (var (k, item) in items) item.Checked = k == m;
        Tick();
    }

    void Tick()
    {
        try
        {
            bool busy = mode == Mode.Auto && state.AnyBusy(DateTime.UtcNow);
            bool want = Decision.WantActive(mode, busy);
            if (Decision.ShouldNudge(want, Native.IdleTime())) Native.PressF15();
            tray.Icon = want ? green : grey;
            tray.Text = $"Teams Always Active: {mode}" + (want ? " (keeping active)" : "");
        }
        catch (IOException) { } // ponytail: transient disk errors shouldn't kill the tray; the next tick retries
        catch (UnauthorizedAccessException) { }
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
