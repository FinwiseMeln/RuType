using static RuType.Interop.NativeMethods;

namespace RuType.Interop;

/// <summary>
/// Низкоуровневый хук мыши (WH_MOUSE_LL). Нужен только чтобы заметить клик -
/// им пользователь перемещает каретку, а клавиатурный хук этого не видит.
/// По клику сбрасываем накопленное слово, иначе замена стёрла бы чужой текст.
/// </summary>
public sealed class MouseHook : IDisposable
{
    /// <summary>Нажата кнопка мыши (вероятно, перемещена каретка).</summary>
    public event Action? ButtonDown;

    private readonly LowLevelKeyboardProc _proc; // та же сигнатура подходит
    private IntPtr _hookHandle = IntPtr.Zero;

    public MouseHook() => _proc = HookCallback;

    public void Install()
    {
        if (_hookHandle != IntPtr.Zero) return;
        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    public void Uninstall()
    {
        if (_hookHandle == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
            {
                try { ButtonDown?.Invoke(); }
                catch (Exception ex) { RuType.Core.Log.Line($"MOUSE hook exception: {ex}"); }
            }
        }
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
