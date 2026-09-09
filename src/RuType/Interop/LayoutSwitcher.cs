using static RuType.Interop.NativeMethods;

namespace RuType.Interop;

/// <summary>
/// Определение установленных раскладок и переключение языка ввода для окна
/// в фокусе. В версии 1 поддерживается только пара ru &lt;-&gt; en (ТЗ, раздел 7):
/// автопереключение активно, лишь если в системе есть обе раскладки.
/// </summary>
public sealed class LayoutSwitcher
{
    private const ushort LANGID_RU = 0x0419;
    private const ushort LANGID_EN = 0x0409;

    public IntPtr RuLayout { get; private set; } = IntPtr.Zero;
    public IntPtr EnLayout { get; private set; } = IntPtr.Zero;

    /// <summary>В системе есть и русская, и английская раскладки.</summary>
    public bool BothPresent => RuLayout != IntPtr.Zero && EnLayout != IntPtr.Zero;

    public void Detect()
    {
        uint count = GetKeyboardLayoutList(0, null);
        if (count == 0) return;
        var list = new IntPtr[count];
        GetKeyboardLayoutList((int)count, list);

        foreach (IntPtr hkl in list)
        {
            // Младшее слово HKL - LANGID активной раскладки.
            ushort langId = (ushort)(hkl.ToInt64() & 0xFFFF);
            if (langId == LANGID_RU && RuLayout == IntPtr.Zero) RuLayout = hkl;
            else if (langId == LANGID_EN && EnLayout == IntPtr.Zero) EnLayout = hkl;
        }
    }

    /// <summary>Переключает раскладку окна в фокусе на указанную.</summary>
    public void Activate(IntPtr hkl)
    {
        if (hkl == IntPtr.Zero) return;
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, (IntPtr)INPUTLANGCHANGE_SYSCHARSET, hkl);
    }
}
