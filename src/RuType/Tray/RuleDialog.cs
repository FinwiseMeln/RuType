using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using RuType.Interop;

namespace RuType.Tray;

/// <summary>
/// Мини-окно создания правила замены: слово подставлено в левое поле, справа -
/// на что менять. По «Создать» вызывает callback (откуда -> куда).
/// </summary>
public sealed class RuleDialog : Window
{
    private readonly TextBox _from;
    private readonly TextBox _to;
    private readonly Action<string, string> _onCreate;

    public RuleDialog(string word, Action<string, string> onCreate)
    {
        _onCreate = onCreate;

        Title = "RuType — правило";
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Background = Theme.WinBg;
        FontSize = 14;

        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(new TextBlock
        {
            Text = "Правило замены",
            Foreground = Theme.Fg,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        _from = Box(word, 180);
        _to = Box("", 180);
        row.Children.Add(_from);
        row.Children.Add(new TextBlock
        {
            Text = "→",
            Foreground = Theme.Fg,
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0)
        });
        row.Children.Add(_to);
        root.Children.Add(row);

        root.Children.Add(new TextBlock
        {
            Text = "Заменять слово слева на слово справа (без учёта регистра).",
            Foreground = Theme.Muted,
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0)
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var create = Btn("Создать", accent: true);
        create.Click += (_, _) => Create();
        var cancel = Btn("Отмена", accent: false);
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(cancel);
        buttons.Children.Add(create);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => { _to.Focus(); _to.SelectAll(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int on = 1;
        if (NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
    }

    private void Create()
    {
        string from = _from.Text.Trim();
        string to = _to.Text.Trim();
        if (from.Length == 0 || to.Length == 0) { Close(); return; }
        _onCreate(from, to);
        Close();
    }

    private static TextBox Box(string text, double width) => new()
    {
        Text = text,
        Width = width,
        VerticalAlignment = VerticalAlignment.Center
    };

    private Button Btn(string text, bool accent)
    {
        var b = new Button
        {
            Content = text,
            Margin = new Thickness(6, 0, 0, 0)
        };
        if (accent) b.Style = (Style)FindResource("AccentButton");
        return b;
    }
}
