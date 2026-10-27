using System;
using System.Text;

namespace SmsBlast.App.Core;

/// <summary>
/// Encodage base64 « URL-safe » sans padding : le résultat ne contient que
/// [A-Za-z0-9-_], donc aucun caractère à échapper lors du passage par
/// `adb shell am broadcast`. Décodé côté Android avec
/// Base64.URL_SAFE | Base64.NO_PADDING | Base64.NO_WRAP.
/// </summary>
public static class Base64Url
{
    public static string Encode(string? value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string Decode(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var s = value.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
}
