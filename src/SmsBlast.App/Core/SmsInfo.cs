using System;

namespace SmsBlast.App.Core;

public sealed class SmsMetrics
{
    public bool Gsm7 { get; init; }
    public int Units { get; init; }
    public int Segments { get; init; }

    public string Summary => Segments == 0
        ? "message vide"
        : $"{Units} caractère{(Units > 1 ? "s" : "")} · {Segments} SMS · " +
          (Gsm7 ? "alphabet GSM" : "Unicode (accents/emoji hors GSM → segments plus courts)");
}

/// <summary>
/// Calcule le nombre de segments SMS facturés, selon que le texte tient
/// dans l'alphabet GSM 03.38 (160/153 caractères) ou impose l'UCS-2 (70/67).
/// </summary>
public static class SmsInfo
{
    private const string Gsm7Base =
        "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?" +
        "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";

    private const string Gsm7Extended = "^{}\\[~]|€";

    public static SmsMetrics Analyze(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return new SmsMetrics { Gsm7 = true, Units = 0, Segments = 0 };

        var gsm7 = true;
        var units = 0;

        foreach (var ch in text)
        {
            if (Gsm7Extended.IndexOf(ch) >= 0) units += 2;
            else if (Gsm7Base.IndexOf(ch) >= 0) units += 1;
            else { gsm7 = false; break; }
        }

        if (!gsm7)
        {
            // UCS-2 : on compte les unités UTF-16 (un emoji hors BMP en occupe deux).
            units = text.Length;
            var segments = units <= 70 ? 1 : (int)Math.Ceiling(units / 67.0);
            return new SmsMetrics { Gsm7 = false, Units = units, Segments = segments };
        }

        var seg = units <= 160 ? 1 : (int)Math.Ceiling(units / 153.0);
        return new SmsMetrics { Gsm7 = true, Units = units, Segments = seg };
    }
}
