using System.Text;
using RuType.Config;

namespace RuType.Core;

/// <summary>
/// Служебный режим: что ядро думает о конкретных словах. Для каждого слова
/// печатает вердикты всех оракулов (Hunspell ru/en, ru_extra плоский и аффиксный,
/// en_extra, пользовательские списки), частоту и решение SuggestTypo.
///
/// Нужен при наполнении словарей-дополнений: проверить, что лемма с аффиксным
/// флагом действительно раскрылась во всю парадигму ("модалка/I" -> "модалкой").
///
/// Запуск: RuType.exe --check слово1 слово2 ...
///         RuType.exe --check @файл_со_словами
/// </summary>
public static class WordProbe
{
    public static int Run(IReadOnlyList<string> words)
    {
        var store = new ConfigStore();
        var cfg = store.Load();
        store.EnsureUserLists();

        var dict = new Dictionaries();
        string ruDic = store.ResolveAppPath(cfg.Dictionaries.RuHunspell);
        string ruAff = System.IO.Path.ChangeExtension(ruDic, ".aff");
        bool ruOk = dict.LoadRuHunspell(ruDic);
        dict.LoadEnHunspell(store.ResolveAppPath(cfg.Dictionaries.EnHunspell));
        dict.LoadFreq(store.ResolveAppPath(cfg.Dictionaries.RuFreq));
        dict.LoadExtra(store.ResolveAppPath(cfg.Dictionaries.RuExtra));
        bool extraDicOk = dict.LoadRuExtraHunspell(store.ResolveAppPath(cfg.Dictionaries.RuExtraDic), ruAff);
        bool enExtraOk = dict.LoadEnExtra(store.ResolveAppPath(cfg.Dictionaries.EnExtra));
        dict.LoadUserLists(store.MyWordsPath, store.StopWordsPath, store.RulesPath);
        if (cfg.Ngram.Enabled && ruOk)
            dict.SetNgram(NgramModel.BuildOrLoad(ruDic, store.ResolveAppPath(cfg.Dictionaries.RuFreq),
                System.IO.Path.Combine(store.DataDir, cfg.Ngram.CacheFile), cfg.Ngram.WeightByFrequency));

        var analyzer = new Analyzer(dict, cfg) { LayoutDetectionEnabled = dict.EnLoaded };

        var expanded = new List<string>();
        foreach (var w in words)
        {
            if (w.StartsWith('@') && System.IO.File.Exists(w[1..]))
            {
                foreach (var line in System.IO.File.ReadLines(w[1..]))
                {
                    string t = line.Trim();
                    if (t.Length > 0 && !t.StartsWith('#')) expanded.Add(t);
                }
            }
            else expanded.Add(w);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"ru_extra.dic: {(extraDicOk ? "загружен" : "НЕТ")}, en_extra: {(enExtraOk ? $"{dict.EnExtraWordsCount} слов" : "НЕТ")}");
        sb.AppendLine($"{"слово",-20} {"validRu",-8} {"validEn",-8} {"частота",-9} решение");
        sb.AppendLine(new string('-', 72));
        int bad = 0;
        foreach (var w in expanded)
        {
            string lower = w.ToLowerInvariant();
            var d = analyzer.Analyze(w);
            string verdict = d.Kind == ActionKind.None ? "-" : $"{d.Kind} => '{d.Replacement}'";
            if (d.Kind != ActionKind.None) bad++;
            sb.AppendLine($"{w,-20} {dict.IsValidRu(lower),-8} {dict.IsValidEn(lower),-8} {dict.Frequency(lower),-9} {verdict}");
        }
        sb.AppendLine(new string('-', 72));
        sb.AppendLine($"слов: {expanded.Count}, из них ядро тронуло бы: {bad}");

        string report = sb.ToString();
        string outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rutype_word_probe.txt");
        System.IO.File.WriteAllText(outPath, report);
        Console.WriteLine(report);
        Console.WriteLine($"[report -> {outPath}]");
        return 0;
    }
}
