using System.IO;

namespace RuType.Core;

/// <summary>
/// Лёгкий диагностический лог. Включается, если в каталоге данных есть файл
/// debug.on. Пишет в %TEMP%\rutype_debug.log. В обычной работе бездействует.
/// </summary>
public static class Log
{
    private static readonly bool _enabled;
    private static readonly string _path;
    private static readonly object _lock = new();

    static Log()
    {
        // Портабл: лог и флаг debug.on - в подпапке data рядом с exe.
        string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        _path = Path.Combine(dataDir, "rutype_debug.log");
        _enabled = File.Exists(Path.Combine(dataDir, "debug.on"));
        if (_enabled)
        {
            try { File.AppendAllText(_path, $"\n=== старт {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n"); }
            catch { }
        }
    }

    public static bool Enabled => _enabled;

    public static void Line(string s)
    {
        if (!_enabled) return;
        try
        {
            lock (_lock)
                File.AppendAllText(_path, $"{DateTime.Now:HH:mm:ss.fff} {s}\n");
        }
        catch { }
    }
}
