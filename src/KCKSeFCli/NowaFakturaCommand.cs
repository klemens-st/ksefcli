// XML KSeF file may not have a namespace.
using System.Globalization;
using System.Xml.Linq;

using CommandLine;

using KCKSeFCli.Utils;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace KCKSeFCli;

[Verb("NowaFaktura", HelpText = "Create a new KSeF XML invoice from a YAML specification.")]
public class NowaFakturaCommand : IGlobalCommand {
    [Value(0, Required = true, HelpText = "Input YAML specification file path.")]
    public required string InputFile { get; set; }

    [Value(1, Required = true, HelpText = "Output XML file path.")]
    public required string OutputFile { get; set; }

    [Option("bez-walidacji", Required = false, HelpText = "Skip XML validation after creation.")]
    public bool BezWalidacji { get; set; }

    public class InvoiceSpec {
        public SellerSpec Sprzedawca { get; set; } = new();
        public BuyerSpec Kupujący { get; set; } = new();
        public List<PositionSpec> Pozycje { get; set; } = new();
        public List<DodatkowyOpisSpec> DodatkowyOpis { get; set; } = new();
        public string? Stopka { get; set; }
        public string? MiejsceWystawieniaFaktury { get; set; }
        public string? DataWykonania { get; set; }
    }

    public class DodatkowyOpisSpec {
        public string Klucz { get; set; } = "";
        public string Wartosc { get; set; } = "";
    }

    public abstract class PodmiotSpec {
        public string Nip { get; set; } = "";
        public string? NrID { get; set; }
        public string Nazwa { get; set; } = "";
        public string Kraj { get; set; } = "PL";
        public string Adres { get; set; } = "";
        public string? Regon { get; set; }
        public string? PelnaNazwa { get; set; }
        public string? Bdo { get; set; }

        public async Task FillFromNipInfo(string searchDate, CancellationToken cancellationToken) {
            if (string.IsNullOrEmpty(Nip) || (!string.IsNullOrEmpty(Nazwa) && !string.IsNullOrEmpty(Adres) && !string.IsNullOrEmpty(Regon))) {
                return; // Only proceed if NIP is present and other details are missing
            }

            try {
                NipInfo? nipInfo = await PobierzInfoONipCommand.GetNipDetailsAsync(Nip, searchDate, cancellationToken).ConfigureAwait(false);
                if (nipInfo != null) {
                    if (string.IsNullOrEmpty(Nazwa)) Nazwa = nipInfo.Name ?? "";
                    if (string.IsNullOrEmpty(Adres)) Adres = nipInfo.Address ?? "";
                    if (string.IsNullOrEmpty(Regon)) Regon = nipInfo.Regon;
                    // PelnaNazwa is not directly available from the current NIP API response, so we don't fill it for now
                }
            } catch (HttpRequestException ex) {
                Log.Warning($"Warning: Could not fetch NIP info for {Nip}: {ex.Message}");
            }
        }
    }

    public class SellerSpec : PodmiotSpec {
    }

    public class BuyerSpec : PodmiotSpec {
        public int JST { get; set; } = 2;
        public int GV { get; set; } = 2;
    }

    public class PositionSpec {
        public string Nazwa { get; set; } = "";
        public string? Jednostka { get; set; } = "";
        public decimal? Ilosc { get; set; } = null;
        public string? StawkaPodatku { get; set; } = null;
        public decimal WartoscBrutto { get; set; }
    }

    private class RateTotals {
        public decimal TotalNet { get; set; }
        public decimal TotalVat { get; set; }
    }

    public override async Task<int> ExecuteAsync(CancellationToken cancellationToken) {
        ConfigureLogging();

        if (!File.Exists(InputFile)) {
            Log.Error($"Error: Input file not found: {InputFile}");
            return 1;
        }

        string yamlContent = File.ReadAllText(InputFile);
        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .Build();
        InvoiceSpec spec = deserializer.Deserialize<InvoiceSpec>(yamlContent);

        string searchDate = DateTime.Now.ToString("yyyy-MM-dd");

        await spec.Sprzedawca.FillFromNipInfo(searchDate, cancellationToken).ConfigureAwait(false);
        await spec.Kupujący.FillFromNipInfo(searchDate, cancellationToken).ConfigureAwait(false);

        string xml = GenerateXml(spec);
        File.WriteAllText(OutputFile, xml);
        Log.Information($"Successfully created invoice and saved to: {OutputFile}");

        if (!BezWalidacji) {
            if (XmlValidator.ValidateLog(xml, out _)) {
                Log.Information("Validation successful.");
            } else {
                return 1;
            }
        }

        return 0;
    }

    private List<XElement> CreatePodmiotElements(PodmiotSpec podmiot) {
        List<XElement> elements = new List<XElement>();

        string name = podmiot.Nazwa;
        string address = podmiot.Adres;
        string nip = podmiot.Nip?.Replace("-", "") ?? "";

        XElement daneIdentyfikacyjne = new XElement("DaneIdentyfikacyjne");
        if (!string.IsNullOrEmpty(nip)) {
            daneIdentyfikacyjne.Add(new XElement("NIP", nip));
        } else if (!string.IsNullOrEmpty(podmiot.NrID)) {
            daneIdentyfikacyjne.Add(new XElement("NrID", podmiot.NrID));
        } else {
            daneIdentyfikacyjne.Add(new XElement("BrakID", "1"));
        }
        daneIdentyfikacyjne.Add(new XElement("Nazwa", string.IsNullOrEmpty(name) ? "BRAK" : name));

        elements.Add(daneIdentyfikacyjne);

        elements.Add(new XElement("Adres",
            new XElement("KodKraju", podmiot.Kraj),
            new XElement("AdresL1", string.IsNullOrEmpty(address) ? "BRAK" : address)
        ));
        elements.Add(new XElement("AdresKoresp",
            new XElement("KodKraju", "PL"),
            new XElement("AdresL1", "BRAK")
        ));

        if (podmiot is BuyerSpec buyer) {
            elements.Add(new XElement("NrKlienta", "BRAK"));
            elements.Add(new XElement("IDNabywcy", "BRAK"));
            elements.Add(new XElement("JST", "1"));
            elements.Add(new XElement("GV", "1"));
        }

        return elements;
    }

    private string GenerateXml(InvoiceSpec spec) {
        XNamespace ns = MyXml.KsefNamespace;
        XNamespace xsi = MyXml.XsiNamespace;

        DateTime now = DateTime.UtcNow;

        Dictionary<string, RateTotals> totalsByRate = new Dictionary<string, RateTotals>();
        decimal totalGross = 0;
        bool hasOO = false;

        List<XElement> faWiersze = new List<XElement>();
        for (int i = 0; i < spec.Pozycje.Count; i++) {
            PositionSpec p = spec.Pozycje[i];
            string rate = (p.StawkaPodatku ?? "23").Replace("%", "");
            if (rate.ToLower() == "odwrotne obciążenie") {
                rate = "oo";
            }

            if (rate == "oo") {
                hasOO = true;
            }

            decimal vatRate = 0;
            if (decimal.TryParse(rate, out decimal parsedRate)) {
                vatRate = parsedRate / 100m;
            }

            decimal net = Math.Round(p.WartoscBrutto / (1 + vatRate), 2);
            decimal vat = p.WartoscBrutto - net;

            totalGross += p.WartoscBrutto;

            if (!totalsByRate.ContainsKey(rate)) {
                totalsByRate[rate] = new RateTotals();
            }
            totalsByRate[rate].TotalNet += net;
            totalsByRate[rate].TotalVat += vat;

            XElement faWiersz = new XElement("FaWiersz",
                new XElement("NrWierszaFa", (i + 1).ToString()),
                new XElement("P_7", p.Nazwa));

            if (!string.IsNullOrEmpty(p.Jednostka)) {
                faWiersz.Add(new XElement("P_8A", p.Jednostka));
            }

            if (p.Ilosc.HasValue) {
                faWiersz.Add(new XElement("P_8B", p.Ilosc.Value.ToString("F2", CultureInfo.InvariantCulture)));
            }

            faWiersz.Add(
                new XElement("P_9A", net.ToString("F2", CultureInfo.InvariantCulture)),
                new XElement("P_11", net.ToString("F2", CultureInfo.InvariantCulture)),
                new XElement("P_12", rate)
            );

            faWiersze.Add(faWiersz);
        }

        List<XElement> faElements = new List<XElement>
        {
            new XElement("KodWaluty", "PLN"),
            new XElement("P_1", now.ToString("yyyy-MM-dd")),
        };

        if (!string.IsNullOrEmpty(spec.MiejsceWystawieniaFaktury)) {
            faElements.Add(new XElement("P_1M", spec.MiejsceWystawieniaFaktury));
        }

        string dataWykonania = spec.DataWykonania ?? now.ToString("yyyy-MM-dd");

        faElements.AddRange(new XElement[]
        {
            new XElement("P_2", "FV/" + now.ToString("yyyyMMdd") + "/01"),
            new XElement("P_6", dataWykonania)
        });

        // Every rate band that has a P_13_x/P_14_x pair, not just 23/8/5. A 4% (ryczałt) or
        // historical 22%/7% position used to be counted in P_15 with no summary fields at all,
        // so the invoice did not add up — and XSD validation does not check that it does.
        //
        // Emitted in field order because the FA(3) schema is a sequence. Net and VAT keep the
        // gross-remainder arithmetic above (vat = brutto - net), which makes net + vat equal
        // the gross exactly; only the field mapping comes from InvoiceTotals.
        List<string> unsupportedRates = new List<string>();
        Dictionary<string, (string VatField, decimal Net, decimal Vat)> byField = new();

        foreach (KeyValuePair<string, RateTotals> entry in totalsByRate) {
            InvoiceTotals.VatBand? band = InvoiceTotals.BandForRate(entry.Key);
            if (band is null) {
                // "oo" and "zw" are expected here: they carry no VAT and are declared through
                // Adnotacje and other fields instead.
                if (entry.Key != "oo" && entry.Value.TotalNet > 0) {
                    unsupportedRates.Add(entry.Key);
                }
                continue;
            }
            (string VatField, decimal Net, decimal Vat) running =
                byField.TryGetValue(band.Value.NetField, out (string VatField, decimal Net, decimal Vat) existing)
                    ? existing
                    : (band.Value.VatField, 0m, 0m);
            byField[band.Value.NetField] =
                (running.VatField, running.Net + entry.Value.TotalNet, running.Vat + entry.Value.TotalVat);
        }

        foreach (KeyValuePair<string, (string VatField, decimal Net, decimal Vat)> pair
                 in byField.OrderBy(p => p.Key, StringComparer.Ordinal)) {
            // Tylko pasmo puste po obu stronach można pominąć — nie wnosi nic do żadnej sumy.
            // Warunek <= 0 gubił pasmo, które rabat sprowadził poniżej zera, podczas gdy
            // totalGross liczyło je dalej: faktura zostawała bez P_13_x/P_14_x, a P_15 i tak
            // się o nie zmieniało, więc składniki nie sumowały się do podanej kwoty.
            if (pair.Value.Net == 0m && pair.Value.Vat == 0m) {
                continue;
            }
            faElements.Add(new XElement(pair.Key, InvoiceTotals.Format(pair.Value.Net)));
            faElements.Add(new XElement(pair.Value.VatField, InvoiceTotals.Format(pair.Value.Vat)));
        }

        if (unsupportedRates.Count > 0) {
            Log.Warning($"Stawki {string.Join(", ", unsupportedRates)} nie mają pary pól "
                        + "P_13_x/P_14_x. Ich wartość wchodzi do P_15, ale odpowiednie pola "
                        + "sumujące trzeba uzupełnić ręcznie.");
        }

        faElements.Add(new XElement("P_15", totalGross.ToString("F2", CultureInfo.InvariantCulture)));

        // Adnotacje section - Strictly matching user's example
        faElements.Add(new XElement("Adnotacje",
            new XElement("P_16", "2"),
            new XElement("P_17", "2"),
            new XElement("P_18", hasOO ? "1" : "2"),
            new XElement("P_18A", "2"),
            new XElement("Zwolnienie",
                new XElement("P_19N", "1")
            ),
            new XElement("NoweSrodkiTransportu",
                new XElement("P_22N", "1")
            ),
            new XElement("P_23", "2"),
            new XElement("PMarzy",
                new XElement("P_PMarzyN", "1")
            )
        ));

        faElements.Add(new XElement("RodzajFaktury", "VAT"));

        foreach (DodatkowyOpisSpec opis in spec.DodatkowyOpis) {
            faElements.Add(new XElement("DodatkowyOpis",
                new XElement("Klucz", opis.Klucz),
                new XElement("Wartosc", opis.Wartosc)
            ));
        }

        foreach (XElement wiersz in faWiersze) {
            faElements.Add(wiersz);
        }

        XDocument doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("Faktura",
                new XElement("Naglowek",
                    new XElement("KodFormularza",
                        new XAttribute("kodSystemowy", "FA (3)"),
                        new XAttribute("wersjaSchemy", "1-0E"),
                        "FA"),
                    new XElement("WariantFormularza", "3"),
                    new XElement("DataWytworzeniaFa", now.ToString("yyyy-MM-ddTHH:mm:ssZ")), // Use local time string for schema
                    new XElement("SystemInfo", "KCKSeFCli")
                ),
                new XElement("Podmiot1", CreatePodmiotElements(spec.Sprzedawca)),
                new XElement("Podmiot2", CreatePodmiotElements(spec.Kupujący)),
                new XElement("Fa", faElements)
            )
        );

        doc = MyXml.Normalize(doc);
        return MyXml.XmlToString(doc);
    }
}
