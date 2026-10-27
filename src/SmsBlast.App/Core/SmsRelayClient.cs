using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SmsBlast.App.Core;

public sealed class BroadcastOutcome
{
    public bool Parsed { get; init; }
    public int Result { get; init; } = -999;
    public string Data { get; init; } = string.Empty;
    public string RawOutput { get; init; } = string.Empty;

    /// <summary>Le relais a accepté l'ordre et confié le SMS au framework télécom.</summary>
    public bool Accepted => Parsed && Result == 0;
}

/// <summary>Dialogue avec l'application relais installée sur le téléphone.</summary>
public sealed class SmsRelayClient
{
    public const string PackageName = "com.smsblast.relay";
    public const string Component = PackageName + "/.SendSmsReceiver";
    public const string SendAction = PackageName + ".action.SEND";

    /// <summary>FLAG_INCLUDE_STOPPED_PACKAGES : indispensable si l'app n'a jamais été lancée.</summary>
    private const string IncludeStoppedPackages = "0x00000020";

    private static readonly Regex BroadcastResult =
        new(@"result=(-?\d+)(?:\s*,\s*data=""(?<data>[^""]*)"")?", RegexOptions.Compiled);

    private readonly AdbClient _adb;

    public SmsRelayClient(AdbClient adb) => _adb = adb;

    /// <summary>
    /// Construit la ligne de commande d'envoi. Numéro et message voyagent en
    /// base64 URL-safe : aucun caractère spécial ne peut casser le shell Android.
    /// </summary>
    public static string[] BuildSendArguments(string? serial, string e164, string message, string id)
    {
        var args = new List<string>();
        if (!string.IsNullOrEmpty(serial)) { args.Add("-s"); args.Add(serial); }

        args.Add("shell");
        args.Add("am");
        args.Add("broadcast");
        args.Add("-n"); args.Add(Component);
        args.Add("-a"); args.Add(SendAction);
        args.Add("-f"); args.Add(IncludeStoppedPackages);
        args.Add("--es"); args.Add("id"); args.Add(id);
        args.Add("--es"); args.Add("num_b64"); args.Add(Base64Url.Encode(e164));
        args.Add("--es"); args.Add("msg_b64"); args.Add(Base64Url.Encode(message));
        return args.ToArray();
    }

    public static BroadcastOutcome ParseBroadcast(string output)
    {
        var match = BroadcastResult.Match(output ?? string.Empty);
        if (!match.Success)
            return new BroadcastOutcome { Parsed = false, RawOutput = output ?? string.Empty };

        return new BroadcastOutcome
        {
            Parsed = true,
            Result = int.Parse(match.Groups[1].Value),
            Data = match.Groups["data"].Success ? match.Groups["data"].Value : string.Empty,
            RawOutput = output ?? string.Empty
        };
    }

    public async Task<BroadcastOutcome> SendAsync(
        string? serial, string e164, string message, string id,
        CancellationToken cancellationToken = default)
    {
        var result = await _adb
            .RunAsync(BuildSendArguments(serial, e164, message, id), cancellationToken, 30000)
            .ConfigureAwait(false);

        if (result.TimedOut || result.ExitCode != 0)
            return new BroadcastOutcome { Parsed = false, RawOutput = result.All };

        return ParseBroadcast(result.All);
    }

    public async Task<bool> IsInstalledAsync(string? serial, CancellationToken cancellationToken = default)
    {
        var args = new List<string>();
        if (!string.IsNullOrEmpty(serial)) { args.Add("-s"); args.Add(serial); }
        args.AddRange(new[] { "shell", "pm", "list", "packages", PackageName });

        var result = await _adb.RunAsync(args, cancellationToken, 15000).ConfigureAwait(false);
        return result.Ok && result.StdOut.Contains(PackageName, StringComparison.Ordinal);
    }

    public async Task<AdbResult> InstallAsync(string? serial, string apkPath, CancellationToken cancellationToken = default)
    {
        var args = new List<string>();
        if (!string.IsNullOrEmpty(serial)) { args.Add("-s"); args.Add(serial); }
        args.AddRange(new[] { "install", "-r", "-g", apkPath });
        return await _adb.RunAsync(args, cancellationToken, 180000).ConfigureAwait(false);
    }

    public async Task<AdbResult> GrantSmsPermissionAsync(string? serial, CancellationToken cancellationToken = default)
    {
        var args = new List<string>();
        if (!string.IsNullOrEmpty(serial)) { args.Add("-s"); args.Add(serial); }
        args.AddRange(new[] { "shell", "pm", "grant", PackageName, "android.permission.SEND_SMS" });
        return await _adb.RunAsync(args, cancellationToken, 20000).ConfigureAwait(false);
    }

    public async Task<AdbResult> LaunchAppAsync(string? serial, CancellationToken cancellationToken = default)
    {
        var args = new List<string>();
        if (!string.IsNullOrEmpty(serial)) { args.Add("-s"); args.Add(serial); }
        args.AddRange(new[] { "shell", "am", "start", "-n", PackageName + "/.MainActivity" });
        return await _adb.RunAsync(args, cancellationToken, 20000).ConfigureAwait(false);
    }

    public async Task<AdbResult> ClearLogAsync(string? serial, CancellationToken cancellationToken = default)
    {
        var args = new List<string>();
        if (!string.IsNullOrEmpty(serial)) { args.Add("-s"); args.Add(serial); }
        args.AddRange(new[] { "logcat", "-c" });
        return await _adb.RunAsync(args, cancellationToken, 15000).ConfigureAwait(false);
    }

    /// <summary>Relit les accusés d'émission tracés par le relais dans logcat.</summary>
    public async Task<string> ReadDeliveryLogAsync(string? serial, CancellationToken cancellationToken = default)
    {
        var args = new List<string>();
        if (!string.IsNullOrEmpty(serial)) { args.Add("-s"); args.Add(serial); }
        args.AddRange(new[] { "logcat", "-d", "-s", "SMSRELAY" });

        var result = await _adb.RunAsync(args, cancellationToken, 20000).ConfigureAwait(false);
        return result.All;
    }
}
