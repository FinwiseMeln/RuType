using System.Windows.Media;

namespace RuType.Tray;

/// <summary>
/// Палитра тёмной темы для кода, который строит окна вручную. Значения ДУБЛИРУЮТ
/// кисти из DarkTheme.xaml (там же живут шаблоны контролов) - менять синхронно.
/// </summary>
internal static class Theme
{
    public static readonly Brush WinBg = Frozen(0x20, 0x20, 0x20);
    public static readonly Brush NavBg = Frozen(0x1B, 0x1B, 0x1B);
    public static readonly Brush PanelBg = Frozen(0x2B, 0x2B, 0x2B);
    public static readonly Brush CtrlBg = Frozen(0x35, 0x35, 0x35);
    public static readonly Brush Fg = Frozen(0xF0, 0xF0, 0xF0);
    public static readonly Brush Muted = Frozen(0xA6, 0xA6, 0xA6);
    public static readonly Brush Border = Frozen(0x3F, 0x3F, 0x3F);
    public static readonly Brush Divider = Frozen(0x36, 0x36, 0x36);
    public static readonly Brush Accent = Frozen(0x4C, 0xC2, 0xFF);
    public static readonly Brush AccentHover = Frozen(0x6E, 0xD0, 0xFF);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }
}
