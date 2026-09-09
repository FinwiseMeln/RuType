using System.Runtime.InteropServices;
using static RuType.Interop.NativeMethods;

namespace RuType.Interop;

/// <summary>
/// Исполнитель замены: стирает N символов (backspace) и впечатывает текст
/// через SendInput с Unicode-флагом. Весь инъецированный ввод помечается
/// LLKHF_INJECTED самой системой, поэтому наш хук его игнорирует.
/// </summary>
public static class Replacer
{
    // sizeof(INPUT): 40 на x64, 28 на x86. Берём фактический размер - при неверном
    // значении SendInput молча возвращает 0 и ничего не вводит.
    private static readonly int CbSize = Marshal.SizeOf<INPUT>();

    /// <summary>
    /// Стирает <paramref name="backspaces"/> символов, печатает <paramref name="text"/>
    /// и, если задан <paramref name="trailingVk"/>, дожимает эту клавишу (Enter/Tab)
    /// как обычное нажатие (Unicode-инъекция переводов строки/табов ненадёжна).
    /// </summary>
    public static void Replace(int backspaces, string text, int trailingVk = 0)
    {
        var inputs = new List<INPUT>(backspaces * 2 + text.Length * 2 + 2);

        for (int i = 0; i < backspaces; i++)
        {
            inputs.Add(KeyDownVk(VK_BACK));
            inputs.Add(KeyUpVk(VK_BACK));
        }

        foreach (char ch in text)
        {
            inputs.Add(UnicodeDown(ch));
            inputs.Add(UnicodeUp(ch));
        }

        if (trailingVk != 0)
        {
            inputs.Add(KeyDownVk((ushort)trailingVk));
            inputs.Add(KeyUpVk((ushort)trailingVk));
        }

        if (inputs.Count == 0) return;
        var arr = inputs.ToArray();
        SendInput((uint)arr.Length, arr, CbSize);
    }

    private static INPUT KeyDownVk(ushort vk) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = 0, time = 0, dwExtraInfo = IntPtr.Zero } }
    };

    private static INPUT KeyUpVk(ushort vk) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero } }
    };

    private static INPUT UnicodeDown(char ch) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = KEYEVENTF_UNICODE, time = 0, dwExtraInfo = IntPtr.Zero } }
    };

    private static INPUT UnicodeUp(char ch) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero } }
    };
}
