using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SmsBlast.App.Core;

public enum PhoneKind
{
    Mobile,
    Landline,
    Foreign,
    Invalid
}

public sealed class PhoneResult
{
    public string E164 { get; init; } = string.Empty;
    public PhoneKind Kind { get; init; } = PhoneKind.Invalid;
    public string Detail { get; init; } = string.Empty;

    public bool IsValid => Kind != PhoneKind.Invalid;

    public string KindText => Kind switch
    {
        PhoneKind.Mobile => "Mobile",
        PhoneKind.Landline => "Fixe",
        PhoneKind.Foreign => "Étranger",
        _ => "Invalide"
    };
}

public sealed class CountryProfile
{
    public string Code { get; init; } = "";
    public string Label { get; init; } = "";
    public string Dial { get; init; } = "";
    public bool TrunkZero { get; init; }
    public string[] MobilePrefixes { get; init; } = Array.Empty<string>();
    public int MinNsn { get; init; }
    public int MaxNsn { get; init; }

    public override string ToString() => Label;
}

/// <summary>
/// Normalisation de numéros vers le format E.164 (+33612345678).
/// Volontairement limité aux pays francophones courants plutôt que d'embarquer
/// libphonenumber : la logique tient en quelques règles et reste lisible.
/// </summary>
public static class PhoneNormalizer
{
    public static readonly CountryProfile France = new()
    {
        Code = "FR", Label = "France (+33)", Dial = "33", TrunkZero = true,
        MobilePrefixes = new[] { "6", "7" }, MinNsn = 9, MaxNsn = 9
    };

    public static readonly CountryProfile Belgium = new()
    {
        Code = "BE", Label = "Belgique (+32)", Dial = "32", TrunkZero = true,
        MobilePrefixes = new[] { "4" }, MinNsn = 8, MaxNsn = 9
    };

    public static readonly CountryProfile Switzerland = new()
    {
        Code = "CH", Label = "Suisse (+41)", Dial = "41", TrunkZero = true,
        MobilePrefixes = new[] { "7" }, MinNsn = 9, MaxNsn = 9
    };

    public static readonly CountryProfile Luxembourg = new()
    {
        Code = "LU", Label = "Luxembourg (+352)", Dial = "352", TrunkZero = false,
        MobilePrefixes = new[] { "6" }, MinNsn = 8, MaxNsn = 9
    };

    /// <summary>Aucun préfixe implicite : le fichier doit déjà contenir des numéros internationaux.</summary>
    public static readonly CountryProfile International = new()
    {
        Code = "INTL", Label = "Aucun (numéros déjà internationaux)", Dial = "", TrunkZero = false,
        MobilePrefixes = Array.Empty<string>(), MinNsn = 0, MaxNsn = 0
    };

    public static readonly CountryProfile[] All =
    {
        France, Belgium, Switzerland, Luxembourg, International
    };

    public static PhoneResult Normalize(string? raw, CountryProfile country)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Invalid("cellule vide");

        var trimmed = raw.Trim().TrimStart('\'');

        // Un numéro passé par une colonne « nombre » d'Excel ressort parfois en
        // notation scientifique : mieux vaut le signaler que de deviner.
        if (trimmed.IndexOf("E+", StringComparison.OrdinalIgnoreCase) >= 0)
            return Invalid("notation scientifique — passer la colonne en Texte dans Excel");

        var plus = trimmed.StartsWith("+", StringComparison.Ordinal);
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());

        if (digits.Length == 0)
            return Invalid("aucun chiffre");

        if (!plus && digits.StartsWith("00", StringComparison.Ordinal))
        {
            plus = true;
            digits = digits.Substring(2);
        }

        string e164;
        if (plus)
        {
            if (digits.Length is < 8 or > 15 || digits[0] == '0')
                return Invalid($"longueur invalide (+{digits})");
            e164 = "+" + digits;
        }
        else
        {
            if (country.Dial.Length == 0)
                return Invalid("pas d'indicatif pays et aucun pays par défaut choisi");

            var nsn = digits;
            if (country.TrunkZero && nsn.StartsWith("0", StringComparison.Ordinal))
                nsn = nsn.Substring(1);

            if (nsn.Length < country.MinNsn || nsn.Length > country.MaxNsn)
                return Invalid($"{nsn.Length} chiffres, {Expected(country)} attendus");

            e164 = "+" + country.Dial + nsn;
        }

        return Classify(e164, country);
    }

    private static string Expected(CountryProfile c) =>
        c.MinNsn == c.MaxNsn ? c.MinNsn.ToString() : $"{c.MinNsn} à {c.MaxNsn}";

    private static PhoneResult Classify(string e164, CountryProfile country)
    {
        foreach (var c in All)
        {
            if (c.Dial.Length == 0) continue;
            if (!e164.StartsWith("+" + c.Dial, StringComparison.Ordinal)) continue;

            var nsn = e164.Substring(1 + c.Dial.Length);
            var mobile = c.MobilePrefixes.Any(p => nsn.StartsWith(p, StringComparison.Ordinal));

            // Un numéro reconnu comme appartenant à un autre pays que celui choisi
            // reste valide, il est simplement signalé.
            var kind = mobile ? PhoneKind.Mobile : PhoneKind.Landline;
            var detail = c.Code == country.Code || country.Dial.Length == 0
                ? (mobile ? "" : "ligne fixe : ne recevra pas de SMS")
                : $"numéro {c.Code}" + (mobile ? "" : ", ligne fixe");

            return new PhoneResult { E164 = e164, Kind = kind, Detail = detail };
        }

        return new PhoneResult
        {
            E164 = e164,
            Kind = PhoneKind.Foreign,
            Detail = "indicatif non reconnu — vérifier avant envoi"
        };
    }

    private static PhoneResult Invalid(string detail) =>
        new() { E164 = string.Empty, Kind = PhoneKind.Invalid, Detail = detail };

    /// <summary>Affichage lisible : +33 6 12 34 56 78.</summary>
    public static string Pretty(string e164)
    {
        if (string.IsNullOrEmpty(e164) || !e164.StartsWith("+", StringComparison.Ordinal))
            return e164;

        foreach (var c in All)
        {
            if (c.Dial.Length == 0) continue;
            if (!e164.StartsWith("+" + c.Dial, StringComparison.Ordinal)) continue;

            var nsn = e164.Substring(1 + c.Dial.Length);
            var sb = new StringBuilder("+" + c.Dial + " ");
            if (nsn.Length % 2 == 1)
            {
                sb.Append(nsn[0]).Append(' ');
                nsn = nsn.Substring(1);
            }
            for (var i = 0; i < nsn.Length; i += 2)
            {
                sb.Append(nsn.Substring(i, Math.Min(2, nsn.Length - i)));
                if (i + 2 < nsn.Length) sb.Append(' ');
            }
            return sb.ToString();
        }
        return e164;
    }

    /// <summary>Détecte la colonne la plus probablement porteuse de numéros.</summary>
    public static int GuessPhoneColumn(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        string[] hints = { "portable", "mobile", "gsm", "tel", "tél", "phone", "numero", "numéro", "cell" };

        for (var i = 0; i < headers.Count; i++)
        {
            var h = headers[i].ToLowerInvariant();
            if (hints.Any(x => h.Contains(x))) return i;
        }

        // Sinon : la colonne dont le plus de cellules se normalisent en mobile.
        var best = -1;
        var bestScore = 0;
        for (var i = 0; i < headers.Count; i++)
        {
            var score = rows.Take(50).Count(r =>
                i < r.Length && Normalize(r[i], France).Kind == PhoneKind.Mobile);
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return bestScore > 0 ? best : -1;
    }
}
