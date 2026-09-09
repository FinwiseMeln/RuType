using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using RuType.Config;
using RuType.Core;
using RuType.Interop;
using RuType.Tray;

namespace RuType;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private ConfigStore _store = null!;
    private AppConfig _cfg = null!;
    private Dictionaries _dict = null!;
    private Analyzer _analyzer = null!;
    private InputProcessor _processor = null!;
    private KeyboardHook _hook = null!;
    private MouseHook _mouseHook = null!;
    private Sounds _sounds = null!;
    private TrayApp _tray = null!;
    private LayoutSwitcher _layout = null!;
    private ExclusionManager _exclusions = null!;
    private SuggestionCounter _suggestions = null!;
    private LearnLog _learn = null!;
    private SuggestionWindow? _suggestionWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Headless-самопроверка логики ядра (без хука/трея).
        if (e.Args.Contains("--selftest"))
        {
            int code = Core.SelfTest.Run();
            Shutdown(code);
            return;
        }

        // Оценка качества на fix/keep-фикстурах (accuracy + FP/FN отчёт).
        if (e.Args.Contains("--eval"))
        {
            int code = Core.Eval.Run();
            Shutdown(code);
            return;
        }

        // Диагностика триграммной модели: сравнение обучения .dic vs частотный список
        // (+ опциональный доп. частотный файл: --ngram-probe <путь>).
        int probeIdx = Array.IndexOf(e.Args, "--ngram-probe");
        if (probeIdx >= 0)
        {
            string? extra = probeIdx + 1 < e.Args.Length && !e.Args[probeIdx + 1].StartsWith("--")
                ? e.Args[probeIdx + 1] : null;
            int code = Core.NgramProbe.Run(extra);
            Shutdown(code);
            return;
        }

        // Вердикты всех оракулов по конкретным словам (наполнение словарей-дополнений).
        int checkIdx = Array.IndexOf(e.Args, "--check");
        if (checkIdx >= 0)
        {
            var words = e.Args.Skip(checkIdx + 1).TakeWhile(a => !a.StartsWith("--")).ToList();
            int code = Core.WordProbe.Run(words);
            Shutdown(code);
            return;
        }

        // Подбор порога "естественности" по реальному логу правок (corrections.jsonl).
        int marginIdx = Array.IndexOf(e.Args, "--margin-probe");
        if (marginIdx >= 0)
        {
            string? log = marginIdx + 1 < e.Args.Length && !e.Args[marginIdx + 1].StartsWith("--")
                ? e.Args[marginIdx + 1] : null;
            int code = Core.MarginProbe.Run(log);
            Shutdown(code);
            return;
        }

        // Служебный режим: выгрузить слова, неизвестные Hunspell, для словаря-дополнения.
        int dumpIdx = Array.IndexOf(e.Args, "--dump-unknown");
        if (dumpIdx >= 0)
        {
            int minFreq = 8;
            if (dumpIdx + 1 < e.Args.Length && int.TryParse(e.Args[dumpIdx + 1], out int mf)) minFreq = mf;
            int code = Core.CandidateDump.Run(minFreq);
            Shutdown(code);
            return;
        }

        int filtIdx = Array.IndexOf(e.Args, "--filter-unknown");
        if (filtIdx >= 0 && filtIdx + 2 < e.Args.Length)
        {
            int code = Core.CandidateDump.FilterUnknown(e.Args[filtIdx + 1], e.Args[filtIdx + 2]);
            Shutdown(code);
            return;
        }

        // Предпросмотр окна настроек (dev-режим для вёрстки): без хука, трея и мьютекса -
        // можно открывать рядом с работающей копией. Конфиг/списки - из data/ рядом с exe.
        int settingsIdx = Array.IndexOf(e.Args, "--settings");
        if (settingsIdx >= 0)
        {
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            _store = new ConfigStore();
            _cfg = _store.Load();
            _store.EnsureUserLists();
            var w = new SettingsWindow(_store, _cfg, () => { });
            // Необязательный номер страницы и флаг "тихо": окно за экраном, без активации -
            // для автоматических снимков (PrintWindow) без вмешательства в работу пользователя.
            if (settingsIdx + 1 < e.Args.Length && int.TryParse(e.Args[settingsIdx + 1], out int page))
                w.SelectPage(page);
            if (e.Args.Contains("--offscreen"))
            {
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Left = -w.Width - 200;
                w.Top = 100;
                w.ShowActivated = false;
                w.ShowInTaskbar = false;
            }
            w.Show();
            return;
        }

        // Один экземпляр - два хука дрались бы за один и тот же ввод.
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\RuType_SingleInstance", out bool created);
        if (!created)
        {
            MessageBox.Show("RuType уже запущен (см. иконку в трее).", "RuType",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _store = new ConfigStore();
        _cfg = _store.Load();
        _store.Save(_cfg);            // дозаписать новые поля в config.json (миграция схемы)
        _store.EnsureUserLists();

        _dict = new Dictionaries();
        string ruDic = _store.ResolveAppPath(_cfg.Dictionaries.RuHunspell);
        bool ruOk = _dict.LoadRuHunspell(ruDic);
        bool enOk = _dict.LoadEnHunspell(_store.ResolveAppPath(_cfg.Dictionaries.EnHunspell));
        bool freqOk = _dict.LoadFreq(_store.ResolveAppPath(_cfg.Dictionaries.RuFreq));
        _dict.LoadExtra(_store.ResolveAppPath(_cfg.Dictionaries.RuExtra));   // после LoadFreq
        bool extraDicOk = _dict.LoadRuExtraHunspell(
            _store.ResolveAppPath(_cfg.Dictionaries.RuExtraDic),
            System.IO.Path.ChangeExtension(ruDic, ".aff"));   // .aff общий с основным словарём
        bool enExtraOk = _dict.LoadEnExtra(_store.ResolveAppPath(_cfg.Dictionaries.EnExtra));
        _dict.LoadUserLists(_store.MyWordsPath, _store.StopWordsPath, _store.RulesPath);

        // Триграммная модель "естественности" (строится из ru_RU.dic, кеш в data/).
        // Строим всегда при наличии словаря (сборка кешируется), чтобы рубильник
        // в настройках включался без перезапуска; использование гейтит конфиг.
        if (ruOk)
        {
            var ngram = NgramModel.BuildOrLoad(ruDic,
                _store.ResolveAppPath(_cfg.Dictionaries.RuFreq),
                System.IO.Path.Combine(_store.DataDir, _cfg.Ngram.CacheFile),
                _cfg.Ngram.WeightByFrequency);
            _dict.SetNgram(ngram);
            Core.Log.Line($"ngram: {(ngram.Loaded ? $"загружена ({ngram.TrigramCount} триграмм, freq={_cfg.Ngram.WeightByFrequency})" : "НЕ построена")}");
        }
        _dict.PrewarmIndexes(); // индекс частот - на старте, не в потоке хука

        _layout = new LayoutSwitcher();
        _layout.Detect();

        _exclusions = new ExclusionManager();
        _exclusions.SetBlacklist(_cfg.Exclusions.AppBlacklist);

        _analyzer = new Analyzer(_dict, _cfg)
        {
            LayoutDetectionEnabled = _cfg.Layout.Enabled && _layout.BothPresent && enOk
        };
        _processor = new InputProcessor(_analyzer, Dispatcher, _exclusions, _cfg.Exclusions.SkipPasswordFields)
        {
            Enabled = _cfg.General.Enabled,
            HotkeyVk = _cfg.Layout.HotkeyUndoVk,
            SuggestHotkeyVk = _cfg.Layout.HotkeySuggestVk,
            RuLayout = _layout.RuLayout,
            EnLayout = _layout.EnLayout
        };
        _processor.ReplacementRequested += OnReplacement;
        _processor.ToggleRequested += OnToggle;
        _processor.SuggestRequested += word => Dispatcher.BeginInvoke(() => ShowSuggestion(word));

        _suggestions = new SuggestionCounter
        {
            Enabled = _cfg.Suggestions.Enabled,
            Threshold = _cfg.Suggestions.Threshold
        };
        _learn = new LearnLog(
            System.IO.Path.Combine(_store.DataDir, _cfg.Learning.File), _cfg.Learning.Enabled);
        _processor.WordRejected += OnWordRejected;

        _sounds = new Sounds(
            _store.ResolveAppPath(_cfg.Sound.TypoWav),
            _store.ResolveAppPath(_cfg.Sound.LayoutWav));

        _tray = new TrayApp(
            _store.ResolveAppPath(_cfg.Tray.IconIdle),
            _store.ResolveAppPath(_cfg.Tray.IconActive),
            _cfg.Tray.BlinkMs,
            _cfg.General.Enabled);
        _tray.EnabledChanged += OnEnabledChanged;
        _tray.SettingsRequested += OpenSettings;
        _tray.ExitRequested += () => Shutdown();

        AutostartManager.Apply(_cfg.General.Autostart); // синхронизировать реестр с конфигом

        _mouseHook = new MouseHook();
        _mouseHook.ButtonDown += _processor.ResetWordState; // клик мышью двигает каретку
        _mouseHook.Install();

        _hook = new KeyboardHook();
        _hook.KeyAction += _processor.OnKey;
        try
        {
            _hook.Install();
            StartHookWatchdog();
            Core.Log.Line($"hook installed={_hook.IsInstalled}, enabled={_processor.Enabled}, ruOk={ruOk}, enOk={enOk}, freqOk={freqOk} ({_dict.FreqCount}), " +
                          $"extraDic={extraDicOk}, enExtra={enExtraOk} ({_dict.EnExtraWordsCount}), " +
                          $"layoutDetect={_analyzer.LayoutDetectionEnabled} (ru={_layout.RuLayout:X} en={_layout.EnLayout:X}), " +
                          $"my={_dict.MyWordsCount}/stop={_dict.StopWordsCount}/rules={_dict.RulesCount}");
        }
        catch (Exception ex)
        {
            Core.Log.Line($"hook install FAILED: {ex}");
            MessageBox.Show($"Не удалось установить хук клавиатуры:\n{ex.Message}", "RuType",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        if (!ruOk)
        {
            MessageBox.Show(
                $"Русский словарь Hunspell не загружен:\n{ruDic}\n(нужны .dic и .aff рядом)\n\nИсправление опечаток работать не будет, пока файлы не появятся.",
                "RuType", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Windows 7+ молча снимает LL-хук, чей обработчик однажды превысил
    // LowLevelHooksTimeout (300 мс по умолчанию; Hunspell.Suggest на слабой машине
    // может). Снаружи это выглядит как "программа перестала работать до перезапуска".
    // Периодическая переустановка возвращает хук; сама операция мгновенна и ввод не теряет.
    private DispatcherTimer? _hookWatchdog;

    private void StartHookWatchdog()
    {
        _hookWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _hookWatchdog.Tick += (_, _) =>
        {
            try
            {
                long slow = _hook.TakeMaxCallbackMs();
                if (slow >= 200) Core.Log.Line($"watchdog: самый долгий обработчик за период {slow} мс");
                _hook.Reinstall();
            }
            catch (Exception ex) { Core.Log.Line($"watchdog: переустановка хука не удалась: {ex.Message}"); }
        };
        _hookWatchdog.Start();
    }

    private void OnEnabledChanged(bool enabled)
    {
        _processor.Enabled = enabled;
        _cfg.General.Enabled = enabled;
        _store.Save(_cfg);
        _tray.SetEnabledChecked(enabled);
    }

    private void OnReplacement(ReplacementRequest req)
    {
        Core.Log.Line($"REPLACE backspaces={req.Backspaces} text='{req.Text}' kind={req.Kind} layout={req.Layout}");
        _learn.LogCorrection(req.Original, req.Corrected, req.Kind);

        // Для смены раскладки сначала переключаем язык ввода окна, затем перенабираем.
        if (req.Kind == ActionKind.Layout)
        {
            IntPtr hkl = req.Layout == LayoutTarget.Ru ? _layout.RuLayout : _layout.EnLayout;
            _layout.Activate(hkl);
        }

        Replacer.Replace(req.Backspaces, req.Text, req.TrailingVk);

        if (_cfg.Tray.BlinkOnAction)
            _tray.Blink();

        if (req.Kind == ActionKind.Layout)
        {
            if (_cfg.Sound.BeepOnLayout) _sounds.PlayLayout();
        }
        else if (_cfg.Sound.BeepOnTypo && (req.Kind == ActionKind.Typo || req.Kind == ActionKind.Rule))
        {
            _sounds.PlayTypo();
        }
    }

    private SettingsWindow? _settingsWindow;

    private void OpenSettings()
    {
        if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(_store, _cfg, ApplySettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void ApplySettings()
    {
        // Файлы списков могли измениться - перечитать.
        _dict.LoadUserLists(_store.MyWordsPath, _store.StopWordsPath, _store.RulesPath);

        _analyzer.UpdateConfig(_cfg);
        _layout.Detect(); // раскладку могли добавить в систему уже после запуска
        _processor.RuLayout = _layout.RuLayout;
        _processor.EnLayout = _layout.EnLayout;
        _analyzer.LayoutDetectionEnabled = _cfg.Layout.Enabled && _layout.BothPresent && _dict.EnLoaded;

        // Режим обучения n-gram мог измениться - пересобрать модель (при смене режима
        // кеш недействителен и строится заново; иначе грузится из кеша мгновенно).
        if (_dict.RuLoaded)
        {
            var ngram = NgramModel.BuildOrLoad(
                _store.ResolveAppPath(_cfg.Dictionaries.RuHunspell),
                _store.ResolveAppPath(_cfg.Dictionaries.RuFreq),
                System.IO.Path.Combine(_store.DataDir, _cfg.Ngram.CacheFile),
                _cfg.Ngram.WeightByFrequency);
            _dict.SetNgram(ngram);
        }

        _processor.Enabled = _cfg.General.Enabled;
        _processor.SkipPasswordFields = _cfg.Exclusions.SkipPasswordFields;
        _processor.HotkeyVk = _cfg.Layout.HotkeyUndoVk;
        _processor.SuggestHotkeyVk = _cfg.Layout.HotkeySuggestVk;

        _exclusions.SetBlacklist(_cfg.Exclusions.AppBlacklist);
        _suggestions.Enabled = _cfg.Suggestions.Enabled;
        _suggestions.Threshold = _cfg.Suggestions.Threshold;
        _learn.Enabled = _cfg.Learning.Enabled;

        AutostartManager.Apply(_cfg.General.Autostart);
        _tray.SetEnabledChecked(_cfg.General.Enabled);
        Core.Log.Line("settings applied");
    }

    private void OnWordRejected(string word)
    {
        _learn.LogRejection(word);
        if (!_suggestions.RegisterRejection(word)) return;
        Dispatcher.BeginInvoke(() => ShowSuggestion(word));
    }

    private void ShowSuggestion(string word)
    {
        try { _suggestionWindow?.Close(); } catch { /* окно уже закрыто */ }

        var w = new SuggestionWindow(word);
        w.Chosen += choice => OnSuggestionChosen(word, choice);
        w.Closed += (_, _) => { if (ReferenceEquals(_suggestionWindow, w)) _suggestionWindow = null; };
        _suggestionWindow = w;
        w.Show();
    }

    private void OnSuggestionChosen(string word, SuggestionChoice choice)
    {
        switch (choice)
        {
            case SuggestionChoice.MyWord:
                _store.AppendWord(_store.MyWordsPath, word);
                _dict.LoadUserLists(_store.MyWordsPath, _store.StopWordsPath, _store.RulesPath);
                break;
            case SuggestionChoice.StopWord:
                _store.AppendWord(_store.StopWordsPath, word);
                _dict.LoadUserLists(_store.MyWordsPath, _store.StopWordsPath, _store.RulesPath);
                break;
            case SuggestionChoice.LayoutRule:
                // Один клик: правило "слово = вариант в другой раскладке".
                string? other = LayoutMap.ToOtherLayout(word);
                if (other != null)
                {
                    _store.AppendRule(word, other);
                    _dict.LoadUserLists(_store.MyWordsPath, _store.StopWordsPath, _store.RulesPath);
                }
                break;
            case SuggestionChoice.Rule:
                var dlg = new RuleDialog(word, (from, to) =>
                {
                    _store.AppendRule(from, to);
                    _dict.LoadUserLists(_store.MyWordsPath, _store.StopWordsPath, _store.RulesPath);
                });
                dlg.Show();
                break;
            case SuggestionChoice.Dismiss:
                break;
        }
        _suggestions.Reset(word);
        Core.Log.Line($"suggestion '{word}' -> {choice}");
    }

    private void OnToggle(ToggleRequest t)
    {
        Core.Log.Line($"TOGGLE backspaces={t.Backspaces} text='{t.Text}' activate={t.ActivateLayout}");

        if (t.ActivateLayout != LayoutTarget.None)
        {
            IntPtr hkl = t.ActivateLayout == LayoutTarget.Ru ? _layout.RuLayout : _layout.EnLayout;
            _layout.Activate(hkl);
        }

        Replacer.Replace(t.Backspaces, t.Text, t.TrailingVk);

        if (_cfg.Tray.BlinkOnAction)
            _tray.Blink();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hookWatchdog?.Stop();
        _hook?.Dispose();
        _mouseHook?.Dispose();
        _tray?.Dispose();
        _sounds?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
