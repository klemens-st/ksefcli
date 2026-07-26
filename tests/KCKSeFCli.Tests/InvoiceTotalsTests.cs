// Money math for DodajPozycjeNaFakturze.
//
// Two defects motivated pulling this out of the command:
//
// 1. Only "22" and "23" were recognised. An item at 8% skipped the P_13_x/P_14_x update
//    entirely and was added to P_15 with zero VAT, so the invoice total came out short by the
//    whole VAT amount, silently. FA(3) has a separate net/VAT field pair per rate band.
//
// 2. VAT was added to P_15 unrounded while P_14_x was rounded only on the way out via
//    ToString("F2"). The printed total could therefore disagree with the printed components by
//    a grosz, which is the kind of thing KSeF rejects an invoice over.
//
// Rates with no net+VAT pair (0%, zw, np, odwrotne obciążenie) go to different fields
// entirely. Guessing at those would produce a wrong invoice, so they are reported as
// unsupported and the command refuses rather than writing something plausible-looking.
using KCKSeFCli.Utils;

using Xunit;

namespace KCKSeFCli.Tests;

public class InvoiceTotalsTests {
    [Theory]
    // Stawka podstawowa.
    [InlineData("23", "P_13_1", "P_14_1")]
    [InlineData("22", "P_13_1", "P_14_1")]
    // Stawka obniżona pierwsza.
    [InlineData("8", "P_13_2", "P_14_2")]
    [InlineData("7", "P_13_2", "P_14_2")]
    // Stawka obniżona druga.
    [InlineData("5", "P_13_3", "P_14_3")]
    // Ryczałt dla rolnika.
    [InlineData("4", "P_13_4", "P_14_4")]
    public void MapsEachRateToItsOwnFieldPair(string rate, string netField, string vatField) {
        InvoiceTotals.VatBand? band = InvoiceTotals.BandForRate(rate);

        Assert.NotNull(band);
        Assert.Equal(netField, band!.Value.NetField);
        Assert.Equal(vatField, band.Value.VatField);
    }

    [Theory]
    [InlineData("23", 23)]
    [InlineData("8", 8)]
    [InlineData("5", 5)]
    public void CarriesTheNumericRate(string rate, int expectedPercent) {
        Assert.Equal(expectedPercent, InvoiceTotals.BandForRate(rate)!.Value.Percent);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("zw")]
    [InlineData("np")]
    [InlineData("oo")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nonsense")]
    [InlineData("12")]
    public void ReportsRatesWithNoNetVatPairAsUnsupported(string? rate) {
        // Silently treating these as 0% VAT understated the invoice total before.
        Assert.Null(InvoiceTotals.BandForRate(rate));
    }

    [Theory]
    [InlineData(" 23 ", "P_13_1")]
    [InlineData("23%", "P_13_1")]
    [InlineData("8%", "P_13_2")]
    public void ToleratesSurroundingSpaceAndAPercentSign(string rate, string netField) {
        Assert.Equal(netField, InvoiceTotals.BandForRate(rate)!.Value.NetField);
    }

    [Theory]
    // 1000.00 at 23% is exact.
    [InlineData(1000.00, 23, 230.00)]
    // 99.99 at 23% is 22.9977, which must land on a whole grosz.
    [InlineData(99.99, 23, 23.00)]
    // 0.95 at 5% is 0.0475, matching P_14_3 in tests/FA_3_Przykład_1.xml.
    [InlineData(0.95, 5, 0.05)]
    // Exact .005 midpoints round away from zero, the Polish tax convention, not to even.
    [InlineData(0.10, 5, 0.01)]
    [InlineData(1.50, 5, 0.08)]
    public void RoundsVatToWholeGrosze(decimal net, int percent, decimal expected) {
        Assert.Equal(expected, InvoiceTotals.VatFor(net, percent));
    }

    [Fact]
    public void TotalEqualsItsOwnComponents() {
        // The invariant the unrounded addition broke: whatever is printed as net plus whatever
        // is printed as VAT must be exactly what is printed as the total.
        decimal net = 99.99m;
        decimal vat = InvoiceTotals.VatFor(net, 23);

        Assert.Equal(InvoiceTotals.RoundMoney(net + vat), net + vat);
    }

    [Theory]
    [InlineData(0.005, 0.01)]
    [InlineData(-0.005, -0.01)]
    [InlineData(0.014, 0.01)]
    [InlineData(0.015, 0.02)]
    [InlineData(2.675, 2.68)]
    public void RoundMoneyGoesAwayFromZeroOnMidpoints(decimal value, decimal expected) {
        Assert.Equal(expected, InvoiceTotals.RoundMoney(value));
    }

    [Fact]
    public void LineNetIsRoundedBeforeItReachesTheTotals() {
        // 3 x 33.333 is 99.999, which must not carry a third decimal into the invoice.
        Assert.Equal(100.00m, InvoiceTotals.LineNet(quantity: 3m, unitPrice: 33.333m));
    }
}
