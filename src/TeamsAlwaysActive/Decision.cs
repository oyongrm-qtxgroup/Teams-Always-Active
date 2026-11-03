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
