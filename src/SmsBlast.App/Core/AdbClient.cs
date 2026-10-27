using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SmsBlast.App.Core;

public sealed class AdbResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = string.Empty;
    public string StdErr { get; init; } = string.Empty;
    public bool TimedOut { get; init; }

    public bool Ok => ExitCode == 0 && !TimedOut;
    public string All => (StdOut + "\n" + StdErr).Trim();
}

public sealed class AdbDevice
{
    public string Serial { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;

    public bool Ready => State == "device";

    public override string ToString() =>
        string.IsNullOrEmpty(Model) ? $"{Serial} ({State})" : $"{Model} — {Serial} ({State})";
}

/// <summary>Pilotage du binaire adb.exe par lancement de processus.</summary>
public sealed class AdbClient
{
    public string AdbPath { get; set; } = "adb";

    public static string? Locate()
    {
        var candidates = new List<string>();

        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(local))
            candidates.Add(Path.Combine(local, "Android", "Sdk", "platform-tools", "adb.exe"));

        var profile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrEmpty(profile))
        {
            candidates.Add(Path.Combine(profile, "AppData", "Local", "Android", "Sdk", "platform-tools", "adb.exe"));
            candidates.Add(Path.Combine(profile, "platform-tools", "adb.exe"));
        }

        candidates.Add(@"C:\platform-tools\adb.exe");
        candidates.Add(@"C:\Android\platform-tools\adb.exe");
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "adb.exe"));

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        // Dernier recours : présence dans le PATH.
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var name in new[] { "adb.exe", "adb" })
            {
                try
                {
                    var full = Path.Combine(dir.Trim(), name);
                    if (File.Exists(full)) return full;
                }
                catch (ArgumentException)
                {
                    // Entrée PATH malformée : on l'ignore.
                }
            }
        }

        return null;
    }

    public async Task<AdbResult> RunAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default,
        int timeoutMs = 30000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = AdbPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var a in arguments) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new AdbResult { ExitCode = -1, StdErr = $"Lancement d'adb impossible : {ex.Message}" };
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { /* déjà terminé */ }
            return new AdbResult
            {
                ExitCode = -1,
                TimedOut = true,
                StdErr = cancellationToken.IsCancellationRequested
                    ? "Interrompu."
                    : $"Pas de réponse d'adb après {timeoutMs} ms."
            };
        }

        return new AdbResult
        {
            ExitCode = process.ExitCode,
            StdOut = (await stdout.ConfigureAwait(false)).Trim(),
            StdErr = (await stderr.ConfigureAwait(false)).Trim()
        };
    }

    public async Task<List<AdbDevice>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(new[] { "devices", "-l" }, cancellationToken, 15000)
            .ConfigureAwait(false);

        var devices = new List<AdbDevice>();
        if (!result.Ok) return devices;

        foreach (var line in result.StdOut.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.StartsWith("*", StringComparison.Ordinal)) continue;

            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            var model = parts.FirstOrDefault(p => p.StartsWith("model:", StringComparison.Ordinal));
            devices.Add(new AdbDevice
            {
                Serial = parts[0],
                State = parts[1],
                Model = model?.Substring("model:".Length).Replace('_', ' ') ?? string.Empty
            });
        }

        return devices;
    }
}
