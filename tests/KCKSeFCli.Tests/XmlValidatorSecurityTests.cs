using System.Reflection;
using System.Xml;
using System.Xml.Schema;

using KCKSeFCli;

using Xunit;

namespace KCKSeFCli.Tests;

/// <summary>
/// Regression tests for defect #8: XmlValidator used to attach an XmlUrlResolver, so the
/// FA(3) schema's absolute-URL import made every validation fetch
/// http://crd.gov.pl/.../StrukturyDanych_v10-0E.xsd over plaintext HTTP at runtime.
///
/// That meant validation could not run offline, and the schema deciding whether an invoice
/// is valid was tamperable by anyone on the network path. The whole import chain is now
/// vendored and registered explicitly, and the resolver is disabled.
///
/// These tests are meaningful only because they run with no network access to crd.gov.pl.
/// </summary>
public class XmlValidatorSecurityTests {
    private static string SampleInvoicePath {
        get {
            // tests/KCKSeFCli.Tests/bin/<cfg>/<tfm>/ -> repo root
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir.Length > 1; i++) {
                string candidate = Path.Combine(dir, "tests", "FA_3_Przykład_1.xml");
                if (File.Exists(candidate)) {
                    return candidate;
                }
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar))!;
            }
            throw new FileNotFoundException("Could not locate tests/FA_3_Przykład_1.xml");
        }
    }

    [Fact]
    public void EveryImportedSchemaIsEmbedded() {
        // If any link in the chain is missing from the assembly, validation silently falls
        // back to skipping the import and stops enforcing those types.
        string[] expected = {
            "KCKSeFCli.Resources.schemat_FA(3)_v1-0E.xsd",
            "KCKSeFCli.Resources.StrukturyDanych_v10-0E.xsd",
            "KCKSeFCli.Resources.ElementarneTypyDanych_v10-0E.xsd",
            "KCKSeFCli.Resources.KodyKrajow_v10-0E.xsd",
        };

        string[] actual = typeof(XmlValidator).Assembly.GetManifestResourceNames();

        foreach (string name in expected) {
            Assert.Contains(name, actual);
        }
    }

    [Fact]
    public void ValidInvoiceValidatesWithoutNetworkAccess() {
        string xml = File.ReadAllText(SampleInvoicePath);

        bool valid = XmlValidator.Validate(xml, out List<string> errors);

        Assert.True(valid, "Expected the sample FA(3) invoice to validate offline. Errors: "
                           + string.Join(" | ", errors));
    }

    [Fact]
    public void ValidationDoesNotDependOnResolvingRemoteSchemaLocations() {
        // The failure mode before the fix: the import could not be resolved, so the etd types
        // went undeclared and validation blew up on TNaturalny / TKodKraju / TDataCzas rather
        // than reporting a genuine problem with the invoice.
        string xml = File.ReadAllText(SampleInvoicePath);

        XmlValidator.Validate(xml, out List<string> errors);

        Assert.DoesNotContain(errors, e => e.Contains("is not declared", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(errors, e => e.Contains("crd.gov.pl", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportedTypesAreActuallyEnforced() {
        // Guards against "fixing" this by dropping the import entirely, which would make
        // validation pass everything. KodKraju is typed etd:TKodKraju, a closed enumeration
        // of country codes, so a bogus code must be rejected.
        string xml = File.ReadAllText(SampleInvoicePath);
        string tampered = xml.Replace("<KodKraju>PL</KodKraju>", "<KodKraju>ZZ</KodKraju>");
        Assert.NotEqual(xml, tampered); // the sample really does contain that element

        bool valid = XmlValidator.Validate(tampered, out List<string> errors);

        Assert.False(valid, "An invalid country code must be rejected; the vendored etd schema "
                            + "chain is not being enforced.");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void DoctypeDeclarationIsRejected() {
        // XXE guard: DtdProcessing must be Prohibit, so a DOCTYPE is refused outright.
        string xxe =
            "<?xml version=\"1.0\"?>"
            + "<!DOCTYPE Faktura [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>"
            + "<Faktura xmlns=\"http://crd.gov.pl/wzor/2025/06/25/13775/\"><x>&xxe;</x></Faktura>";

        bool valid = XmlValidator.Validate(xxe, out List<string> errors);

        Assert.False(valid);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ExternalEntityContentIsNeverResolved() {
        // Even if DTD handling were relaxed, no file content may reach the parsed document.
        string xxe =
            "<?xml version=\"1.0\"?>"
            + "<!DOCTYPE Faktura [<!ENTITY xxe SYSTEM \"file:///etc/hostname\">]>"
            + "<Faktura xmlns=\"http://crd.gov.pl/wzor/2025/06/25/13775/\"><x>&xxe;</x></Faktura>";

        XmlValidator.Validate(xxe, out List<string> errors);

        string joined = string.Join(" | ", errors);
        string hostname = File.Exists("/etc/hostname")
            ? File.ReadAllText("/etc/hostname").Trim()
            : "";
        if (!string.IsNullOrEmpty(hostname)) {
            Assert.DoesNotContain(hostname, joined);
        }
    }

    [Fact]
    public void SchemaSetCompilesOfflineWithBothNamespacesRegistered() {
        // XmlSchemaSet.XmlResolver is set-only, so assert the outcome instead: the set must
        // compile and carry both namespaces having never touched the network. Before the fix
        // the etd namespace could only arrive via an HTTP fetch.
        MethodInfo getSchema = typeof(XmlValidator).GetMethod(
            "GetSchema", BindingFlags.NonPublic | BindingFlags.Static)!;
        XmlSchemaSet set = (XmlSchemaSet)getSchema.Invoke(null, null)!;

        List<string> namespaces = set.Schemas().Cast<XmlSchema>()
            .Select(s => s.TargetNamespace ?? "").ToList();

        Assert.True(set.IsCompiled, "Schema set should be compiled eagerly, so a broken chain "
                                    + "fails loudly at load rather than silently at validation.");
        Assert.Contains("http://crd.gov.pl/wzor/2025/06/25/13775/", namespaces);
        Assert.Contains("http://crd.gov.pl/xml/schematy/dziedzinowe/mf/2022/01/05/eD/DefinicjeTypy/",
                        namespaces);
    }
}
