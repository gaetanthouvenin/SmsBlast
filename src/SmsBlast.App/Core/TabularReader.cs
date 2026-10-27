using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SmsBlast.App.Core;

public sealed class Table
{
    public string SheetName { get; set; } = string.Empty;
    public List<string> Headers { get; } = new();
    public List<string[]> Rows { get; } = new();
}

/// <summary>
/// Lecture de .xlsx et .csv sans dépendance externe.
/// Le .xlsx est un ZIP de XML OpenXML : on lit workbook.xml, sharedStrings.xml,
/// styles.xml (pour reconnaître les dates) et la feuille demandée.
/// Pour passer à ClosedXML ou EPPlus, seul ce fichier est à remplacer.
/// </summary>
public static class TabularReader
{
    private static readonly XNamespace Main =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRel =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    public static bool IsSpreadsheet(string path) =>
        Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".xlsm", StringComparison.OrdinalIgnoreCase);

    public static List<string> GetSheetNames(string path)
    {
        if (!IsSpreadsheet(path)) return new List<string> { Path.GetFileName(path) };

        using var zip = ZipFile.OpenRead(path);
        return ReadSheetEntries(zip).Select(s => s.Name).ToList();
    }

    public static Table Read(string path, string? sheetName, int headerRow)
    {
        if (headerRow < 1) headerRow = 1;
        var table = IsSpreadsheet(path) ? ReadXlsx(path, sheetName) : ReadCsv(path);
        return Finalize(table, headerRow);
    }

    // ---------------------------------------------------------------- XLSX

    private sealed record SheetEntry(string Name, string EntryPath);

    private static List<SheetEntry> ReadSheetEntries(ZipArchive zip)
    {
        var workbook = LoadXml(zip, "xl/workbook.xml")
            ?? throw new InvalidDataException("Fichier .xlsx invalide : xl/workbook.xml introuvable.");

        var rels = LoadXml(zip, "xl/_rels/workbook.xml.rels");
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (rels != null)
        {
            foreach (var r in rels.Root!.Elements(PkgRel + "Relationship"))
            {
                var id = (string?)r.Attribute("Id");
                var target = (string?)r.Attribute("Target");
                if (id != null && target != null) map[id] = NormalizeTarget(target);
            }
        }

        var result = new List<SheetEntry>();
        var sheets = workbook.Root!.Element(Main + "sheets");
        if (sheets == null) return result;

        var index = 1;
        foreach (var sheet in sheets.Elements(Main + "sheet"))
        {
            var name = (string?)sheet.Attribute("name") ?? $"Feuille{index}";
            var rid = (string?)sheet.Attribute(Rel + "id");
            var entry = rid != null && map.TryGetValue(rid, out var t)
                ? t
                : $"xl/worksheets/sheet{index}.xml";
            result.Add(new SheetEntry(name, entry));
            index++;
        }
        return result;
    }

    private static string NormalizeTarget(string target)
    {
        var t = target.Replace('\\', '/');
        if (t.StartsWith("/", StringComparison.Ordinal)) return t.TrimStart('/');
        if (t.StartsWith("../", StringComparison.Ordinal)) return t.Substring(3);
        return "xl/" + t;
    }

    private static Table ReadXlsx(string path, string? sheetName)
    {
        using var zip = ZipFile.OpenRead(path);

        var sheets = ReadSheetEntries(zip);
        if (sheets.Count == 0)
            throw new InvalidDataException("Aucune feuille trouvée dans le classeur.");

        var target = sheetName == null
            ? sheets[0]
            : sheets.FirstOrDefault(s => s.Name == sheetName) ?? sheets[0];

        var shared = ReadSharedStrings(zip);
        var dateStyles = ReadDateStyles(zip);

        var doc = LoadXml(zip, target.EntryPath)
            ?? throw new InvalidDataException($"Feuille « {target.Name} » illisible.");

        var table = new Table { SheetName = target.Name };
        var data = doc.Root!.Element(Main + "sheetData");
        if (data == null) return table;

        foreach (var row in data.Elements(Main + "row"))
        {
            var cells = new SortedDictionary<int, string>();
            foreach (var c in row.Elements(Main + "c"))
            {
                var reference = (string?)c.Attribute("r");
                var col = reference != null ? ColumnIndex(reference) : cells.Count;
                var text = CellText(c, shared, dateStyles);
                if (!string.IsNullOrEmpty(text)) cells[col] = text;
            }

            if (cells.Count == 0)
            {
                table.Rows.Add(Array.Empty<string>());
                continue;
            }

            var width = cells.Keys.Max() + 1;
            var array = new string[width];
            for (var i = 0; i < width; i++)
                array[i] = cells.TryGetValue(i, out var v) ? v : string.Empty;
            table.Rows.Add(array);
        }

        return table;
    }

    private static XDocument? LoadXml(ZipArchive zip, string entryPath)
    {
        var entry = zip.GetEntry(entryPath)
            ?? zip.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, entryPath, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return null;

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var result = new List<string>();
        var doc = LoadXml(zip, "xl/sharedStrings.xml");
        if (doc?.Root == null) return result;

        foreach (var si in doc.Root.Elements(Main + "si"))
        {
            var sb = new StringBuilder();
            foreach (var t in si.Elements(Main + "t")) sb.Append(t.Value);
            foreach (var r in si.Elements(Main + "r"))
                foreach (var t in r.Elements(Main + "t")) sb.Append(t.Value);
            result.Add(sb.ToString());
        }
        return result;
    }

    private static readonly int[] BuiltInDateFormats =
    {
        14, 15, 16, 17, 18, 19, 20, 21, 22,
        27, 28, 29, 30, 31, 32, 33, 34, 35, 36,
        45, 46, 47, 50, 51, 52, 53, 54, 55, 56, 57, 58
    };

    /// <summary>Indique, pour chaque index de style, s'il représente une date.</summary>
    private static bool[] ReadDateStyles(ZipArchive zip)
    {
        var doc = LoadXml(zip, "xl/styles.xml");
        if (doc?.Root == null) return Array.Empty<bool>();

        var custom = new Dictionary<int, string>();
        var numFmts = doc.Root.Element(Main + "numFmts");
        if (numFmts != null)
        {
            foreach (var f in numFmts.Elements(Main + "numFmt"))
            {
                if (int.TryParse((string?)f.Attribute("numFmtId"), out var id))
                    custom[id] = (string?)f.Attribute("formatCode") ?? string.Empty;
            }
        }

        var cellXfs = doc.Root.Element(Main + "cellXfs");
        if (cellXfs == null) return Array.Empty<bool>();

        var xfs = cellXfs.Elements(Main + "xf").ToList();
        var result = new bool[xfs.Count];
        for (var i = 0; i < xfs.Count; i++)
        {
            if (!int.TryParse((string?)xfs[i].Attribute("numFmtId"), out var id)) continue;

            if (BuiltInDateFormats.Contains(id)) { result[i] = true; continue; }
            if (custom.TryGetValue(id, out var code)) result[i] = LooksLikeDate(code);
        }
        return result;
    }

    private static bool LooksLikeDate(string formatCode)
    {
        var inQuotes = false;
        foreach (var ch in formatCode)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (inQuotes) continue;
            if (ch is 'y' or 'Y' or 'd' or 'D') return true;
            if (ch is 'm' or 'M') return true;
        }
        return false;
    }

    private static string CellText(XElement cell, List<string> shared, bool[] dateStyles)
    {
        var type = (string?)cell.Attribute("t");

        if (type == "inlineStr")
        {
            var inline = cell.Element(Main + "is");
            if (inline == null) return string.Empty;
            var sb = new StringBuilder();
            foreach (var t in inline.Elements(Main + "t")) sb.Append(t.Value);
            foreach (var r in inline.Elements(Main + "r"))
                foreach (var t in r.Elements(Main + "t")) sb.Append(t.Value);
            return sb.ToString();
        }

        var v = cell.Element(Main + "v");
        if (v == null) return string.Empty;
        var raw = v.Value;

        switch (type)
        {
            case "s":
                return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)
                       && idx >= 0 && idx < shared.Count
                    ? shared[idx]
                    : string.Empty;
            case "str":
                return raw;
            case "b":
                return raw == "1" ? "VRAI" : "FAUX";
            case "e":
                return raw;
        }

        var styleIndex = int.TryParse((string?)cell.Attribute("s"), out var si) ? si : 0;

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return raw;

        if (styleIndex >= 0 && styleIndex < dateStyles.Length && dateStyles[styleIndex]
            && number > 0 && number < 2958466)
        {
            var date = DateTime.FromOADate(number);
            return date.TimeOfDay == TimeSpan.Zero
                ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : date.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        }

        // Entier : surtout pas de notation scientifique, on y perdrait les numéros.
        if (Math.Abs(number % 1) < double.Epsilon && Math.Abs(number) < 1e15)
            return ((long)number).ToString(CultureInfo.InvariantCulture);

        return number.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    private static int ColumnIndex(string cellReference)
    {
        var index = 0;
        foreach (var ch in cellReference)
        {
            if (ch is >= 'A' and <= 'Z') index = index * 26 + (ch - 'A' + 1);
            else if (ch is >= 'a' and <= 'z') index = index * 26 + (ch - 'a' + 1);
            else break;
        }
        return Math.Max(0, index - 1);
    }

    // ----------------------------------------------------------------- CSV

    private static Table ReadCsv(string path)
    {
        var text = ReadAllTextGuessingEncoding(path);
        var table = new Table { SheetName = Path.GetFileName(path) };

        var firstLine = text.Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? string.Empty;
        var delimiter = new[] { ';', '\t', ',', '|' }
            .OrderByDescending(d => firstLine.Count(c => c == d))
            .First();

        foreach (var row in ParseCsv(text, delimiter))
            table.Rows.Add(row);

        return table;
    }

    private static string ReadAllTextGuessingEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        // UTF-8 strict : si le décodage échoue, on retombe sur Latin-1 (export Excel FR).
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static IEnumerable<string[]> ParseCsv(string text, char delimiter)
    {
        var field = new StringBuilder();
        var row = new List<string>();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
                continue;
            }

            if (ch == '"') { inQuotes = true; continue; }
            if (ch == delimiter) { row.Add(field.ToString()); field.Clear(); continue; }

            if (ch == '\r') continue;
            if (ch == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                yield return row.ToArray();
                row.Clear();
                continue;
            }

            field.Append(ch);
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return row.ToArray();
        }
    }

    // ------------------------------------------------------------ Finalize

    private static Table Finalize(Table raw, int headerRow)
    {
        // Les lignes entièrement vides en tête de feuille ne comptent pas.
        var rows = raw.Rows;
        var start = 0;
        while (start < rows.Count && IsEmpty(rows[start])) start++;

        var headerIndex = start + headerRow - 1;
        var result = new Table { SheetName = raw.SheetName };
        if (headerIndex >= rows.Count) return result;

        var header = rows[headerIndex];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
        {
            var name = (header[i] ?? string.Empty).Trim();
            if (name.Length == 0) name = $"Colonne{i + 1}";
            var candidate = name;
            var suffix = 2;
            while (!used.Add(candidate)) candidate = $"{name}_{suffix++}";
            result.Headers.Add(candidate);
        }

        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            if (IsEmpty(rows[i])) continue;

            var source = rows[i];
            var line = new string[result.Headers.Count];
            for (var c = 0; c < line.Length; c++)
                line[c] = c < source.Length ? (source[c] ?? string.Empty).Trim() : string.Empty;
            result.Rows.Add(line);
        }

        return result;
    }

    private static bool IsEmpty(string[] row) =>
        row.Length == 0 || row.All(string.IsNullOrWhiteSpace);
}
