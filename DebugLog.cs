namespace RGBCommander;

/// <summary>Crude always-on trace for chasing device flakiness (GPU mode-switch drops).
/// Appends next to the exe, capped so it can stay enabled indefinitely.</summary>
public static class DebugLog
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "rgbc-debug.log");

    public static void Log(string message)
    {
        try
        {
            lock (Gate)
            {
                var f = new FileInfo(LogPath);
                if (f.Exists && f.Length > 1_000_000) f.Delete();
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}\r\n");
            }
        }
        catch { }
    }
}
