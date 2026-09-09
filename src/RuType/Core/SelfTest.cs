using System.Text;
using RuType.Config;
using RuType.Interop;

namespace RuType.Core;

/// <summary>
/// Headless-проверка логики ядра без хука и инъекции ввода: грузит реальный
/// словарь и прогоняет образцы слов через Analyzer. Запуск: RuType.exe --selftest.
/// Результат пишется в файл и (если есть консоль) в stdout.
/// </summary>
public static class SelfTest
{
    public static int Run()
    {
        var sb = new StringBuilder();
        var store = new ConfigStore();
        var cfg = store.Load();
        store.EnsureUserLists();

        var dict = new Dictionaries();
        string ruDic = store.ResolveAppPath(cfg.Dictionaries.RuHunspell);
        bool ruOk = dict.LoadRuHunspell(ruDic);
        bool enOk = dict.LoadEnHunspell(store.ResolveAppPath(cfg.Dictionaries.EnHunspell));
        bool freqOk = dict.LoadFreq(store.ResolveAppPath(cfg.Dictionaries.RuFreq));
        bool extraOk = dict.LoadExtra(store.ResolveAppPath(cfg.Dictionaries.RuExtra));
        bool extraDicOk = dict.LoadRuExtraHunspell(
            store.ResolveAppPath(cfg.Dictionaries.RuExtraDic), System.IO.Path.ChangeExtension(ruDic, ".aff"));
        bool enExtraOk = dict.LoadEnExtra(store.ResolveAppPath(cfg.Dictionaries.EnExtra));
        dict.LoadUserLists(store.MyWordsPath, store.StopWordsPath, store.RulesPath);
        if (cfg.Ngram.Enabled && ruOk)
            dict.SetNgram(NgramModel.BuildOrLoad(ruDic, store.ResolveAppPath(cfg.Dictionaries.RuFreq),
                System.IO.Path.Combine(store.DataDir, cfg.Ngram.CacheFile), cfg.Ngram.WeightByFrequency));

        sb.AppendLine($"ru Hunspell: {(ruOk ? "загружен" : "НЕ ЗАГРУЖЕН")} ({ruDic})");
        sb.AppendLine($"en Hunspell: {(enOk ? "загружен" : "НЕ ЗАГРУЖЕН")}");
        sb.AppendLine($"частоты: {(freqOk ? $"загружены ({dict.FreqCount})" : "НЕ ЗАГРУЖЕНЫ")}");
        sb.AppendLine($"ru_extra: {(extraOk ? $"загружен ({dict.ExtraWordsCount})" : "нет")}");
        sb.AppendLine($"ru_extra.dic (аффиксный): {(extraDicOk ? "загружен" : "нет")}");
        sb.AppendLine($"en_extra: {(enExtraOk ? $"загружен ({dict.EnExtraWordsCount})" : "нет")}");
        sb.AppendLine($"n-gram: {(dict.NgramLoaded ? "загружена" : "нет")}");
        sb.AppendLine($"my_words: {dict.MyWordsCount}, stopwords: {dict.StopWordsCount}, rules: {dict.RulesCount}");
        sb.AppendLine($"max_edit_distance={cfg.Typo.MaxEditDistance}, min_word_length={cfg.Typo.MinWordLength}, " +
                      $"dominance_ratio={cfg.Typo.DominanceRatio}, min_frequency={cfg.Typo.MinFrequency}");
        sb.AppendLine(new string('-', 60));

        var analyzer = new Analyzer(dict, cfg) { LayoutDetectionEnabled = enOk };

        // Опечатки, которые должны исправиться.
        string[] typos = { "привт", "докумен", "ошбка", "сегдня", "сообщене", "превед", "касается" };
        sb.AppendLine("ОПЕЧАТКИ (ожидаем исправление):");
        foreach (var w in typos)
            sb.AppendLine($"  {w,-14} -> {Describe(analyzer.Analyze(w))}");

        // Корректные слова (в т.ч. редкие формы) - не трогать.
        string[] valid = { "кошка", "документ", "сегодня", "привет", "программа", "работает", "вкладки", "вкладок", "ноутбук" };
        sb.AppendLine("КОРРЕКТНЫЕ (ожидаем без изменений):");
        foreach (var w in valid)
            sb.AppendLine($"  {w,-14} -> {Describe(analyzer.Analyze(w))}");

        // Англицизмы/современная лексика из ru_extra - НЕ трогать (симптом "ложно исправляет").
        sb.AppendLine("АНГЛИЦИЗМЫ (ожидаем без изменений, если ru_extra загружен):");
        foreach (var w in new[] { "десктоп", "лайфхак", "фронтенд", "бэкенд", "деплой", "гаджет" })
            sb.AppendLine($"  {w,-14} -> {Describe(analyzer.Analyze(w))}");

        // Опечатки в англицизмах - ожидаем исправление через кандидатов из ru_extra.
        sb.AppendLine("ОПЕЧАТКИ В АНГЛИЦИЗМАХ (ожидаем исправление):");
        foreach (var w in new[] { "десктп", "лайфак", "гаджт" })
            sb.AppendLine($"  {w,-14} -> {Describe(analyzer.Analyze(w))}");

        // Регистр.
        sb.AppendLine("РЕГИСТР:");
        foreach (var w in new[] { "Привт", "ПРИВТ" })
            sb.AppendLine($"  {w,-14} -> {Describe(analyzer.Analyze(w))}");

        // Раскладка (ожидаем переключение + перенабор).
        sb.AppendLine("РАСКЛАДКА (ожидаем смену раскладки):");
        foreach (var w in new[] { "ghbdtn", "руддщ", "ьфылф", "Ghbdtn" })
            sb.AppendLine($"  {w,-14} -> {Describe(analyzer.Analyze(w))}");
        sb.AppendLine("РАСКЛАДКА - НЕ трогать настоящие слова:");
        foreach (var w in new[] { "hello", "world", "привет", "tcp", "url", "gif", "vps", "dir", "ctrl", "xhttp", "уты" })
            sb.AppendLine($"  {w,-14} -> {Describe(analyzer.Analyze(w))}");

        // Раскладка по "сырому" прогону со знаком-буквой (',' -> 'б'): ',fylbn' -> 'бандит'.
        // Проверяем IsLayoutSwap так же, как его вызывает InputProcessor на границе:
        // первый случай - валидное слово (смена), второй - мусор (без изменений).
        sb.AppendLine("РАСКЛАДКА со знаком-буквой:");
        foreach (var (cur, tgt) in new[] { (",fylbn", "бандит"), (",rjrj", "бкоко") })
            sb.AppendLine($"  {cur,-14} -> {(analyzer.IsLayoutSwap(cur, tgt, LayoutTarget.Ru) ? $"смена => '{tgt}'" : "(без изменений)")}");

        // Правила (если заданы в rules.txt).
        sb.AppendLine("ПРАВИЛА (зависит от rules.txt):");
        foreach (var w in new[] { "тлф", "кантора" })
            sb.AppendLine($"  {w,-14} -> {Describe(analyzer.Analyze(w))}");

        // Сценарии InputProcessor: полный путь границ/сегментов на фейковой раскладке.
        // Ловят класс граблей "знак-буква" ('продолжить' = 'ghjljk;bnm' -> раньше
        // 'продал;ить'), который Analyzer-фикстуры не видят. Дефолтный конфиг, чтобы
        // ожидания не зависели от пользовательского config.json.
        int scenFail = 0;
        if (ruOk && enOk)
        {
            sb.AppendLine("СЦЕНАРИИ ВВОДА (клавиши по EN-позициям -> итоговый текст):");
            var scenAnalyzer = new Analyzer(dict, new AppConfig()) { LayoutDetectionEnabled = true };
            foreach (var (keys, expect, startRu) in new (string, string, bool)[]
            {
                ("ghbdtn ",      "привет ",      false), // детект раскладки на пробеле
                ("ghjljk;bnm ",  "продолжить ",  false), // знак-буква ';'='ж' в середине
                ("ufhf; ",       "гараж ",       false), // знак-буква в конце
                (",fylbn ",      "бандит ",      false), // знак-буква в начале (регресс)
                ("gjt[fkb ",     "поехали ",     false), // знак-буква '['='х' (регресс)
                ("ghjljk;bnm? ", "продолжить, ", false), // хвостовой знак по целевому рендеру ('?'='RU запятая')
                ("ghbdtn. ",     "привет. ",     false), // хвостовая точка ('.'='ю', но задумана точкой)
                ("hello ",       "hello ",       false), // keep: настоящее английское
                ("ghbdtn5 ",     "ghbdtn5 ",     false), // keep: цифра -> технический токен
                ("ghbdtn ",      "привет ",      true),  // keep: обычный русский набор
                ("ghbdtn& ",     "привет? ",     true),  // keep: русский со знаком '?' (Shift+7)
                ("hello ",       "hello ",       true),  // 'руддщ' -> детект на пробеле
                ("rfrjq-nj ",    "какой-то ",    false), // дефис внутри слова: чинить обе части
                ("ghbdtn,rfrjq ", "привет,какой ", false), // знак МЕЖДУ словами: режем прогон
                ("test,data ",   "test,data ",   false), // keep: настоящий английский со знаком
                ("ghbdtn?rfrjq ", "привет,какой ", true), // keep: русский набор со знаком не трогаем
                ("ghbdtn/rfrjq ", "привет.какой ", false), // знак-разделитель, в цели тоже знак
                (",'rfg ",       "бэкап ",       false), // знак-буква '\''='э' (слово из ru_extra.dic)
                ("ghbdtn/ ",     "привет. ",     false), // клавиша '/' - точка в RU
                ("(ghbdtn) ",    "(привет) ",    false), // слово в скобках
            })
            {
                var kb = new FakeKeyboard(scenAnalyzer);
                if (startRu) kb.Current = FakeKeyboard.Ru;
                kb.Type(keys);
                bool ok = kb.Screen == expect;
                if (!ok) scenFail++;
                sb.AppendLine($"  {(ok ? "OK  " : "FAIL")} '{keys}' -> '{kb.Screen}'{(ok ? "" : $" (ожидалось '{expect}')")}");
            }

            // Alt+Tab с невалидным словом в буфере: Tab при зажатом Alt - команда, а не
            // граница. Раньше ToUnicodeEx отдавал табуляцию, слово уходило в Analyze, а
            // правка (и подавление клавиши) улетали в переключение окон.
            {
                var kb = new FakeKeyboard(scenAnalyzer);
                kb.Type("ghbdtn");
                kb.Chord = true; kb.Type("	"); kb.Chord = false;
                kb.Type(" ");
                bool ok = kb.Screen == "ghbdtn	 ";
                if (!ok) scenFail++;
                sb.AppendLine($"  {(ok ? "OK  " : "FAIL")} Alt+Tab не граница -> '{kb.Screen.Replace("	", "<tab>")}'{(ok ? "" : " (ожидалось 'ghbdtn<tab> ')")}");
            }

            // Смена активного окна без клика мышью (Alt+Tab, Win+N): force по слову из
            // прежнего окна не должен впечатываться в новое по Pause.
            {
                var kb = new FakeKeyboard(scenAnalyzer);
                kb.Type("hello ");            // валидное английское - force взведён
                kb.Window = (IntPtr)2;        // другое окно
                kb.Press(0x13, false);        // Pause
                bool ok = kb.Screen == "hello ";
                if (!ok) scenFail++;
                sb.AppendLine($"  {(ok ? "OK  " : "FAIL")} Pause после смены окна -> '{kb.Screen}'{(ok ? "" : " (ожидалось 'hello ')")}");
            }

            sb.AppendLine(scenFail == 0 ? "  все сценарии пройдены" : $"  ПРОВАЛЕНО СЦЕНАРИЕВ: {scenFail}");
        }

        string report = sb.ToString();
        string outPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "rutype_selftest.txt");
        System.IO.File.WriteAllText(outPath, report);
        Console.WriteLine(report);
        Console.WriteLine($"[report -> {outPath}]");
        return !ruOk ? 2 : scenFail > 0 ? 3 : 0;
    }

    /// <summary>
    /// Фейковая клавиатура для headless-прогона InputProcessor: таблицы QWERTY/ЙЦУКЕН
    /// вместо ToUnicodeEx, модель "экрана" вместо SendInput. Клавиши подаются по
    /// символам АНГЛИЙСКОЙ раскладки (= физические позиции), Current задаёт активную.
    /// </summary>
    private sealed class FakeKeyboard
    {
        public static readonly IntPtr En = (IntPtr)0x04090409;
        public static readonly IntPtr Ru = (IntPtr)0x04190419;

        public IntPtr Current = En;
        public bool Shift;
        public bool Chord;      // зажат Ctrl/Alt/Win (команда, не текст)
        public IntPtr Window = (IntPtr)1; // "активное окно" - смена сбрасывает состояние
        public string Screen => _screen.ToString();

        private readonly StringBuilder _screen = new();
        private readonly InputProcessor _proc;

        // (vk, shift) -> символ; только клавиши, нужные сценариям.
        private static readonly Dictionary<(uint Vk, bool Shift), string> EnMap = new();
        private static readonly Dictionary<(uint Vk, bool Shift), string> RuMap = new();
        private static readonly Dictionary<char, (uint Vk, bool Shift)> EnRev = new();

        static FakeKeyboard()
        {
            const string enKeys    = "qwertyuiop[]asdfghjkl;'zxcvbnm,.";
            const string enShifted = "QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>";
            const string ruLetters = "йцукенгшщзхъфывапролджэячсмитьбю";
            for (int i = 0; i < enKeys.Length; i++)
            {
                char en = enKeys[i];
                uint vk = en switch
                {
                    '[' => 0xDB, ']' => 0xDD, ';' => 0xBA, '\'' => 0xDE, ',' => 0xBC, '.' => 0xBE,
                    _ => char.ToUpperInvariant(en)
                };
                EnMap[(vk, false)] = en.ToString();
                EnMap[(vk, true)] = enShifted[i].ToString();
                RuMap[(vk, false)] = ruLetters[i].ToString();
                RuMap[(vk, true)] = char.ToUpperInvariant(ruLetters[i]).ToString();
            }
            // Клавиша /? : в EN - '/', '?'; в РУ - '.', ','.
            EnMap[(0xBF, false)] = "/"; EnMap[(0xBF, true)] = "?";
            RuMap[(0xBF, false)] = "."; RuMap[(0xBF, true)] = ",";
            // Дефис и равно - одинаковы в обеих раскладках (нужны для 'какой-то').
            EnMap[(0xBD, false)] = "-"; EnMap[(0xBD, true)] = "_";
            RuMap[(0xBD, false)] = "-"; RuMap[(0xBD, true)] = "_";
            // Цифровой ряд.
            const string digits = "1234567890", enSh = "!@#$%^&*()", ruSh = "!\"№;%:?*()";
            for (int i = 0; i < digits.Length; i++)
            {
                uint vk = digits[i];
                EnMap[(vk, false)] = digits[i].ToString(); EnMap[(vk, true)] = enSh[i].ToString();
                RuMap[(vk, false)] = digits[i].ToString(); RuMap[(vk, true)] = ruSh[i].ToString();
            }
            EnMap[(0x20, false)] = " "; RuMap[(0x20, false)] = " ";
            EnMap[(0x09, false)] = "	"; RuMap[(0x09, false)] = "	";

            foreach (var kv in EnMap)
                if (kv.Value.Length == 1 && !EnRev.ContainsKey(kv.Value[0]))
                    EnRev[kv.Value[0]] = kv.Key;
        }

        private static string? Translate(uint vk, uint scan, IntPtr hkl, bool shift, bool caps)
        {
            var map = (hkl.ToInt64() & 0xFFFF) == 0x0419 ? RuMap : EnMap;
            return map.TryGetValue((vk, shift), out var s) ? s : null;
        }

        public FakeKeyboard(Analyzer analyzer)
        {
            _proc = new InputProcessor(analyzer, dispatcher: null)
            {
                RuLayout = Ru,
                EnLayout = En,
                CurrentLayout = () => Current,
                TranslateLive = (vk, scan, hkl) => Translate(vk, scan, hkl, Shift, false),
                TranslateWith = Translate,
                Modifiers = () => (Shift, false),
                CommandChordHeld = () => Chord,
                ForegroundWindow = () => Window,
            };
            _proc.ReplacementRequested += r => Apply(r.Backspaces, r.Text, r.Layout);
            _proc.ToggleRequested += t => Apply(t.Backspaces, t.Text, t.ActivateLayout);
        }

        private void Apply(int backspaces, string text, LayoutTarget lt)
        {
            if (lt == LayoutTarget.Ru) Current = Ru;
            else if (lt == LayoutTarget.En) Current = En;
            int n = Math.Min(backspaces, _screen.Length);
            _screen.Remove(_screen.Length - n, n);
            _screen.Append(text);
        }

        /// <summary>Нажать клавиши, заданные символами английской раскладки.</summary>
        public void Type(string enChars)
        {
            foreach (char c in enChars)
            {
                if (!EnRev.TryGetValue(c, out var k))
                    throw new InvalidOperationException($"FakeKeyboard: нет клавиши для '{c}'");
                Press(k.Vk, k.Shift);
            }
        }

        public void Press(uint vk, bool shift)
        {
            Shift = shift;
            var args = new KeyboardHook.KeyArgs { VkCode = vk, ScanCode = 0, IsKeyDown = true };
            _proc.OnKey(null, args);
            if (!args.Suppress)
            {
                string? ch = Translate(vk, 0, Current, shift, false);
                if (ch != null) _screen.Append(ch);
            }
        }
    }

    private static string Describe(Decision d) => d.Kind switch
    {
        ActionKind.None => "(без изменений)",
        ActionKind.Typo => $"опечатка => '{d.Replacement}'",
        ActionKind.Rule => $"правило  => '{d.Replacement}'",
        ActionKind.Layout => $"раскладка=> '{d.Replacement}' ({d.Layout})",
        _ => "?"
    };
}
