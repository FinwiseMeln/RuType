using System.Text;
using System.Windows.Automation;
using static RuType.Interop.NativeMethods;

namespace RuType.Interop;

/// <summary>
/// Определение исключённого контекста: активное приложение в чёрном списке или
/// фокус в поле пароля. В таком контексте программа не трогает (и не логирует) ввод.
///
/// Поле пароля: быстрый Win32-детект (ES_PASSWORD у нативного Edit) на каждый
/// нажатый символ + более широкий UIA-детект (FocusedElement.IsPassword,
/// покрывает браузеры/Electron) на границе слова, перед действием.
/// </summary>
public sealed class ExclusionManager
{
    private HashSet<string> _blacklist = new(StringComparer.OrdinalIgnoreCase);

    public void SetBlacklist(IEnumerable<string> apps)
    {
        _blacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in apps)
        {
            var name = a.Trim();
            if (name.Length == 0) continue;
            _blacklist.Add(name);
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                _blacklist.Add(name + ".exe");
        }
    }

    /// <summary>Процесс активного окна входит в чёрный список.</summary>
    public bool IsBlacklistedApp(IntPtr foreground)
    {
        if (_blacklist.Count == 0 || foreground == IntPtr.Zero) return false;
        try
        {
            GetWindowThreadProcessId(foreground, out uint pid);
            if (pid == 0) return false;

            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (!QueryFullProcessImageName(h, 0, sb, ref size)) return false;
                string exe = System.IO.Path.GetFileName(sb.ToString());
                return _blacklist.Contains(exe);
            }
            finally
            {
                CloseHandle(h);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Быстрый детект: фокус в нативном поле пароля (ES_PASSWORD).</summary>
    public bool IsPasswordFast()
    {
        try
        {
            IntPtr focus = GetFocusedControl();
            if (focus == IntPtr.Zero) return false;

            var sb = new StringBuilder(64);
            GetClassName(focus, sb, sb.Capacity);
            string cls = sb.ToString();
            if (cls.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) < 0) return false;

            long style = GetWindowLongPtr(focus, GWL_STYLE).ToInt64();
            return (style & ES_PASSWORD) != 0;
        }
        catch
        {
            return false;
        }
    }

    // Кеш UIA-детекта: кросс-процессный COM-вызов дорог (десятки мс в браузерах) и
    // дёргается на границе КАЖДОГО слова из синхронного пути хука - без кеша это
    // постоянный налог на задержку ввода и риск снятия LL-хука по таймауту. Ключ -
    // (активное окно, hwnd фокуса) с коротким TTL: внутри браузера hwnd фокуса не
    // меняется при переходе между полями страницы, поэтому окно риска (пароль-поле
    // после обычного в том же окне) ограничено TTL, а не временем жизни фокуса.
    private IntPtr _uiaForeground;
    private IntPtr _uiaFocus;
    private bool _uiaResult;
    private long _uiaAtMs;
    private const int UiaTtlMs = 1500;

    /// <summary>Широкий детект через UIA (браузеры/Electron). Вызывать на границе слова.</summary>
    public bool IsPasswordThorough()
    {
        if (IsPasswordFast()) return true;

        IntPtr fg = GetForegroundWindow();
        IntPtr focus = GetFocusedControl();
        long now = Environment.TickCount64;
        if (focus != IntPtr.Zero && focus == _uiaFocus && fg == _uiaForeground
            && now - _uiaAtMs < UiaTtlMs)
            return _uiaResult;

        bool result;
        try
        {
            AutomationElement? focused = AutomationElement.FocusedElement;
            result = focused != null
                && (bool)focused.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty);
        }
        catch
        {
            result = false; // UIA ненадёжен в некоторых окнах - не блокируем ввод
        }

        _uiaForeground = fg;
        _uiaFocus = focus;
        _uiaResult = result;
        _uiaAtMs = now;
        return result;
    }

    private static IntPtr GetFocusedControl()
    {
        var gti = new GUITHREADINFO();
        gti.cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>();
        uint tid = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        if (tid == 0) return IntPtr.Zero;
        if (!GetGUIThreadInfo(tid, ref gti)) return IntPtr.Zero;
        return gti.hwndFocus;
    }
}
