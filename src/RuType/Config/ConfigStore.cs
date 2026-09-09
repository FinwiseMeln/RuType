using System.IO;
using System.Text.Json;

namespace RuType.Config;

/// <summary>
/// Каталог данных в %APPDATA%\RuType\ и чтение/запись config.json.
/// Пользовательские списки (my_words/stopwords/rules) - текстовые файлы рядом.
/// </summary>
public sealed class ConfigStore
{
    public string DataDir { get; }
    public string ConfigPath => Path.Combine(DataDir, "config.json");
    public string MyWordsPath => Path.Combine(DataDir, "my_words.txt");
    public string StopWordsPath => Path.Combine(DataDir, "stopwords.txt");
    public string RulesPath => Path.Combine(DataDir, "rules.txt");

    /// <summary>Каталог приложения (там лежат assets/ и dict/).</summary>
    public string AppDir { get; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public ConfigStore()
    {
        // Портабл: все данные пользователя в подпапке data рядом с exe.
        AppDir = AppContext.BaseDirectory;
        DataDir = Path.Combine(AppDir, "data");
        Directory.CreateDirectory(DataDir);
    }

    public AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var fresh = new AppConfig();
            Save(fresh);
            return fresh;
        }

        try
        {
            string json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? new AppConfig();
        }
        catch
        {
            // Битый конфиг не должен ронять программу - откатываемся к дефолтам.
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        string json = JsonSerializer.Serialize(config, JsonOpts);
        File.WriteAllText(ConfigPath, json);
    }

    /// <summary>Гарантирует существование пользовательских текстовых списков.</summary>
    public void EnsureUserLists()
    {
        if (!File.Exists(MyWordsPath))
            File.WriteAllText(MyWordsPath, "# Мой словарь: корректные слова, по одному на строку\n");
        if (!File.Exists(StopWordsPath))
            File.WriteAllText(StopWordsPath, "# Стоп-слова: не трогать, по одному на строку\n");
        if (!File.Exists(RulesPath))
            File.WriteAllText(RulesPath, "# Правила замены: опечатка = исправление\n");
    }

    /// <summary>Дозаписать слово в список (если его там ещё нет). Возвращает true, если добавлено.</summary>
    public bool AppendWord(string path, string word)
    {
        word = word.Trim();
        if (word.Length == 0) return false;

        if (File.Exists(path))
        {
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                if (string.Equals(line, word, StringComparison.OrdinalIgnoreCase))
                    return false; // уже есть
            }
        }

        // Гарантируем перевод строки перед добавлением.
        if (File.Exists(path))
        {
            string existing = File.ReadAllText(path);
            if (existing.Length > 0 && !existing.EndsWith('\n'))
                File.AppendAllText(path, "\n");
        }
        File.AppendAllText(path, word + "\n");
        return true;
    }

    /// <summary>Дозаписать правило замены "from = to" (если такого ещё нет).</summary>
    public void AppendRule(string from, string to)
    {
        from = from.Trim();
        to = to.Trim();
        if (from.Length == 0 || to.Length == 0) return;

        if (File.Exists(RulesPath))
        {
            foreach (var raw in File.ReadLines(RulesPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (string.Equals(line[..eq].Trim(), from, StringComparison.OrdinalIgnoreCase))
                    return; // правило для этого слова уже есть
            }
            string existing = File.ReadAllText(RulesPath);
            if (existing.Length > 0 && !existing.EndsWith('\n'))
                File.AppendAllText(RulesPath, "\n");
        }
        File.AppendAllText(RulesPath, $"{from} = {to}\n");
    }

    /// <summary>Превращает относительный путь из конфига в абсолютный (от каталога приложения).</summary>
    public string ResolveAppPath(string relativeOrAbsolute)
    {
        if (Path.IsPathRooted(relativeOrAbsolute))
            return relativeOrAbsolute;
        return Path.Combine(AppDir, relativeOrAbsolute);
    }
}
