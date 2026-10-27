using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SmsBlast.App.Core;

namespace SmsBlast.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Recipient> _recipients = new();
    private readonly AdbClient _adb = new();
    private readonly SmsRelayClient _relay;

    private Table? _table;
    private string _filePath = string.Empty;
    private bool _loading;
    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();
        _relay = new SmsRelayClient(_adb);

        // Thème : reflète le mode courant sans déclencher Theme_Checked.
        _loading = true;
        (ThemeManager.Mode switch
        {
            ThemeMode.Light => rbThemeLight,
            ThemeMode.Dark => rbThemeDark,
            _ => rbThemeSystem
        }).IsChecked = true;
        _loading = false;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        dgRecipients.ItemsSource = _recipients;

        cboCountry.ItemsSource = PhoneNormalizer.All;
        cboCountry.DisplayMemberPath = nameof(CountryProfile.Label);
        cboCountry.SelectedIndex = 0;

        txtMessage.Text = "Bonjour {{Prénom|}}, ceci est un message de test envoyé depuis SmsBlast.";

        var adbPath = AdbClient.Locate();
        txtAdb.Text = adbPath ?? "adb";
        _adb.AdbPath = txtAdb.Text;

        UpdateSmsInfo();
        UpdateCounts();
        Loaded += async (_, _) => await RefreshDevicesAsync();
    }

    private CountryProfile Country =>
        cboCountry.SelectedItem as CountryProfile ?? PhoneNormalizer.France;

    private string? SelectedSerial => (cboDevice.SelectedItem as AdbDevice)?.Serial;

    // ------------------------------------------------------------- Source

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choisir le fichier de destinataires",
            Filter = "Classeurs et CSV|*.xlsx;*.xlsm;*.csv;*.txt|Excel|*.xlsx;*.xlsm|CSV|*.csv;*.txt|Tous les fichiers|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        _filePath = dialog.FileName;
        txtFile.Text = _filePath;

        try
        {
            _loading = true;
            var sheets = TabularReader.GetSheetNames(_filePath);
            cboSheet.ItemsSource = sheets;
            cboSheet.SelectedIndex = 0;
            cboSheet.IsEnabled = sheets.Count > 1;
        }
        catch (Exception ex)
        {
            Fail("Lecture du fichier impossible", ex);
            return;
        }
        finally
        {
            _loading = false;
        }

        LoadTable(guessPhoneColumn: true);
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        if (_filePath.Length == 0) return;
        LoadTable(guessPhoneColumn: true);
    }

    private void CboSheet_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _filePath.Length == 0) return;
        LoadTable(guessPhoneColumn: true);
    }

    private void LoadTable(bool guessPhoneColumn)
    {
        if (_filePath.Length == 0) return;

        if (!int.TryParse(txtHeaderRow.Text, out var headerRow) || headerRow < 1)
        {
            headerRow = 1;
            txtHeaderRow.Text = "1";
        }

        try
        {
            _table = TabularReader.Read(_filePath, cboSheet.SelectedItem as string, headerRow);
        }
        catch (Exception ex)
        {
            Fail("Lecture du fichier impossible", ex);
            return;
        }

        _loading = true;
        try
        {
            cboPhoneColumn.ItemsSource = _table.Headers;
            lstColumns.ItemsSource = _table.Headers;

            if (guessPhoneColumn)
            {
                var guess = PhoneNormalizer.GuessPhoneColumn(_table.Headers, _table.Rows);
                cboPhoneColumn.SelectedIndex = guess >= 0 ? guess : (_table.Headers.Count > 0 ? 0 : -1);
            }
        }
        finally
        {
            _loading = false;
        }

        lblSourceInfo.Text = $"{_table.Rows.Count} ligne(s), {_table.Headers.Count} colonne(s)";
        Log($"Chargé : {Path.GetFileName(_filePath)} / feuille « {_table.SheetName} » — " +
            $"{_table.Rows.Count} ligne(s), colonnes : {string.Join(", ", _table.Headers)}");

        RebuildRecipients();
    }

    private void CboPhoneColumn_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        RebuildRecipients();
    }

    private void CboCountry_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        RebuildRecipients();
    }

    private void RebuildRecipients()
    {
        _recipients.Clear();

        if (_table == null || cboPhoneColumn.SelectedIndex < 0)
        {
            UpdateCounts();
            return;
        }

        var column = cboPhoneColumn.SelectedIndex;
        var headerLine = int.TryParse(txtHeaderRow.Text, out var h) ? h : 1;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < _table.Rows.Count; i++)
        {
            var row = _table.Rows[i];
            var raw = column < row.Length ? row[column] : string.Empty;
            var phone = PhoneNormalizer.Normalize(raw, Country);

            var detail = phone.Detail;
            var state = phone.Kind switch
            {
                PhoneKind.Invalid => "invalid",
                PhoneKind.Landline => "landline",
                _ => "ok"
            };

            if (phone.IsValid && !seen.Add(phone.E164))
            {
                detail = string.IsNullOrEmpty(detail) ? "doublon" : detail + " · doublon";
                state = "landline";
            }

            _recipients.Add(new Recipient
            {
                LineNumber = headerLine + 1 + i,
                Values = row,
                RawPhone = raw,
                E164 = phone.E164,
                Pretty = phone.IsValid ? PhoneNormalizer.Pretty(phone.E164) : string.Empty,
                Kind = phone.Kind,
                KindText = phone.KindText,
                Detail = detail,
                RowState = state,
                Selected = phone.Kind == PhoneKind.Mobile && !detail.Contains("doublon", StringComparison.Ordinal)
            });
        }

        RenderMessages();
        UpdateCounts();
    }

    // ------------------------------------------------------------ Message

    private void TxtMessage_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSmsInfo();
        RenderMessages();
    }

    private void RenderMessages()
    {
        if (_table == null) return;

        var template = txtMessage.Text;
        var missingGlobal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in _recipients)
        {
            var values = MessageTemplate.RowValues(_table.Headers, r.Values);
            r.Rendered = MessageTemplate.Render(template, values, out var missing);

            foreach (var m in missing) missingGlobal.Add(m);
        }

        if (missingGlobal.Count > 0)
        {
            var unknown = missingGlobal
                .Where(v => !_table.Headers.Contains(v, StringComparer.OrdinalIgnoreCase))
                .ToList();

            lblSourceInfo.Text = unknown.Count > 0
                ? $"⚠ variable inconnue : {string.Join(", ", unknown)}"
                : $"{_table.Rows.Count} ligne(s) — certaines cellules sont vides pour : " +
                  string.Join(", ", missingGlobal);
        }
        else if (_table != null)
        {
            lblSourceInfo.Text = $"{_table.Rows.Count} ligne(s), {_table.Headers.Count} colonne(s)";
        }
    }

    private void UpdateSmsInfo()
    {
        var sample = _recipients.FirstOrDefault()?.Rendered;
        var metrics = SmsInfo.Analyze(string.IsNullOrEmpty(sample) ? txtMessage.Text : sample);
        var prefix = string.IsNullOrEmpty(sample) ? "Gabarit" : "Premier message";
        lblSmsInfo.Text = $"{prefix} : {metrics.Summary}";
    }

    private void LstColumns_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (lstColumns.SelectedItem is not string column) return;

        var token = "{{" + column + "}}";
        var caret = txtMessage.CaretIndex;
        txtMessage.Text = txtMessage.Text.Insert(caret, token);
        txtMessage.CaretIndex = caret + token.Length;
        txtMessage.Focus();
    }

    // ------------------------------------------------------- Sélection

    private void BtnCheckAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _recipients) r.Selected = r.Kind != PhoneKind.Invalid;
        UpdateCounts();
    }

    private void BtnUncheckAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _recipients) r.Selected = false;
        UpdateCounts();
    }

    private void BtnMobilesOnly_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _recipients)
            r.Selected = r.Kind == PhoneKind.Mobile &&
                         !r.Detail.Contains("doublon", StringComparison.Ordinal);
        UpdateCounts();
    }

    private void BtnResetStatus_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _recipients)
        {
            r.Status = string.Empty;
            r.RowState = r.Kind switch
            {
                PhoneKind.Invalid => "invalid",
                PhoneKind.Landline => "landline",
                _ => "ok"
            };
        }
        pbProgress.Value = 0;
        lblProgress.Text = string.Empty;
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        var total = _recipients.Count;
        var selected = _recipients.Count(r => r.Selected);
        var invalid = _recipients.Count(r => r.Kind == PhoneKind.Invalid);
        var sent = _recipients.Count(r => r.RowState == "sent");
        var failed = _recipients.Count(r => r.RowState == "failed");

        lblCounts.Text = $"{total} ligne(s) · {selected} sélectionné(s) · {invalid} numéro(s) invalide(s)" +
                         (sent + failed > 0 ? $" · {sent} envoyé(s) · {failed} échec(s)" : string.Empty);
    }

    // ---------------------------------------------------------------- ADB

    private void BtnBrowseAdb_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Emplacement de adb.exe",
            Filter = "adb|adb.exe|Tous les fichiers|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        txtAdb.Text = dialog.FileName;
        _adb.AdbPath = dialog.FileName;
        _ = RefreshDevicesAsync();
    }

    private async void BtnRefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDevicesAsync();
    }

    private async Task RefreshDevicesAsync()
    {
        _adb.AdbPath = string.IsNullOrWhiteSpace(txtAdb.Text) ? "adb" : txtAdb.Text.Trim();

        List<AdbDevice> devices;
        try
        {
            devices = await _adb.ListDevicesAsync();
        }
        catch (Exception ex)
        {
            lblRelayState.Text = "adb injoignable";
            Log("adb : " + ex.Message);
            return;
        }

        _loading = true;
        cboDevice.ItemsSource = devices;
        cboDevice.SelectedItem = devices.FirstOrDefault(d => d.Ready) ?? devices.FirstOrDefault();
        _loading = false;

        if (devices.Count == 0)
        {
            lblRelayState.Text = "Aucun appareil détecté — vérifier le câble et le débogage USB.";
            Log("adb devices : aucun appareil.");
            return;
        }

        Log("adb devices : " + string.Join(" | ", devices.Select(d => d.ToString())));
        await CheckRelayAsync();
    }

    private void CboDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _ = CheckRelayAsync();
    }

    private async Task CheckRelayAsync()
    {
        var device = cboDevice.SelectedItem as AdbDevice;
        if (device == null) { lblRelayState.Text = string.Empty; return; }

        if (!device.Ready)
        {
            lblRelayState.Text = device.State == "unauthorized"
                ? "Appareil non autorisé — accepter la clé RSA sur le téléphone."
                : $"Appareil à l'état « {device.State} ».";
            return;
        }

        var installed = await _relay.IsInstalledAsync(device.Serial);
        lblRelayState.Text = installed
            ? "Relais installé sur le téléphone."
            : "Relais absent — cliquer sur « Préparer le téléphone ».";
    }

    private async void BtnPrepare_Click(object sender, RoutedEventArgs e)
    {
        var device = cboDevice.SelectedItem as AdbDevice;
        if (device is not { Ready: true })
        {
            MessageBox.Show(this, "Sélectionner d'abord un appareil prêt.", "SmsBlast",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var defaultApk = Path.Combine(AppContext.BaseDirectory, "SmsRelay.apk");
        var dialog = new OpenFileDialog
        {
            Title = "Choisir l'APK du relais (SmsRelay.apk)",
            Filter = "APK|*.apk|Tous les fichiers|*.*",
            FileName = File.Exists(defaultApk) ? defaultApk : "SmsRelay.apk"
        };
        if (dialog.ShowDialog(this) != true) return;

        SetBusy(true);
        try
        {
            Log("Installation de " + dialog.FileName);
            var install = await _relay.InstallAsync(device.Serial, dialog.FileName);
            Log("install : " + install.All);

            if (!install.Ok || install.All.Contains("Failure", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "Installation échouée :\n\n" + install.All, "SmsBlast",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var grant = await _relay.GrantSmsPermissionAsync(device.Serial);
            Log("pm grant : " + (grant.All.Length == 0 ? "OK" : grant.All));

            await _relay.LaunchAppAsync(device.Serial);
            Log("Application relais lancée (sortie de l'état « stopped »).");

            await CheckRelayAsync();

            MessageBox.Show(this,
                grant.Ok && grant.All.Length == 0
                    ? "Téléphone prêt : relais installé et permission SMS accordée."
                    : "Relais installé. La permission n'a pas pu être accordée par adb " +
                      "(certains constructeurs le refusent) : accordez-la depuis l'écran " +
                      "de l'application qui vient de s'ouvrir sur le téléphone.",
                "SmsBlast", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Fail("Préparation du téléphone impossible", ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // -------------------------------------------------------------- Envoi

    private async void BtnSendTest_Click(object sender, RoutedEventArgs e)
    {
        var device = cboDevice.SelectedItem as AdbDevice;
        if (device is not { Ready: true })
        {
            MessageBox.Show(this, "Sélectionner d'abord un appareil prêt.", "SmsBlast",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var phone = PhoneNormalizer.Normalize(txtTestNumber.Text, Country);
        if (!phone.IsValid)
        {
            MessageBox.Show(this, "Numéro de test invalide : " + phone.Detail, "SmsBlast",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var body = _recipients.FirstOrDefault()?.Rendered;
        if (string.IsNullOrWhiteSpace(body)) body = txtMessage.Text;
        if (string.IsNullOrWhiteSpace(body))
        {
            MessageBox.Show(this, "Le message est vide.", "SmsBlast",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetBusy(true);
        try
        {
            var outcome = await _relay.SendAsync(device.Serial, phone.E164, body, "test");
            Log($"TEST → {phone.E164} : {Describe(outcome)}");
            MessageBox.Show(this, Describe(outcome), "SmsBlast",
                MessageBoxButton.OK,
                outcome.Accepted ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BtnSend_Click(object sender, RoutedEventArgs e)
    {
        var dryRun = chkDryRun.IsChecked == true;

        var targets = _recipients
            .Where(r => r.Selected && r.Kind != PhoneKind.Invalid && r.RowState != "sent")
            .ToList();

        if (targets.Count == 0)
        {
            MessageBox.Show(this, "Aucun destinataire sélectionné à envoyer.", "SmsBlast",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (targets.Any(t => string.IsNullOrWhiteSpace(t.Rendered)))
        {
            MessageBox.Show(this, "Certains messages sont vides. Vérifier le gabarit.", "SmsBlast",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AdbDevice? device = null;
        if (!dryRun)
        {
            device = cboDevice.SelectedItem as AdbDevice;
            if (device is not { Ready: true })
            {
                MessageBox.Show(this, "Sélectionner d'abord un appareil prêt.", "SmsBlast",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!await _relay.IsInstalledAsync(device.Serial))
            {
                MessageBox.Show(this, "Le relais n'est pas installé sur ce téléphone.", "SmsBlast",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var segments = targets.Sum(t => SmsInfo.Analyze(t.Rendered).Segments);
            var confirm = MessageBox.Show(this,
                $"Envoyer {targets.Count} SMS ({segments} segment(s) facturé(s)) depuis « {device.Model} » ?\n\n" +
                "Cette action est irréversible.",
                "Confirmer l'envoi", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;
        }

        if (!int.TryParse(txtDelay.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var delay))
            delay = 4000;
        delay = Math.Max(0, delay);

        _cts = new CancellationTokenSource();
        SetBusy(true);
        btnStop.IsEnabled = true;
        pbProgress.Minimum = 0;
        pbProgress.Maximum = targets.Count;
        pbProgress.Value = 0;

        var random = new Random();
        var done = 0;

        try
        {
            if (!dryRun) await _relay.ClearLogAsync(device!.Serial, _cts.Token);

            foreach (var target in targets)
            {
                _cts.Token.ThrowIfCancellationRequested();

                target.RowState = "sending";
                target.Status = dryRun ? "Simulation…" : "Envoi…";

                if (dryRun)
                {
                    var preview = string.Join(" ",
                        SmsRelayClient.BuildSendArguments(device?.Serial, target.E164,
                            target.Rendered, target.LineNumber.ToString(CultureInfo.InvariantCulture)));
                    Log($"[SIMULATION] adb {preview}");
                    Log($"[SIMULATION] {target.E164} ← \"{target.Rendered}\"");
                    target.Status = "Simulé";
                    target.RowState = "ok";
                }
                else
                {
                    var outcome = await _relay.SendAsync(
                        device!.Serial, target.E164, target.Rendered,
                        target.LineNumber.ToString(CultureInfo.InvariantCulture), _cts.Token);

                    if (outcome.Accepted)
                    {
                        target.Status = "Envoyé";
                        target.RowState = "sent";
                    }
                    else
                    {
                        target.Status = "Échec";
                        target.RowState = "failed";
                        target.Detail = Describe(outcome);
                    }

                    Log($"{target.E164} → {Describe(outcome)}");
                }

                done++;
                pbProgress.Value = done;
                lblProgress.Text = $"{done}/{targets.Count}";
                UpdateCounts();

                if (done < targets.Count && delay > 0)
                {
                    // Léger aléa : un rythme parfaitement régulier ressemble à du spam
                    // pour les filtres opérateur.
                    var jitter = delay + random.Next(0, Math.Max(1, delay / 4));
                    await Task.Delay(jitter, _cts.Token);
                }
            }

            if (!dryRun)
            {
                await Task.Delay(2000, _cts.Token);
                await MergeDeliveryLogAsync(device!.Serial, _cts.Token);
            }

            MessageBox.Show(this,
                dryRun
                    ? $"Simulation terminée : {done} message(s) préparé(s). Rien n'a été envoyé."
                    : $"Envoi terminé : {_recipients.Count(r => r.RowState == "sent")} envoyé(s), " +
                      $"{_recipients.Count(r => r.RowState == "failed")} échec(s).",
                "SmsBlast", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            Log("Envoi interrompu par l'utilisateur.");
            foreach (var r in _recipients.Where(x => x.RowState == "sending"))
            {
                r.RowState = "failed";
                r.Status = "Interrompu";
            }
        }
        catch (Exception ex)
        {
            Fail("Erreur pendant l'envoi", ex);
        }
        finally
        {
            btnStop.IsEnabled = false;
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
            UpdateCounts();
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // -------------------------------------------------------------- Thème

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (sender is RadioButton { Tag: string tag } &&
            Enum.TryParse<ThemeMode>(tag, ignoreCase: true, out var mode))
        {
            ThemeManager.SetMode(mode);
        }
    }

    /// <summary>Complète les statuts avec les accusés d'émission relevés dans logcat.</summary>
    private async Task MergeDeliveryLogAsync(string serial, CancellationToken cancellationToken)
    {
        var log = await _relay.ReadDeliveryLogAsync(serial, cancellationToken);
        if (string.IsNullOrWhiteSpace(log)) return;

        var pattern = new Regex(@"SENT id=(?<id>\S+) part=(?<part>\d+)/(?<total>\d+) status=(?<status>\S+)");
        var statuses = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (Match m in pattern.Matches(log))
        {
            var id = m.Groups["id"].Value;
            if (!statuses.TryGetValue(id, out var list))
                statuses[id] = list = new List<string>();
            list.Add(m.Groups["status"].Value);
        }

        foreach (var r in _recipients)
        {
            var id = r.LineNumber.ToString(CultureInfo.InvariantCulture);
            if (!statuses.TryGetValue(id, out var list)) continue;

            if (list.All(s => s == "OK"))
            {
                r.Status = "Remis à l'opérateur";
                r.RowState = "sent";
            }
            else
            {
                r.Status = "Échec opérateur";
                r.RowState = "failed";
                r.Detail = string.Join(", ", list.Distinct());
            }
        }

        Log("Accusés d'émission relevés pour " + statuses.Count + " message(s).");
    }

    private static string Describe(BroadcastOutcome outcome)
    {
        if (!outcome.Parsed)
            return "Réponse inattendue d'adb : " +
                   (outcome.RawOutput.Length == 0 ? "(vide)" : outcome.RawOutput);

        if (outcome.Accepted)
            return "accepté par le relais — " +
                   (outcome.Data.Length == 0 ? "en cours d'émission" : outcome.Data);

        return outcome.Data.Length > 0
            ? $"refusé (code {outcome.Result}) : {outcome.Data}"
            : $"refusé (code {outcome.Result})";
    }

    // ------------------------------------------------------------ Rapport

    private void BtnReport_Click(object sender, RoutedEventArgs e)
    {
        if (_recipients.Count == 0) return;

        var dialog = new SaveFileDialog
        {
            Title = "Enregistrer le rapport d'envoi",
            Filter = "CSV|*.csv",
            FileName = Path.GetFileName(ReportWriter.DefaultReportPath())
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ReportWriter.Write(dialog.FileName, _recipients);
            Log("Rapport écrit : " + dialog.FileName);
        }
        catch (Exception ex)
        {
            Fail("Écriture du rapport impossible", ex);
        }
    }

    // ------------------------------------------------------------ Divers

    private void SetBusy(bool busy)
    {
        btnSend.IsEnabled = !busy;
        btnSendTest.IsEnabled = !busy;
        btnPrepare.IsEnabled = !busy;
        btnBrowse.IsEnabled = !busy;
        btnReload.IsEnabled = !busy;
        btnRefreshDevices.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        txtLog.AppendText(line + Environment.NewLine);
        txtLog.ScrollToEnd();
    }

    private void Fail(string title, Exception ex)
    {
        Log($"{title} : {ex.GetType().Name} — {ex.Message}");
        MessageBox.Show(this, $"{title} :\n\n{ex.Message}", "SmsBlast",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
