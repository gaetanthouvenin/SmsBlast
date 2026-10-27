using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SmsBlast.App.Core;

/// <summary>Rapport d'envoi en CSV point-virgule, UTF-8 avec BOM (ouvrable tel quel dans Excel FR).</summary>
public static class ReportWriter
{
    public static void Write(string path, IEnumerable<Recipient> recipients)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ligne;Numero source;Numero E164;Type;Selectionne;Statut;Detail;Message");

        foreach (var r in recipients)
        {
            sb.Append(r.LineNumber).Append(';')
              .Append(Escape(r.RawPhone)).Append(';')
              .Append(Escape(r.E164)).Append(';')
              .Append(Escape(r.KindText)).Append(';')
              .Append(r.Selected ? "oui" : "non").Append(';')
              .Append(Escape(r.Status)).Append(';')
              .Append(Escape(r.Detail)).Append(';')
              .Append(Escape(r.Rendered))
              .AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Escape(string? value)
    {
        var v = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        if (v.Contains('"')) v = v.Replace("\"", "\"\"");
        return v.Contains(';') || v.Contains('"') ? $"\"{v}\"" : v;
    }

    public static string DefaultReportPath()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var name = $"smsblast-rapport-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
        return Path.Combine(string.IsNullOrEmpty(dir) ? AppContext.BaseDirectory : dir, name);
    }
}
