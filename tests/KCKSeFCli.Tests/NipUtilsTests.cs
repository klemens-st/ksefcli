// NIP parsing and comparison.
//
// These are characterization tests: the logic already existed and is exercised by the CLI
// suite, but only indirectly. MatchNip decides whether the certificate identity matches the
// configured NIP (CheckAuthNip), and ExtractNipFromToken derives the NIP a command will act
// as, so both deserve their behaviour written down rather than inferred.
//
// 5252611332 is the NIP used throughout tests/test_kcksefcli.yaml.
using Xunit;

namespace KCKSeFCli.Tests;

public class NipUtilsTests {
    [Theory]
    [InlineData("5252611332")]
    [InlineData("5260202588")]
    public void AcceptsNipsWithACorrectChecksum(string nip) {
        NipUtils.AssertNipIsValid(nip);
    }

    [Theory]
    // Last digit wrong.
    [InlineData("5252611333")]
    // Digits transposed, so the weighted sum changes.
    [InlineData("2552611332")]
    public void RejectsABadChecksum(string nip) {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => NipUtils.AssertNipIsValid(nip));
        Assert.Contains("control sum", ex.Message);
    }

    [Theory]
    [InlineData("525261133")]
    [InlineData("52526113321")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsAnythingThatIsNotTenDigits(string? nip) {
        Assert.Throws<ArgumentException>(() => NipUtils.AssertNipIsValid(nip));
    }

    [Theory]
    [InlineData("525-261-13-32")]
    [InlineData("525 261 13 32")]
    [InlineData("PL5252611332")]
    public void IgnoresSeparatorsAndPrefixesWhenValidating(string nip) {
        // NormalizeNip keeps only digits, so the usual printed forms validate.
        NipUtils.AssertNipIsValid(nip);
    }

    [Fact]
    public void NormalizeStripsEverythingButDigits() {
        Assert.Equal("5252611332", NipUtils.NormalizeNip("PL 525-261-13-32"));
    }

    [Theory]
    [InlineData("5252611332", "525-261-13-32")]
    [InlineData("PL5252611332", "5252611332")]
    public void MatchesNipsWrittenDifferently(string a, string b) {
        Assert.True(NipUtils.MatchNip(a, b));
    }

    [Theory]
    [InlineData("5252611332", "5260202588")]
    // A blank side must never match, or an unconfigured NIP would pass the identity check.
    [InlineData("5252611332", "")]
    [InlineData("5252611332", null)]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "5252611332")]
    public void DoesNotMatchDifferentOrMissingNips(string? a, string? b) {
        Assert.False(NipUtils.MatchNip(a, b));
    }

    [Fact]
    public void ExtractsTheNipFromATokenSegment() {
        Assert.Equal("5252611332", NipUtils.ExtractNipFromToken("abc|nip-5252611332|def"));
    }

    [Fact]
    public void ExtractedNipIsChecksumValidated() {
        // A token carrying a malformed NIP must fail here rather than reaching KSeF.
        Assert.Throws<ArgumentException>(() => NipUtils.ExtractNipFromToken("abc|nip-1234567890|def"));
    }

    [Theory]
    [InlineData("no-nip-here")]
    [InlineData("abc|nip-|def")]
    [InlineData("abc|nip-5252611332")]
    [InlineData("")]
    public void RejectsTokensWithoutAUsableNipSegment(string token) {
        Assert.ThrowsAny<Exception>(() => NipUtils.ExtractNipFromToken(token));
    }

    [Fact]
    public void TakesTheLastNipSegmentWhenATokenCarriesSeveral() {
        // The pattern is greedy, so the trailing segment wins. Recorded because it is not
        // obvious from reading the regex and it decides which identity a command acts as.
        Assert.Equal(
            "5260202588",
            NipUtils.ExtractNipFromToken("x|nip-5252611332|y|nip-5260202588|z"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a certificate")]
    public void ReturnsNullRatherThanThrowingOnUnusableCertificateInput(string? content) {
        Assert.Null(NipUtils.GetCertificateSubject(content));
        Assert.Null(NipUtils.GetNipFromCertificate(content));
    }
}
