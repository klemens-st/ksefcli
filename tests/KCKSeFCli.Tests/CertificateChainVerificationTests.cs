using Xunit;

namespace KCKSeFCli.Tests;

/// <summary>
/// Regression tests for defect #7: Authenticate.CertAuth called
/// SubmitXadesAuthRequestAsync(signedXml, verifyCertificateChain: false) with the flag
/// hardcoded, on every environment including production.
///
/// The flag is a query parameter on /v2/auth/xades-signature, so it tells KSeF not to check
/// that the certificate signing the authentication request chains to a trusted CA. That is
/// what you want against the test environment, where self-signed certificates are the norm.
/// Against production it discards a check the server is offering to perform.
///
/// It is now resolved per profile, defaulting to on everywhere except test, and overridable
/// with verify_certificate_chain in the profile.
/// </summary>
public class CertificateChainVerificationTests {
    private static ProfileConfig Profile(string environment, bool? overrideValue = null) => new() {
        Environment = environment,
        Nip = "5252611332",
        Verify_Certificate_Chain = overrideValue,
    };

    [Theory]
    [InlineData("prod")]
    [InlineData("PROD")]
    [InlineData("Prod")]
    [InlineData("demo")]
    [InlineData("DEMO")]
    public void RealEnvironmentsVerifyTheChain(string environment) {
        Assert.True(Profile(environment).VerifyCertificateChain);
    }

    [Theory]
    [InlineData("test")]
    [InlineData("TEST")]
    [InlineData("Test")]
    public void TestEnvironmentDoesNot(string environment) {
        // Self-signed certificates are the norm there; requiring a chain would break the
        // environment the project's own test config targets.
        Assert.False(Profile(environment).VerifyCertificateChain);
    }

    [Fact]
    public void UnknownEnvironmentFailsSafe() {
        // A typo or a future environment must not silently land on the permissive setting.
        Assert.True(Profile("").VerifyCertificateChain);
        Assert.True(Profile("testing").VerifyCertificateChain);
        Assert.True(Profile("prod-eu").VerifyCertificateChain);
    }

    [Fact]
    public void ExplicitSettingWins() {
        Assert.False(Profile("prod", overrideValue: false).VerifyCertificateChain);
        Assert.True(Profile("test", overrideValue: true).VerifyCertificateChain);
    }

    [Fact]
    public void DefaultProfileVerifies() {
        // A ProfileConfig with nothing set at all must not default to the weaker behaviour.
        Assert.True(new ProfileConfig().VerifyCertificateChain);
    }

    [Fact]
    public void SettingSurvivesTheNamedProfileCopy() {
        // ProfileConfigWithName copies field by field, so a new field is easy to drop silently.
        ProfileConfigWithName named = new(Profile("test", overrideValue: true), "cert_test");

        Assert.True(named.VerifyCertificateChain);
        Assert.True(new ProfileConfigWithName(named).VerifyCertificateChain);
    }

    [Fact]
    public void SettingSurvivesPrintConfigRedaction() {
        // Redact rebuilds the profile, so the same drop-a-field risk applies there.
        Assert.False(PrintConfigCommand.Redact(Profile("prod", overrideValue: false)).VerifyCertificateChain);
        Assert.True(PrintConfigCommand.Redact(Profile("prod")).VerifyCertificateChain);
    }
}
