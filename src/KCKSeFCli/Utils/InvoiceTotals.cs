#nullable enable
using System.Globalization;

namespace KCKSeFCli.Utils;

/// <summary>
/// Arytmetyka kwot dla FA(3): przypisanie stawki VAT do pary pól P_13_x / P_14_x oraz
/// zaokrąglanie do pełnych groszy.
///
/// Pure logic on purpose — the alternative is only reachable through a command that reads and
/// writes files.
/// </summary>
public static class InvoiceTotals {
    /// <summary>Para pól sumujących dla jednej stawki VAT.</summary>
    public readonly record struct VatBand(int Percent, string NetField, string VatField);

    /// <summary>
    /// FA(3) keeps a separate net/VAT pair per rate band. Only the bands that actually have
    /// both fields are listed: 0%, zw, np and odwrotne obciążenie report their net in different
    /// elements and carry no VAT, so they are deliberately absent rather than guessed at.
    /// </summary>
    private static readonly Dictionary<int, VatBand> Bands = new() {
        // Stawka podstawowa, obecnie 23% (22% w starszych fakturach).
        [23] = new VatBand(23, "P_13_1", "P_14_1"),
        [22] = new VatBand(22, "P_13_1", "P_14_1"),
        // Stawka obniżona pierwsza, obecnie 8% (7% historycznie).
        [8] = new VatBand(8, "P_13_2", "P_14_2"),
        [7] = new VatBand(7, "P_13_2", "P_14_2"),
        // Stawka obniżona druga.
        [5] = new VatBand(5, "P_13_3", "P_14_3"),
        // Stawka obniżona trzecia — ryczałt dla taksówek osobowych (schemat_FA(3)_v1-0E.xsd:2558).
        // Nie mylić ze zryczałtowanym zwrotem podatku dla rolnika ryczałtowego: ten należy do
        // faktury VAT RR i nie trafia do żadnego z pól P_13_x.
        [4] = new VatBand(4, "P_13_4", "P_14_4"),
    };

    /// <summary>
    /// Zwraca pola sumujące dla podanej stawki albo <c>null</c>, jeśli stawka nie ma pary
    /// netto/VAT. Wywołujący ma wtedy odmówić, a nie zakładać zerowy VAT.
    /// </summary>
    public static VatBand? BandForRate(string? rate) {
        string normalized = (rate ?? "").Trim().TrimEnd('%').Trim();
        if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent)) {
            return null;
        }
        return Bands.TryGetValue(percent, out VatBand band) ? band : null;
    }

    /// <summary>Pole netto stawki 0%. Pary VAT nie ma — podatek wynosi zero.</summary>
    public readonly record struct ZeroRateBand(string Rate, string NetField);

    /// <summary>
    /// Stawka 0% ma własne pole netto, tylko bez pary P_14_x. Które to pole, rozstrzyga rodzaj
    /// transakcji — i niesie go sama wartość stawki, bo <c>TStawkaPodatku</c> nie dopuszcza
    /// gołego "0", a jedynie te trzy warianty (schemat_FA(3)_v1-0E.xsd:1876-1890). Mapowanie
    /// jest więc jednoznaczne, nie zgadywane.
    /// </summary>
    private static readonly ZeroRateBand[] ZeroRateBands = [
        // Sprzedaż krajowa, z wyłączeniem WDT i eksportu.
        new ZeroRateBand("0 KR", "P_13_6_1"),
        // Wewnątrzwspólnotowa dostawa towarów.
        new ZeroRateBand("0 WDT", "P_13_6_2"),
        // Eksport towarów.
        new ZeroRateBand("0 EX", "P_13_6_3"),
    ];

    /// <summary>
    /// Rozpoznaje stawkę 0% w jednym z wariantów <c>TStawkaPodatku</c>, tolerując wielkość
    /// liter i nadmiarowe odstępy. Zwraca kanoniczną postać stawki razem z jej polem netto,
    /// albo <c>null</c>. Gołe "0" celowo nie przechodzi: schemat go nie zna, więc trafiłoby do
    /// P_12 jako wartość spoza listy.
    /// </summary>
    public static ZeroRateBand? ZeroRateBandFor(string? rate) {
        string normalized = string.Join(
            " ", (rate ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        foreach (ZeroRateBand band in ZeroRateBands) {
            if (string.Equals(band.Rate, normalized, StringComparison.OrdinalIgnoreCase)) {
                return band;
            }
        }
        return null;
    }

    /// <summary>Nazwy stawek, które obsługujemy — do komunikatu o błędzie.</summary>
    public static string SupportedRates =>
        string.Join(", ", Bands.Keys.OrderByDescending(k => k).Select(k => k + "%"))
        + ", " + string.Join(", ", ZeroRateBands.Select(b => b.Rate));

    /// <summary>
    /// Zaokrąglenie do pełnych groszy. Połówki w górę co do modułu, zgodnie z praktyką
    /// podatkową — <c>Math.Round</c> domyślnie zaokrągla do parzystych, co dawałoby inne kwoty.
    /// </summary>
    public static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// VAT od kwoty netto, już zaokrąglony. Zaokrąglenie musi nastąpić tutaj, a nie dopiero
    /// przy formatowaniu: suma P_15 dodaje tę wartość, więc niezaokrąglona rozjeżdżałaby się
    /// z tym, co widnieje w P_14_x.
    /// </summary>
    public static decimal VatFor(decimal net, int percent) =>
        RoundMoney(net * percent / 100m);

    /// <summary>Wartość netto pozycji, zaokrąglona zanim trafi do sum.</summary>
    public static decimal LineNet(decimal quantity, decimal unitPrice) =>
        RoundMoney(quantity * unitPrice);

    /// <summary>Formatowanie kwoty do XML — zawsze dwie cyfry po kropce, niezależnie od locale.</summary>
    public static string Format(decimal value) =>
        value.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>Parsowanie kwoty z XML — zawsze kropka dziesiętna, niezależnie od locale.</summary>
    public static decimal Parse(string value) =>
        decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

    /// <summary>Suma netto i VAT dla jednego pasma stawek.</summary>
    public readonly record struct BandTotal(VatBand Band, decimal Net, decimal Vat);

    /// <summary>
    /// Podsumowanie pozycji faktury. <see cref="UnsupportedRates"/> wymienia stawki, których
    /// nie da się przypisać do pary pól — wywołujący ma o nich wiedzieć, bo ich netto wchodzi
    /// do sumy ogólnej, ale nie do żadnego P_13_x.
    /// </summary>
    public readonly record struct Summary(
        IReadOnlyList<BandTotal> Bands,
        IReadOnlyList<string> UnsupportedRates,
        decimal TotalNet,
        decimal TotalVat);

    /// <summary>
    /// Grupuje pozycje po paśmie stawek i liczy VAT raz od sumy netto pasma, a nie osobno dla
    /// każdej pozycji. Zaokrąglanie każdej pozycji z osobna kumuluje błąd: trzy pozycje po
    /// 0,33 zł przy 23% dają 0,24 zł zamiast poprawnych 0,23 zł.
    ///
    /// Wynik jest kluczowany parą pól, a nie stawką. Kilka stawek dzieli jedną parę (23% i 22%
    /// trafiają do P_13_1/P_14_1, 8% i 7% do P_13_2/P_14_2), więc grupowanie po stawce zwracało
    /// dwie sumy wskazujące na ten sam element: wywołujący wpisywał obie pod ten sam adres i
    /// wygrywała druga, mimo że P_15 liczyło obie pozycje. Prawo nie dopuszcza obu stawek pary
    /// na jednej fakturze, ale WystawKorekte czyta cudzy XML i musi policzyć poprawnie to, co
    /// dostanie, zamiast po cichu rozjeżdżać sumy.
    /// </summary>
    public static Summary Summarize(IEnumerable<(string? Rate, decimal Net)> lines) {
        Dictionary<int, (VatBand Band, decimal Net)> byRate = new();
        List<string> unsupported = new();
        decimal unsupportedNet = 0m;

        foreach ((string? rate, decimal net) in lines) {
            VatBand? band = BandForRate(rate);
            if (band is null) {
                string label = rate ?? "";
                if (!unsupported.Contains(label)) {
                    unsupported.Add(label);
                }
                unsupportedNet += net;
                continue;
            }
            int key = band.Value.Percent;
            decimal running = byRate.TryGetValue(key, out (VatBand Band, decimal Net) existing)
                ? existing.Net
                : 0m;
            byRate[key] = (band.Value, running + net);
        }

        // Dwa etapy, i kolejność ma znaczenie dla kwot. Najpierw VAT od sumy netto danej
        // *stawki* — to jest właśnie liczenie od sumy pasma, którego broni komentarz wyżej.
        // Dopiero potem sumowanie stawek dzielących parę pól, osobno netto i osobno VAT.
        //
        // VAT-u nie wolno przeliczyć z połączonego netto po jednej stawce: 100,00 zł przy 23%
        // i 100,00 zł przy 22% to 45,00 zł podatku, a VatFor(200,00, 23) dałoby 46,00 zł —
        // poprawną kwotę zamieniłoby to na błędną.
        Dictionary<string, BandTotal> byField = new();

        foreach ((VatBand Band, decimal Net) entry in byRate.Values.OrderByDescending(e => e.Band.Percent)) {
            decimal net = RoundMoney(entry.Net);
            decimal vat = VatFor(net, entry.Band.Percent);

            if (byField.TryGetValue(entry.Band.NetField, out BandTotal running)) {
                // Pasmo reprezentuje stawka wyższa, bo pętla idzie malejąco i pierwsza trafia
                // do słownika: dla pary 23/22 zostaje 23. Percent służy już tylko komunikatom.
                byField[entry.Band.NetField] =
                    running with { Net = running.Net + net, Vat = running.Vat + vat };
            } else {
                byField[entry.Band.NetField] = new BandTotal(entry.Band, net, vat);
            }
        }

        List<BandTotal> bands = byField.Values
            .OrderByDescending(band => band.Band.Percent)
            .ToList();

        return new Summary(
            bands,
            unsupported,
            bands.Sum(b => b.Net) + RoundMoney(unsupportedNet),
            bands.Sum(b => b.Vat));
    }
}
