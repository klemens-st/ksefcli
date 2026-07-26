using KCKSeFCli.Utils;

using Xunit;

namespace KCKSeFCli.Tests;

/// <summary>
/// Regression tests for defect #4: PobierzFaktury built output paths as
/// Path.Combine(OutputDir, $"{fileName}.json") where fileName came straight from the KSeF
/// response — the invoice number under --useInvoiceNumber, otherwise the KSeF number.
///
/// Neither is ours. The invoice number is chosen by whoever issued the invoice, and Polish
/// invoice numbers routinely contain a slash: the README's own example is 0004/26. So this was
/// both a traversal hole and a plain functionality bug, since ordinary invoice numbers crashed
/// the download.
///
/// Path.Combine makes the traversal case sharp: given a rooted second argument it discards the
/// first entirely, so an invoice numbered "/etc/cron.d/x" writes to /etc/cron.d, not to the
/// output directory.
///
/// PrzeslijFaktury has the same shape when it names UPO files after the KSeF number.
/// </summary>
public class SafeFileNameTests {
    private static readonly string OutputDir =
        Path.Combine(Path.GetTempPath(), "kcksefcli-out");

    private static void AssertStaysInsideOutputDir(string rawName) {
        string combined = Path.Combine(OutputDir, SafePath.SafeFileName(rawName) + ".json");
        string resolved = Path.GetFullPath(combined);
        string root = Path.GetFullPath(OutputDir) + Path.DirectorySeparatorChar;

        Assert.StartsWith(root, resolved);
        Assert.Equal(Path.GetFullPath(Path.Combine(OutputDir, Path.GetFileName(resolved))), resolved);
    }

    [Theory]
    [InlineData("0004/26")]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/cron.d/kcksefcli")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("../")]
    [InlineData("....//....//etc/passwd")]
    [InlineData("a/../../b")]
    [InlineData("C:\\Windows\\System32\\drivers\\etc\\hosts")]
    [InlineData("..\\..\\windows\\system32")]
    [InlineData("5260202588-20260726-0100000001-A1")]
    [InlineData("")]
    [InlineData("   ")]
    public void EveryNameStaysInsideTheOutputDirectory(string rawName) {
        AssertStaysInsideOutputDir(rawName);
    }

    [Fact]
    public void NullNameIsHandled() {
        AssertStaysInsideOutputDir(null!);
        Assert.NotEmpty(SafePath.SafeFileName(null));
    }

    [Fact]
    public void ResultNeverContainsASeparator() {
        foreach (string raw in new[] {
            "0004/26", "../../etc/passwd", "a\\b", "/abs/path", "x/y/z",
        }) {
            string safe = SafePath.SafeFileName(raw);

            Assert.DoesNotContain('/', safe);
            Assert.DoesNotContain('\\', safe);
            Assert.Equal(safe, Path.GetFileName(safe));
        }
    }

    [Fact]
    public void OrdinaryInvoiceNumberBecomesSomethingUsable() {
        // The functionality half: 0004/26 must produce a file, not a DirectoryNotFoundException.
        Assert.Equal("0004_26", SafePath.SafeFileName("0004/26"));
    }

    [Fact]
    public void KsefNumberIsLeftAlone() {
        // The default naming must not get uglier as a side effect of the fix.
        const string ksefNumber = "5260202588-20260726-0100000001-A1";

        Assert.Equal(ksefNumber, SafePath.SafeFileName(ksefNumber));
    }

    [Fact]
    public void PolishCharactersAreKept() {
        Assert.Equal("FA-Zażółć-2026", SafePath.SafeFileName("FA-Zażółć-2026"));
    }

    [Fact]
    public void DirectoryReferencesNeverSurvive() {
        // ".." with an extension appended is still a traversal component on some paths, and
        // "." names a directory. Neither may come out as-is.
        foreach (string raw in new[] { ".", "..", "...", "./", "../" }) {
            string safe = SafePath.SafeFileName(raw);

            Assert.NotEqual(".", safe);
            Assert.NotEqual("..", safe);
            Assert.False(safe.Trim('.').Length == 0, $"'{raw}' produced '{safe}', which is all dots.");
        }
    }

    [Fact]
    public void NulByteAndControlCharactersAreRemoved() {
        string safe = SafePath.SafeFileName("fa\0ktura\n\r\t2026");

        Assert.DoesNotContain('\0', safe);
        Assert.DoesNotContain('\n', safe);
        Assert.DoesNotContain('\r', safe);
        Assert.DoesNotContain('\t', safe);
    }

    [Fact]
    public void TrailingDotsAndSpacesAreStripped() {
        // Windows silently drops them, so "FA-1." and "FA-1" would name the same file while
        // looking distinct.
        Assert.Equal("FA-1", SafePath.SafeFileName("FA-1. "));
        Assert.Equal("FA-1", SafePath.SafeFileName("FA-1   "));
    }

    [Fact]
    public void WindowsReservedNamesAreEscaped() {
        // CON.xml is not a file on Windows; it is the console device.
        foreach (string reserved in new[] { "CON", "con", "PRN", "AUX", "NUL", "COM1", "LPT9" }) {
            string safe = SafePath.SafeFileName(reserved);

            Assert.NotEqual(reserved, safe);
            Assert.Contains(reserved, safe);
        }
    }

    [Fact]
    public void ReservedNameWithASuffixIsStillEscaped() {
        // Windows matches the stem before the first dot, so "NUL.2026" is reserved too.
        Assert.NotEqual("NUL.2026", SafePath.SafeFileName("NUL.2026"));
    }

    [Fact]
    public void OverlongNamesAreTruncatedToSomethingWritable() {
        string safe = SafePath.SafeFileName(new string('a', 500));

        Assert.NotEmpty(safe);
        // Leaves room for the ".json"/".xml"/".pdf" the callers append, under the usual 255
        // byte limit for a single path component.
        Assert.True(safe.Length <= 200, $"Length was {safe.Length}.");
    }

    [Fact]
    public void ResultIsNeverEmpty() {
        foreach (string raw in new[] { "", "   ", "///", "...", "\0", "\\" }) {
            Assert.NotEmpty(SafePath.SafeFileName(raw));
        }
    }
}
