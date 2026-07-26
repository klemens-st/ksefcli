using System.Globalization;
using System.Xml.Linq;

using CommandLine;

using KCKSeFCli;
using KCKSeFCli.Utils;

namespace KCKSeFCli;

[Verb("WystawKorekte", HelpText = "Issue a correction invoice based on an input XML.")]
public class WystawKorekteCommand : IGlobalCommand {
    [Value(0, Required = true, HelpText = "Input XML file path.")]
    public required string InputFile { get; set; }

    [Value(1, Required = true, HelpText = "Output XML file path.")]
    public required string OutputFile { get; set; }

    [Value(2, Required = true, HelpText = "Pairs of arguments: <numer_lub_nazwa_pozycji> <nowa_ilosc_lub_roznica>")]
    public required IEnumerable<string> Korekty { get; set; }

    [Option("PrzyczynaKorekty", Default = "", HelpText = "Reason for correction (PrzyczynaKorekty).")]
    public required string PrzyczynaKorekty { get; set; }

    [Option("TypKorekty", HelpText = "Type of correction (TypKorekty).")]
    public string? TypKorekty { get; set; }

    [Option("no-validate", HelpText = "Skip XML validation after creating the correction.")]
    public bool NoValidate { get; set; }

    public override async Task<int> ExecuteAsync(CancellationToken cancellationToken) {
        ConfigureLogging();

        if (!File.Exists(InputFile)) {
            Log.Error($"Error: Input file not found: {InputFile}");
            return 1;
        }

        string xml = File.ReadAllText(InputFile);
        XDocument doc = XDocument.Parse(xml);
        XNamespace ns = MyXml.KsefNamespace;

        XElement? fa = doc.Root?.Element(ns + "Fa");
        if (fa == null) {
            Log.Error("Error: Could not find <Fa> element in the XML.");
            return 1;
        }

        string? p1 = fa.Element(ns + "P_1")?.Value;
        string? p2 = fa.Element(ns + "P_2")?.Value;
        if (string.IsNullOrEmpty(p1) || string.IsNullOrEmpty(p2)) {
            Log.Error("Error: Could not find P_1 (issue date) or P_2 (invoice number) in the XML.");
            return 1;
        }

        fa.Element(ns + "RodzajFaktury")?.SetValue("KOR");
        fa.Element(ns + "P_2")?.SetValue($"FK/{p2}");

        XElement daneFaKorygowanej = new XElement(ns + "DaneFaKorygowanej",
            new XElement(ns + "DataWystFaKorygowanej", p1),
            new XElement(ns + "NrFaKorygowanej", p2)
        );

        XElement? p15Element = fa.Element(ns + "P_15");
        p15Element?.AddAfterSelf(daneFaKorygowanej);

        daneFaKorygowanej.AddAfterSelf(new XElement(ns + "PrzyczynaKorekty", PrzyczynaKorekty));

        if (!string.IsNullOrEmpty(TypKorekty)) {
            fa.Element(ns + "PrzyczynaKorekty")?.AddAfterSelf(new XElement(ns + "TypKorekty", TypKorekty));
        }

        List<string> korektyList = new List<string>(Korekty);
        if (korektyList.Count % 2 != 0) {
            Log.Error("Error: Corrections must be provided in pairs: <numer_lub_nazwa> <ilosc_lub_roznica>");
            return 1;
        }

        Dictionary<string, string> corrections = new Dictionary<string, string>();
        for (int i = 0; i < korektyList.Count; i += 2) {
            corrections[korektyList[i]] = korektyList[i + 1];
        }

        List<XElement> originalWiersze = fa.Elements(ns + "FaWiersz").ToList();
        List<XElement> newWiersze = new List<XElement>();

        foreach (XElement? wiersz in originalWiersze) {
            string? nrWiersza = wiersz.Element(ns + "NrWierszaFa")?.Value;
            string? nazwa = wiersz.Element(ns + "P_7")?.Value;

            if ((nrWiersza != null && corrections.TryGetValue(nrWiersza, out string? zmiana)) ||
                (nazwa != null && corrections.TryGetValue(nazwa, out zmiana))) {
                XElement wierszPrzed = new XElement(wiersz);
                NegateWierszValues(wierszPrzed, ns);
                newWiersze.Add(wierszPrzed);

                XElement wierszPo = new XElement(wiersz);
                ApplyCorrection(wierszPo, ns, zmiana);
                newWiersze.Add(wierszPo);
            } else {
                newWiersze.Add(new XElement(wiersz));
            }
        }

        originalWiersze.Remove();
        fa.Add(newWiersze);

        int wierszId = 1;
        foreach (XElement wiersz in fa.Elements(ns + "FaWiersz")) {
            wiersz.Element(ns + "NrWierszaFa")?.SetValue(wierszId++);
        }

        RecalculateTotals(fa, ns);

        doc = MyXml.Normalize(doc);
        string newXml = MyXml.XmlToString(doc);

        if (!NoValidate) {
            if (XmlValidator.Validate(newXml, out List<string>? errors)) {
                Log.Information("Post-modification validation successful.");
            } else {
                Log.Error("Post-modification validation failed:");
                foreach (string error in errors) {
                    Log.Error(error);
                }
                return 1;
            }
        }

        File.WriteAllText(OutputFile, newXml);
        Log.Information($"Successfully created correction and saved to: {OutputFile}");

        return 0;
    }

    private void NegateWierszValues(XElement wiersz, XNamespace ns) {
        NegateElementValue(wiersz, ns, "P_8B");
        NegateElementValue(wiersz, ns, "P_11");
    }

    private void ApplyCorrection(XElement wiersz, XNamespace ns, string zmiana) {
        XElement? p8bElement = wiersz.Element(ns + "P_8B");
        XElement? p9aElement = wiersz.Element(ns + "P_9A");
        XElement? p11Element = wiersz.Element(ns + "P_11");

        if (p8bElement == null || p9aElement == null || p11Element == null) {
            return;
        }

        decimal originalQty = decimal.Parse(p8bElement.Value, CultureInfo.InvariantCulture);
        decimal unitPrice = decimal.Parse(p9aElement.Value, CultureInfo.InvariantCulture);
        decimal newQty;

        if (zmiana.StartsWith("+") || zmiana.StartsWith("-")) {
            decimal diff = decimal.Parse(zmiana, CultureInfo.InvariantCulture);
            newQty = originalQty + diff;
        } else {
            newQty = decimal.Parse(zmiana, CultureInfo.InvariantCulture);
        }

        p8bElement.Value = newQty.ToString("F2", CultureInfo.InvariantCulture);
        p11Element.Value = (newQty * unitPrice).ToString("F2", CultureInfo.InvariantCulture);
    }

    private void NegateElementValue(XElement wiersz, XNamespace ns, string elementName) {
        XElement? element = wiersz.Element(ns + elementName);
        if (element != null && decimal.TryParse(element.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value)) {
            element.Value = (-value).ToString("F2", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Przelicza sumy P_13_x / P_14_x / P_15 po zastosowaniu korekt.
    ///
    /// This used to handle rate 23 only, by its own admission. On an invoice with any other
    /// band, P_15 was recalculated from every line while that band's P_13_x/P_14_x kept its
    /// pre-correction value, so the correction silently did not add up.
    /// </summary>
    private void RecalculateTotals(XElement fa, XNamespace ns) {
        List<(string? Rate, decimal Net)> lines = fa.Elements(ns + "FaWiersz")
            .Select(wiersz => (
                Rate: wiersz.Element(ns + "P_12")?.Value,
                Net: InvoiceTotals.Parse(wiersz.Element(ns + "P_11")?.Value ?? "0")))
            .Where(line => line.Rate is not null)
            .ToList();

        InvoiceTotals.Summary summary = InvoiceTotals.Summarize(lines);

        foreach (InvoiceTotals.BandTotal band in summary.Bands) {
            XElement? netField = fa.Element(ns + band.Band.NetField);
            XElement? vatField = fa.Element(ns + band.Band.VatField);
            if (netField is null || vatField is null) {
                // Inserting a missing pair means placing it correctly in the schema sequence,
                // so say plainly that this band was not written rather than dropping it.
                Log.Warning($"Faktura nie zawiera pól {band.Band.NetField}/{band.Band.VatField} "
                            + $"dla stawki {band.Band.Percent}%; sumy tego pasma nie zostały "
                            + "zaktualizowane. Sprawdź fakturę przed wysyłką.");
                continue;
            }
            netField.Value = InvoiceTotals.Format(band.Net);
            vatField.Value = InvoiceTotals.Format(band.Vat);
        }

        if (summary.UnsupportedRates.Count > 0) {
            Log.Warning($"Pozycje ze stawkami {string.Join(", ", summary.UnsupportedRates)} "
                        + "nie mają pary pól P_13_x/P_14_x. Ich wartość netto wchodzi do P_15, "
                        + "ale odpowiednie pola sumujące trzeba uzupełnić ręcznie.");
        }

        XElement? p15 = fa.Element(ns + "P_15");
        if (p15 is not null) {
            p15.Value = InvoiceTotals.Format(summary.TotalNet + summary.TotalVat);
        }
    }
}
