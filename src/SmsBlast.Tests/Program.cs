using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SmsBlast.App.Core;

namespace SmsBlast.Tests;

/// <summary>
/// Harnais de test sans dépendance externe : `dotnet run --project src/SmsBlast.Tests`.
/// Il couvre la logique métier partagée avec l'application WPF (normalisation des
/// numéros, gabarit, comptage SMS, lecture xlsx/csv, construction des commandes adb).
/// </summary>
internal static class Program
{
    private static int _passed;
    private static readonly List<string> Failures = new();

    private static int Main(string[] args)
    {
        var sampleDir = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples");

        TestBase64Url();
        TestPhoneNormalizer();
        TestPretty();
        TestTemplate();
        TestSmsInfo();
        TestAdbCommand();
        TestBroadcastParsing();
        TestCsv();
        TestXlsx(sampleDir);
        TestEndToEndPipeline(sampleDir);

        Console.WriteLine();
        Console.WriteLine($"{_passed} test(s) réussi(s), {Failures.Count} échec(s).");
        foreach (var f in Failures) Console.WriteLine("  ÉCHEC : " + f);

        return Failures.Count == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------- Helpers

    private static void Check(bool condition, string label)
    {
        if (condition) { _passed++; return; }
        Failures.Add(label);
    }

    private static void Equal(string? actual, string? expected, string label)
    {
        if (string.Equals(actual, expected, StringComparison.Ordinal)) { _passed++; return; }
        Failures.Add($"{label} — attendu \"{expected}\", obtenu \"{actual}\"");
    }

    private static void Equal(int actual, int expected, string label)
    {
        if (actual == expected) { _passed++; return; }
        Failures.Add($"{label} — attendu {expected}, obtenu {actual}");
    }

    private static void Section(string name) => Console.WriteLine("== " + name);

    // --------------------------------------------------------------- Tests

    private static void TestBase64Url()
    {
        Section("Base64Url");

        string[] samples =
        {
            "Bonjour",
            "Rendez-vous le 15/09 à 9h — n'oubliez pas votre pièce d'identité !",
            "Ligne 1\nLigne 2\tavec tabulation",
            "Caractères shell : $ ` \" ' \\ ; & | > < * ?",
            "Emoji 😀 et accents éàüç"
        };

        foreach (var s in samples)
        {
            var encoded = Base64Url.Encode(s);
            Check(Regex.IsMatch(encoded, "^[A-Za-z0-9_-]*$"),
                $"l'encodage ne doit contenir aucun caractère à échapper ({s[..Math.Min(12, s.Length)]}…)");
            Equal(Base64Url.Decode(encoded), s, "aller-retour base64url");
        }

        Equal(Base64Url.Encode(""), "", "chaîne vide");
    }

    private static void TestPhoneNormalizer()
    {
        Section("PhoneNormalizer");

        var fr = PhoneNormalizer.France;

        (string input, string expected, PhoneKind kind)[] cases =
        {
            ("06 12 34 56 78",     "+33612345678", PhoneKind.Mobile),
            ("0612345678",         "+33612345678", PhoneKind.Mobile),
            ("06.12.34.56.78",     "+33612345678", PhoneKind.Mobile),
            ("06-12-34-56-78",     "+33612345678", PhoneKind.Mobile),
            ("+33 6 12 34 56 78",  "+33612345678", PhoneKind.Mobile),
            ("0033612345678",      "+33612345678", PhoneKind.Mobile),
            ("00 33 6 12 34 56 78","+33612345678", PhoneKind.Mobile),
            ("612345678",          "+33612345678", PhoneKind.Mobile),
            ("'0612345678",        "+33612345678", PhoneKind.Mobile),
            ("07 65 43 21 09",     "+33765432109", PhoneKind.Mobile),
            ("01 42 00 00 00",     "+33142000000", PhoneKind.Landline),
        };

        foreach (var (input, expected, kind) in cases)
        {
            var r = PhoneNormalizer.Normalize(input, fr);
            Equal(r.E164, expected, $"normalisation « {input} »");
            Check(r.Kind == kind, $"type de « {input} » : attendu {kind}, obtenu {r.Kind}");
        }

        string[] invalid = { "", "   ", "12", "abc", "06 12 34 56", "6,12E+08", "0612345678901234" };
        foreach (var input in invalid)
            Check(!PhoneNormalizer.Normalize(input, fr).IsValid,
                $"« {input} » doit être rejeté");

        // Un numéro belge saisi au format national avec la Belgique en pays par défaut.
        var be = PhoneNormalizer.Normalize("0470 12 34 56", PhoneNormalizer.Belgium);
        Equal(be.E164, "+32470123456", "normalisation belge");
        Check(be.Kind == PhoneKind.Mobile, "un 04xx belge est un mobile");

        // Le même numéro, en international, reste reconnu quel que soit le pays choisi.
        var beFromFr = PhoneNormalizer.Normalize("+32470123456", fr);
        Equal(beFromFr.E164, "+32470123456", "numéro belge lu depuis un contexte français");
        Check(beFromFr.Kind == PhoneKind.Mobile, "type conservé hors pays par défaut");
        Check(beFromFr.Detail.Contains("BE", StringComparison.Ordinal), "l'origine étrangère est signalée");

        // Sans pays par défaut, un numéro national ne peut pas être deviné.
        Check(!PhoneNormalizer.Normalize("0612345678", PhoneNormalizer.International).IsValid,
            "aucun indicatif implicite en mode international");

        // Détection de colonne.
        var headers = new List<string> { "Nom", "Portable", "Ville" };
        var rows = new List<string[]>
        {
            new[] { "Dupont", "0612345678", "Lyon" },
            new[] { "Martin", "0623456789", "Nancy" }
        };
        Equal(PhoneNormalizer.GuessPhoneColumn(headers, rows), 1, "détection de la colonne « Portable »");

        var blind = new List<string> { "A", "B" };
        var blindRows = new List<string[]> { new[] { "x", "0612345678" } };
        Equal(PhoneNormalizer.GuessPhoneColumn(blind, blindRows), 1,
            "détection par le contenu quand l'en-tête ne dit rien");
    }

    private static void TestPretty()
    {
        Section("Mise en forme");
        Equal(PhoneNormalizer.Pretty("+33612345678"), "+33 6 12 34 56 78", "affichage FR");
        Equal(PhoneNormalizer.Pretty("+32470123456"), "+32 4 70 12 34 56", "affichage BE");
    }

    private static void TestTemplate()
    {
        Section("MessageTemplate");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Prénom"] = "Gaëtan",
            ["Société"] = "Dupont",
            ["Vide"] = "   "
        };

        Equal(MessageTemplate.Render("Bonjour {{Prénom}} de {{Société}}.", values, out _),
            "Bonjour Gaëtan de Dupont.", "remplacement simple");

        Equal(MessageTemplate.Render("Bonjour {{ prénom }}.", values, out _),
            "Bonjour Gaëtan.", "insensible à la casse et aux espaces");

        Equal(MessageTemplate.Render("Bonjour {{Vide|cher client}}.", values, out _),
            "Bonjour cher client.", "valeur de repli sur cellule vide");

        Equal(MessageTemplate.Render("Bonjour {{Inconnu|}}, ça va ?", values, out _),
            "Bonjour , ça va ?", "repli vide explicite");

        MessageTemplate.Render("Bonjour {{Inconnu}}.", values, out var missing);
        Check(missing.Count == 1 && missing[0] == "Inconnu", "variable manquante signalée");

        MessageTemplate.Render("{{Vide}}", values, out var missingEmpty);
        Check(missingEmpty.Count == 1, "une cellule vide compte comme non résolue");

        var vars = MessageTemplate.Variables("{{A}} {{ B }} {{A}} {{C|défaut}}");
        Check(vars.Count == 3, $"3 variables distinctes attendues, obtenu {vars.Count}");

        var dict = MessageTemplate.RowValues(new List<string> { "Nom", "Ville" }, new[] { "Dupont" });
        Equal(dict["Ville"], "", "colonne absente de la ligne -> valeur vide");
    }

    private static void TestSmsInfo()
    {
        Section("SmsInfo");

        Equal(SmsInfo.Analyze(new string('a', 160)).Segments, 1, "160 caractères GSM = 1 SMS");
        Equal(SmsInfo.Analyze(new string('a', 161)).Segments, 2, "161 caractères GSM = 2 SMS");
        Equal(SmsInfo.Analyze(new string('a', 306)).Segments, 2, "306 caractères GSM = 2 SMS");
        Equal(SmsInfo.Analyze(new string('a', 307)).Segments, 3, "307 caractères GSM = 3 SMS");

        Check(SmsInfo.Analyze("Rendez-vous à 9h").Gsm7, "les accents é/à sont dans l'alphabet GSM");
        Equal(SmsInfo.Analyze("€").Units, 2, "l'euro occupe 2 unités GSM");

        var emoji = SmsInfo.Analyze("Bravo 😀");
        Check(!emoji.Gsm7, "un emoji bascule en UCS-2");
        Equal(emoji.Segments, 1, "message court en UCS-2 = 1 SMS");

        Equal(SmsInfo.Analyze(new string('é', 71)).Segments, 1, "71 caractères GSM tiennent en 1 SMS");
        Equal(SmsInfo.Analyze(new string('ç', 71)).Segments, 2, "71 caractères UCS-2 = 2 SMS");
        Equal(SmsInfo.Analyze("").Segments, 0, "message vide");
    }

    private static void TestAdbCommand()
    {
        Section("Commande adb");

        var args = SmsRelayClient.BuildSendArguments(
            "R58M12345", "+33612345678", "Bonjour ; rm -rf / \"test\" $(whoami)", "12");

        Equal(args[0], "-s", "sélection de l'appareil");
        Equal(args[1], "R58M12345", "numéro de série");
        Equal(args[2], "shell", "commande shell");
        Check(args.Contains("broadcast"), "broadcast présent");
        Check(args.Contains(SmsRelayClient.Component), "composant explicite ciblé");
        Check(args.Contains("0x00000020"), "FLAG_INCLUDE_STOPPED_PACKAGES présent");

        foreach (var a in args)
            Check(!a.Contains(' ') || a == "shell",
                $"aucun argument ne doit contenir d'espace non maîtrisé : « {a} »");

        var numIndex = Array.IndexOf(args, "num_b64");
        var msgIndex = Array.IndexOf(args, "msg_b64");
        Equal(Base64Url.Decode(args[numIndex + 1]), "+33612345678", "numéro transporté intact");
        Equal(Base64Url.Decode(args[msgIndex + 1]), "Bonjour ; rm -rf / \"test\" $(whoami)",
            "message transporté intact malgré les métacaractères shell");

        var noSerial = SmsRelayClient.BuildSendArguments(null, "+33612345678", "Salut", "1");
        Equal(noSerial[0], "shell", "sans appareil explicite, pas d'option -s");
    }

    private static void TestBroadcastParsing()
    {
        Section("Analyse de la réponse adb");

        var ok = SmsRelayClient.ParseBroadcast(
            "Broadcasting: Intent { act=com.smsblast.relay.action.SEND flg=0x20 }\n" +
            "Broadcast completed: result=0, data=\"QUEUED parts=1\"");
        Check(ok.Accepted, "result=0 est une acceptation");
        Equal(ok.Data, "QUEUED parts=1", "data extrait");

        var denied = SmsRelayClient.ParseBroadcast(
            "Broadcast completed: result=3, data=\"ERROR permission_SEND_SMS_absente\"");
        Check(!denied.Accepted, "result=3 est un refus");
        Check(denied.Data.Contains("permission", StringComparison.Ordinal), "motif du refus conservé");

        var bare = SmsRelayClient.ParseBroadcast("Broadcast completed: result=0");
        Check(bare.Accepted && bare.Data.Length == 0, "réponse sans data");

        var broken = SmsRelayClient.ParseBroadcast("error: device not found");
        Check(!broken.Parsed && !broken.Accepted, "sortie inattendue non interprétée comme un succès");
    }

    private static void TestCsv()
    {
        Section("Lecture CSV");

        var path = Path.Combine(Path.GetTempPath(), "smsblast-test.csv");
        var csv = "Prénom;Portable;Société\n" +
                  "Gaëtan;06 12 34 56 78;Dupont\n" +
                  "\"Bernard, Amélie\";0623456789;\"Viva\"\"ticket\"\n" +
                  "\n" +
                  "Karim;+33765432109;Acme\n";
        File.WriteAllText(path, csv, new UTF8Encoding(true));

        var table = TabularReader.Read(path, null, 1);
        Equal(table.Headers.Count, 3, "3 colonnes");
        Equal(table.Headers[0], "Prénom", "en-tête accentué");
        Equal(table.Rows.Count, 3, "3 lignes de données (la ligne vide est ignorée)");
        Equal(table.Rows[1][0], "Bernard, Amélie", "champ entre guillemets contenant le séparateur");
        Equal(table.Rows[1][2], "Viva\"ticket", "guillemet échappé");
        Equal(table.Rows[2][1], "+33765432109", "dernière ligne");

        File.Delete(path);
    }

    private static void TestXlsx(string sampleDir)
    {
        Section("Lecture XLSX");

        var path = Path.Combine(sampleDir, "contacts-exemple.xlsx");
        if (!File.Exists(path))
        {
            Failures.Add($"fixture introuvable : {path}");
            return;
        }

        var sheets = TabularReader.GetSheetNames(path);
        Check(sheets.Count == 2 && sheets[0] == "Contacts", "noms des feuilles lus");

        var table = TabularReader.Read(path, "Contacts", 1);
        Equal(table.Headers.Count, 6, "6 colonnes");
        Equal(table.Headers[0], "Prénom", "en-tête 1");
        Equal(table.Headers[2], "Portable", "en-tête 3");
        Equal(table.Rows.Count, 8, "8 lignes de données (ligne vide exclue)");

        Equal(table.Rows[0][0], "Gaëtan", "chaîne partagée accentuée");
        Equal(table.Rows[0][2], "06 12 34 56 78", "numéro stocké en texte");
        Equal(table.Rows[2][2], "612345670", "numéro stocké en nombre, sans notation scientifique");
        Equal(table.Rows[0][4], "15/09/2026", "date reconnue via le style");
        Equal(table.Rows[0][5], "120", "entier sans décimale parasite");

        var notes = TabularReader.Read(path, "Notes", 1);
        Equal(notes.Headers[0], "Feuille secondaire", "seconde feuille lisible");
    }

    private static void TestEndToEndPipeline(string sampleDir)
    {
        Section("Chaîne complète");

        var path = Path.Combine(sampleDir, "contacts-exemple.xlsx");
        if (!File.Exists(path)) return;

        var table = TabularReader.Read(path, "Contacts", 1);
        var column = PhoneNormalizer.GuessPhoneColumn(table.Headers, table.Rows);
        Equal(column, 2, "colonne « Portable » détectée dans le classeur");

        var template = "Bonjour {{Prénom}}, RDV le {{Rendez-vous}} chez {{Société}}. " +
                       "Solde : {{Montant}} EUR.";

        var results = new List<(string phone, PhoneKind kind, string text)>();
        foreach (var row in table.Rows)
        {
            var phone = PhoneNormalizer.Normalize(row[column], PhoneNormalizer.France);
            var text = MessageTemplate.Render(template, MessageTemplate.RowValues(table.Headers, row), out _);
            results.Add((phone.E164, phone.Kind, text));
        }

        Equal(results.Count, 8, "8 destinataires analysés");
        Equal(results[0].phone, "+33612345678", "premier numéro normalisé");
        Equal(results[0].text,
            "Bonjour Gaëtan, RDV le 15/09/2026 chez Dupont. Solde : 120 EUR.",
            "message personnalisé rendu");

        Equal(results[2].phone, "+33612345670", "numéro stocké en nombre recomposé avec l'indicatif");
        Check(results[4].kind == PhoneKind.Landline, "la ligne fixe est identifiée");
        Check(!string.IsNullOrEmpty(results[5].phone) && results[5].phone == results[0].phone,
            "le doublon écrit en 0033 correspond au numéro de la première ligne");
        Check(results[6].kind == PhoneKind.Invalid, "le numéro cassé est rejeté");
        Equal(results[7].phone, "+32470123456", "le numéro belge international est conservé");
        Check(results[7].kind == PhoneKind.Mobile, "le mobile belge reste un mobile");

        var mobiles = results.Count(r => r.kind == PhoneKind.Mobile);
        Equal(mobiles, 6, "6 mobiles dans le jeu d'essai");

        // Le message le plus long doit rester sur un seul SMS dans cet exemple.
        var maxSegments = results.Max(r => SmsInfo.Analyze(r.text).Segments);
        Equal(maxSegments, 1, "les messages d'exemple tiennent en un SMS");
    }
}
