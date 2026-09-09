namespace RuType.Core;

/// <summary>
/// Подгонка регистра результата под исходный ввод (ТЗ, раздел 10):
/// ВСЕ ПРОПИСНЫЕ -> результат прописными; Первая прописная -> капитализация;
/// иначе как есть (нижний регистр результата).
/// </summary>
public static class CaseHelper
{
    public static string ApplyCasePattern(string source, string replacementLower)
    {
        if (string.IsNullOrEmpty(replacementLower)) return replacementLower;
        if (string.IsNullOrEmpty(source)) return replacementLower;

        if (IsAllUpper(source))
            return replacementLower.ToUpperInvariant();

        if (char.IsUpper(source[0]))
            return char.ToUpperInvariant(replacementLower[0]) + replacementLower[1..];

        return replacementLower;
    }

    private static bool IsAllUpper(string s)
    {
        bool hasLetter = false;
        foreach (char c in s)
        {
            if (char.IsLetter(c))
            {
                hasLetter = true;
                if (!char.IsUpper(c)) return false;
            }
        }
        return hasLetter;
    }
}
