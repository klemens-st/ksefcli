using System.Globalization;
using System.Xml.Linq;

using CommandLine;

using KCKSeFCli.Utils;

namespace KCKSeFCli;

[Verb("DodajPozycjeNaFakturze", HelpText = "Add a new item to an existing KSeF XML invoice.")]
public class DodajPozycjeNaFakturzeCommand : IGlobalCommand {
    [Value(0, Required = true, HelpText = "Input XML file path.")]
    public required string InputFile { get; set; }

    [Value(1, Required = false, HelpText = "Output XML file path. If not provided, the input file will be overwritten.")]
    public string? OutputFile { get; set; }

    [Option("nazwa", Required = true, HelpText = "Name of the good or service (P_7).")]
    public required string Nazwa { get; set; }

    [Option("miara", Required = true, HelpText = "Unit of measure (P_8A).")]
    public required string Miara { get; set; }

    [Option("ilosc", Required = true, HelpText = "Quantity (P_8B).")]
    public required decimal Ilosc { get; set; }

    [Option("cena-netto", Required = true, HelpText = "Unit net price (P_9A).")]
    public required decimal CenaNetto { get; set; }

    [Option("stawka-vat", Required = true, HelpText = "VAT rate (P_12), e.g., 23, 8, 5, 0.")]
    public required string StawkaVat { get; set; }

    [Option("bez-walidacji", Required = false, HelpText = "Skip XML validation after adding the item.")]
    public bool BezWalidacji { get; set; }

    public override async Task<int> ExecuteAsync(CancellationToken cancellationToken) {
        ConfigureLogging();

        if (!File.Exists(InputFile)) {
            Log.Error($"Error: Input file not found: {InputFile}");
            return 1;
        }

        string outputPath = OutputFile ?? InputFile;

        string xml = File.ReadAllText(InputFile);

        XDocument doc = XDocument.Parse(xml);
        XNamespace ns = MyXml.KsefNamespace;

        XElement? fa = doc.Root?.Element(ns + "Fa");
        if (fa == null) {
            Log.Error("Error: Could not find <Fa> element in the XML.");
            return 1;
        }

        XElement? lastWiersz = fa.Elements(ns + "FaWiersz").LastOrDefault();
        if (lastWiersz == null) {
            Log.Error("Error: Could not find any <FaWiersz> elements in the XML.");
            return 1;
        }

        // Rates without a P_13_x/P_14_x pair (0%, zw, np, oo) record their net elsewhere. The
        // old code silently treated them as 0% VAT and only touched P_15, which understated the
        // total and left the invoice internally inconsistent. Refuse instead: a loud error beats
        // a plausible-looking invoice filed with the tax authority.
        InvoiceTotals.VatBand? band = InvoiceTotals.BandForRate(StawkaVat);
        if (band is null) {
            Log.Error($"Błąd: stawka VAT '{StawkaVat}' nie jest obsługiwana przez to polecenie. "
                      + $"Obsługiwane stawki: {InvoiceTotals.SupportedRates}. "
                      + "Pozycje ze stawką 0%, zw, np lub odwrotnym obciążeniem trafiają do "
                      + "innych pól sumujących i trzeba je dodać ręcznie.");
            return 1;
        }

        int newWierszId = int.Parse(lastWiersz.Element(ns + "NrWierszaFa")?.Value ?? "0") + 1;
        decimal wartoscNetto = InvoiceTotals.LineNet(Ilosc, CenaNetto);
        decimal wartoscVat = InvoiceTotals.VatFor(wartoscNetto, band.Value.Percent);

        XElement newFaWiersz = new XElement(ns + "FaWiersz",
            new XElement(ns + "NrWierszaFa", newWierszId.ToString()),
            new XElement(ns + "P_7", Nazwa),
            new XElement(ns + "P_8A", Miara),
            new XElement(ns + "P_8B", Ilosc.ToString("F2", CultureInfo.InvariantCulture)),
            new XElement(ns + "P_9A", CenaNetto.ToString("F2", CultureInfo.InvariantCulture)),
            new XElement(ns + "P_11", InvoiceTotals.Format(wartoscNetto)),
            new XElement(ns + "P_12", StawkaVat)
        );

        // Each rate band has its own net/VAT pair, so a 5% item updates P_13_3/P_14_3 rather
        // than being dropped on the floor as it was before. FA(3) only carries the bands the
        // invoice actually uses, and inserting a new pair means placing it correctly in the
        // schema sequence — so if the band is absent, refuse rather than leave the invoice
        // unbalanced. Checked before any mutation, so a refusal changes nothing.
        string[] required = [band.Value.NetField, band.Value.VatField, "P_15"];
        List<string> missing = required.Where(f => fa.Element(ns + f) is null).ToList();
        if (missing.Count > 0) {
            Log.Error($"Błąd: faktura nie zawiera pól sumujących {string.Join(", ", missing)}, "
                      + $"wymaganych dla stawki {StawkaVat}%. Dodanie pozycji rozjechałoby sumy. "
                      + "Uzupełnij te pola w fakturze wejściowej.");
            return 1;
        }

        lastWiersz.AddAfterSelf(newFaWiersz);

        AddToTotal(fa, ns, band.Value.NetField, wartoscNetto);
        AddToTotal(fa, ns, band.Value.VatField, wartoscVat);
        // wartoscVat is already rounded, so the printed total matches the printed components
        // exactly. Adding the raw product here could leave P_15 a grosz off.
        AddToTotal(fa, ns, "P_15", wartoscNetto + wartoscVat);

        doc = MyXml.Normalize(doc);
        string newXml = MyXml.XmlToString(doc);

        // Walidacja przed zapisem, tak jak w WystawKorekte. Odwrotna kolejność zostawiała po
        // nieudanym przebiegu niepoprawny XML na dysku — a to właśnie ten plik podnosi kolejny
        // krok i wysyła dalej. Przy --bez-walidacji zapis jest jedyną rzeczą, jaka się dzieje.
        if (!BezWalidacji) {
            if (XmlValidator.Validate(newXml, out List<string>? errors)) {
                Log.Information("Post-modification validation successful.");
            } else {
                Log.Error("Post-modification validation failed:");
                foreach (string error in errors!) {
                    Log.Error(error);
                }
                return 1;
            }
        }

        File.WriteAllText(outputPath, newXml);
        Log.Information($"Successfully added item and saved to: {outputPath}");

        return 0;
    }

    /// <summary>
    /// Dodaje kwotę do pola sumującego. Obecność pola jest sprawdzana przed jakąkolwiek
    /// modyfikacją dokumentu, więc tutaj jest już pewna.
    /// </summary>
    private static void AddToTotal(XElement fa, XNamespace ns, string field, decimal amount) {
        XElement element = fa.Element(ns + field)!;
        element.Value = InvoiceTotals.Format(InvoiceTotals.Parse(element.Value) + amount);
    }
}
