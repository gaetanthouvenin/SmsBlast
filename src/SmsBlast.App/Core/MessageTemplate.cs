using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SmsBlast.App.Core;

/// <summary>
/// Moteur de gabarit minimal : {{Colonne}} est remplacé par la valeur de la
/// colonne du même nom (insensible à la casse et aux espaces).
/// Une valeur par défaut peut être fournie : {{Prénom|cher client}}.
/// </summary>
public static class MessageTemplate
{
    private static readonly Regex Token =
        new(@"\{\{\s*([^{}|]+?)\s*(?:\|\s*([^{}]*?)\s*)?\}\}", RegexOptions.Compiled);

    public static IReadOnlyList<string> Variables(string? template)
    {
        if (string.IsNullOrEmpty(template)) return Array.Empty<string>();
        return Token.Matches(template)
            .Select(m => m.Groups[1].Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string Render(
        string? template,
        IReadOnlyDictionary<string, string> values,
        out List<string> unresolved)
    {
        var missing = new List<string>();
        var result = Token.Replace(template ?? string.Empty, m =>
        {
            var name = m.Groups[1].Value.Trim();
            var fallback = m.Groups[2].Success ? m.Groups[2].Value : null;

            if (values.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v))
                return v.Trim();

            if (fallback != null) return fallback;

            missing.Add(name);
            return string.Empty;
        });

        unresolved = missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // Un remplacement vide laisse souvent des doubles espaces disgracieux.
        return Regex.Replace(result, @"[ \t]{2,}", " ").Trim();
    }

    /// <summary>Construit le dictionnaire colonne -> valeur pour une ligne.</summary>
    public static Dictionary<string, string> RowValues(IReadOnlyList<string> headers, string[] row)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
            dict[headers[i]] = i < row.Length ? row[i] ?? string.Empty : string.Empty;
        return dict;
    }
}
