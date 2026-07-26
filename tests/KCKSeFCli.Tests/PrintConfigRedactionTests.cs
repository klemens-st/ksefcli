using Xunit;

namespace KCKSeFCli.Tests;

/// <summary>
/// Regression tests for defect #3: PrintConfig serialized the whole resolved profile, so the
/// PEM private key, the certificate password (already fetched from its env var, file or
/// password-manager command) and the KSeF token were all printed verbatim.
///
/// ConfigLoader resolves those fields before PrintConfig sees them, so pointing at a file or a
/// `pass show` command did not keep the secret out of the output — it just moved where the
/// secret came from. In an agent loop the output lands in model context and transcripts.
///
/// Redaction preserves which fields are set, since that is the diagnostic value of the command;
/// only the values go.
/// </summary>
public class PrintConfigRedactionTests {
    private const string Key = "-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBg...\n-----END PRIVATE KEY-----";
    private const string Cert = "-----BEGIN CERTIFICATE-----\nMIIDdzCCAl+gAwIB...\n-----END CERTIFICATE-----";
    private const string Password = "cmd_password_output";
    private const string Token = "mytesttoken|nip-5252611332|123";

    private static ProfileConfig FullyResolvedProfile() => new() {
        Environment = "test",
        Nip = "5252611332",
        Token = Token,
        Certificate = new CertificateConfig {
            Private_Key = Key,
            Certificate = Cert,
            Password = Password,
            Private_Key_File = "certs/klucz.pem",
            Certificate_File = "certs/cert.pem",
            Password_Env = "TEST_PASSWORD_ENV",
            Password_File = "certs/haslo.txt",
            Password_Cmd = new List<string> { "pass", "show", "ksef/cert" },
        },
    };

    [Fact]
    public void NoSecretSurvivesRedaction() {
        ProfileConfig redacted = PrintConfigCommand.Redact(FullyResolvedProfile());

        Assert.NotEqual(Key, redacted.Certificate!.Private_Key);
        Assert.NotEqual(Cert, redacted.Certificate.Certificate);
        Assert.NotEqual(Password, redacted.Certificate.Password);
        Assert.NotEqual(Token, redacted.Token);
    }

    [Fact]
    public void SecretsAreReplacedWithAnUnmistakableMarker() {
        ProfileConfig redacted = PrintConfigCommand.Redact(FullyResolvedProfile());

        Assert.Equal(PrintConfigCommand.RedactionMarker, redacted.Token);
        Assert.Equal(PrintConfigCommand.RedactionMarker, redacted.Certificate!.Private_Key);
        Assert.Equal(PrintConfigCommand.RedactionMarker, redacted.Certificate.Certificate);
        Assert.Equal(PrintConfigCommand.RedactionMarker, redacted.Certificate.Password);
    }

    [Fact]
    public void NoSecretAppearsAnywhereInSerializedOutput() {
        // The assertion that actually matters: whatever the serializer does with the shape,
        // none of these strings may reach stdout.
        string yaml = PrintConfigCommand.Render(
            new ProfileConfigWithName(FullyResolvedProfile(), "cert_test"), json: false, reveal: false);
        string json = PrintConfigCommand.Render(
            new ProfileConfigWithName(FullyResolvedProfile(), "cert_test"), json: true, reveal: false);

        foreach (string output in new[] { yaml, json }) {
            Assert.DoesNotContain("MIIEvQIBADANBg", output);
            Assert.DoesNotContain("MIIDdzCCAl+gAwIB", output);
            Assert.DoesNotContain(Password, output);
            Assert.DoesNotContain(Token, output);
        }
    }

    [Fact]
    public void NonSecretsAreKeptSoTheCommandStillDiagnoses() {
        // Paths, env var names and commands say where a secret comes from without disclosing
        // it — and "which key am I actually using" is why anyone runs PrintConfig.
        ProfileConfig redacted = PrintConfigCommand.Redact(FullyResolvedProfile());

        Assert.Equal("test", redacted.Environment);
        Assert.Equal("5252611332", redacted.Nip);
        Assert.Equal("certs/klucz.pem", redacted.Certificate!.Private_Key_File);
        Assert.Equal("certs/cert.pem", redacted.Certificate.Certificate_File);
        Assert.Equal("TEST_PASSWORD_ENV", redacted.Certificate.Password_Env);
        Assert.Equal("certs/haslo.txt", redacted.Certificate.Password_File);
        Assert.Equal(new[] { "pass", "show", "ksef/cert" }, redacted.Certificate.Password_Cmd);
    }

    [Fact]
    public void AbsentFieldsStayAbsentRatherThanLookingConfigured() {
        // Redacting null into "<redacted>" would claim a key is configured when none is.
        ProfileConfig redacted = PrintConfigCommand.Redact(new ProfileConfig {
            Environment = "test",
            Nip = "5252611332",
            Certificate = new CertificateConfig { Private_Key = Key },
        });

        Assert.Null(redacted.Token);
        Assert.Null(redacted.Certificate!.Password);
        Assert.Null(redacted.Certificate.Certificate);
        Assert.Equal(PrintConfigCommand.RedactionMarker, redacted.Certificate.Private_Key);
    }

    [Fact]
    public void AuthMethodIsUnchangedByRedaction() {
        // AuthMethod is derived from Certificate being non-null; redaction must not flip it.
        Assert.Equal(AuthMethod.Xades, PrintConfigCommand.Redact(FullyResolvedProfile()).AuthMethod);
        Assert.Equal(AuthMethod.KsefToken,
            PrintConfigCommand.Redact(new ProfileConfig { Environment = "test", Token = Token }).AuthMethod);
    }

    [Fact]
    public void TokenOnlyProfileIsRedactedToo() {
        ProfileConfig redacted = PrintConfigCommand.Redact(
            new ProfileConfig { Environment = "demo", Nip = "5252611332", Token = Token });

        Assert.Null(redacted.Certificate);
        Assert.Equal(PrintConfigCommand.RedactionMarker, redacted.Token);
    }

    [Fact]
    public void RevealPrintsTheRealValues() {
        // --reveal has to actually work, otherwise the escape hatch is a lie and there is no
        // way to debug a wrong password.
        string json = PrintConfigCommand.Render(
            new ProfileConfigWithName(FullyResolvedProfile(), "cert_test"), json: true, reveal: true);

        Assert.Contains(Password, json);
        Assert.Contains(Token, json);
        Assert.Contains("MIIEvQIBADANBg", json);
    }

    [Fact]
    public void RedactIsNonDestructive() {
        // Redact must not mutate the profile the rest of the command still uses.
        ProfileConfig original = FullyResolvedProfile();
        PrintConfigCommand.Redact(original);

        Assert.Equal(Key, original.Certificate!.Private_Key);
        Assert.Equal(Token, original.Token);
    }
}
