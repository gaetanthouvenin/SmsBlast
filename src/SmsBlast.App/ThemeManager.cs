using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace SmsBlast.App;

public enum ThemeMode
{
    /// <summary>Suit le réglage « Couleur par défaut des applications » de Windows.</summary>
    System,
    Light,
    Dark
}

/// <summary>
/// Bascule entre Themes/Theme.Light.xaml et Themes/Theme.Dark.xaml à chaud.
/// Les styles référencent les pinceaux en DynamicResource : remplacer le
/// dictionnaire fusionné suffit à repeindre toute l'interface.
/// </summary>
public static class ThemeManager
{
    private const string ThemeFolder = "Themes/Theme.";

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SmsBlast", "settings.txt");

    public static ThemeMode Mode { get; private set; } = ThemeMode.System;

    /// <summary>Thème réellement appliqué, une fois le mode « Système » résolu.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Levé après chaque changement de thème effectif.</summary>
    public static event Action? Changed;

    public static void Initialize()
    {
        Mode = Load();
        Apply();

        // Windows notifie un changement de préférence quand l'utilisateur bascule
        // clair/sombre dans les Paramètres : on suit en mode « Système ».
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General || Mode != ThemeMode.System) return;
            Application.Current?.Dispatcher.BeginInvoke(new Action(Apply));
        };
    }

    public static void SetMode(ThemeMode mode)
    {
        if (mode == Mode) return;
        Mode = mode;
        Save();
        Apply();
    }

    private static void Apply()
    {
        var dark = Mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemUsesDarkTheme()
        };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var uri = new Uri(ThemeFolder + (dark ? "Dark" : "Light") + ".xaml", UriKind.Relative);

        // Un seul dictionnaire de thème à la fois.
        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            var source = dictionaries[i].Source?.OriginalString ?? string.Empty;
            if (source.Contains(ThemeFolder, StringComparison.OrdinalIgnoreCase))
                dictionaries.RemoveAt(i);
        }
        dictionaries.Insert(0, new ResourceDictionary { Source = uri });

        IsDark = dark;

        foreach (Window window in Application.Current.Windows)
            ApplyTitleBar(window);

        Changed?.Invoke();
    }

    /// <summary>Lit HKCU\...\Themes\Personalize\AppsUseLightTheme (1 = clair).</summary>
    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    // ----------------------------------------------------------- Barre de titre

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>Peint la barre de titre native en sombre ou en clair (Windows 10 1809+).</summary>
    public static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var value = IsDark ? 1 : 0;
        try
        {
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
        }
        catch
        {
            // Versions de Windows sans cet attribut : la barre reste claire.
        }
    }

    // ----------------------------------------------------------- Persistance

    private static ThemeMode Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return ThemeMode.System;
            foreach (var line in File.ReadAllLines(SettingsPath))
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2 && parts[0].Trim() == "theme" &&
                    Enum.TryParse<ThemeMode>(parts[1].Trim(), ignoreCase: true, out var mode))
                    return mode;
            }
        }
        catch
        {
            // Fichier illisible : on repart du mode Système.
        }
        return ThemeMode.System;
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, "theme=" + Mode.ToString().ToLowerInvariant() + Environment.NewLine);
        }
        catch
        {
            // Pas bloquant : le choix ne sera simplement pas mémorisé.
        }
    }
}
