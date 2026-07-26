using System.Security.Cryptography;

using KCKSeFCli.Utils;

using Xunit;

namespace KCKSeFCli.Tests;

/// <summary>
/// Regression tests for defect #6: XML2PDF downloaded a ~74 MB executable over HTTPS, cached it
/// by Last-Modified timestamp, chmod +x'd it and ran it — with no signature and no checksum.
/// It runs on the invoice path whenever --pdf is passed, so PobierzFaktury and PrzeslijFaktury
/// reach it too.
///
/// Timestamp caching made it worse than a plain download: once a file sat in the cache with a
/// recent mtime it was executed forever without being re-fetched or re-checked.
///
/// Artifacts are now pinned by SHA-256, the cache is content-addressed, and a mismatched
/// download is deleted rather than left on disk.
/// </summary>
public class ArtifactVerificationTests : IDisposable {
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "kcksefcli-artifact-tests-" + Guid.NewGuid().ToString("N"));

    public ArtifactVerificationTests() => Directory.CreateDirectory(_dir);

    public void Dispose() {
        if (Directory.Exists(_dir)) {
            Directory.Delete(_dir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private string WriteFile(string name, byte[] content) {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static string HashOf(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    [Fact]
    public void HashMatchesAKnownVector() {
        // Pins the encoding: lowercase hex of the raw digest, so a pinned constant elsewhere in
        // the codebase means what it looks like it means.
        string path = WriteFile("abc.bin", "abc"u8.ToArray());

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                     ArtifactVerification.Sha256Hex(path));
    }

    [Fact]
    public void MatchingArtifactPasses() {
        byte[] content = new byte[] { 1, 2, 3, 4, 5 };
        string path = WriteFile("good.bin", content);

        ArtifactVerification.Verify(path, HashOf(content));
    }

    [Fact]
    public void MismatchedArtifactThrows() {
        string path = WriteFile("bad.bin", new byte[] { 1, 2, 3, 4, 5 });

        Assert.Throws<ArtifactVerification.IntegrityException>(
            () => ArtifactVerification.Verify(path, HashOf(new byte[] { 9, 9, 9 })));
    }

    [Fact]
    public void ASingleFlippedByteIsCaught() {
        byte[] original = new byte[1024];
        new Random(1).NextBytes(original);
        string expected = HashOf(original);

        byte[] tampered = (byte[])original.Clone();
        tampered[512] ^= 0x01;
        string path = WriteFile("tampered.bin", tampered);

        Assert.Throws<ArtifactVerification.IntegrityException>(
            () => ArtifactVerification.Verify(path, expected));
    }

    [Fact]
    public void TruncatedArtifactIsCaught() {
        // The realistic failure: an interrupted download leaves a short file behind.
        byte[] original = new byte[4096];
        new Random(2).NextBytes(original);
        string expected = HashOf(original);
        string path = WriteFile("short.bin", original.Take(2048).ToArray());

        Assert.Throws<ArtifactVerification.IntegrityException>(
            () => ArtifactVerification.Verify(path, expected));
    }

    [Fact]
    public void EmptyArtifactIsCaught() {
        byte[] original = new byte[] { 7, 7, 7 };
        string path = WriteFile("empty.bin", Array.Empty<byte>());

        Assert.Throws<ArtifactVerification.IntegrityException>(
            () => ArtifactVerification.Verify(path, HashOf(original)));
    }

    [Fact]
    public void MissingArtifactIsCaught() {
        Assert.Throws<ArtifactVerification.IntegrityException>(
            () => ArtifactVerification.Verify(Path.Combine(_dir, "nope.bin"), HashOf(new byte[] { 1 })));
    }

    [Fact]
    public void ComparisonIsCaseInsensitiveButNotLoose() {
        byte[] content = new byte[] { 1, 2, 3 };
        string path = WriteFile("case.bin", content);
        string expected = HashOf(content);

        ArtifactVerification.Verify(path, expected.ToUpperInvariant());
        Assert.Throws<ArtifactVerification.IntegrityException>(
            () => ArtifactVerification.Verify(path, expected.Substring(0, 32)));
    }

    [Fact]
    public void AnEmptyExpectedHashIsRejectedRatherThanTreatedAsNoCheck() {
        // Otherwise a missing pin would silently disable verification.
        byte[] content = new byte[] { 1, 2, 3 };
        string path = WriteFile("nopin.bin", content);

        Assert.Throws<ArtifactVerification.IntegrityException>(() => ArtifactVerification.Verify(path, ""));
        Assert.Throws<ArtifactVerification.IntegrityException>(() => ArtifactVerification.Verify(path, null!));
    }

    [Fact]
    public void MatchesReportsWithoutThrowing() {
        byte[] content = new byte[] { 4, 5, 6 };
        string path = WriteFile("m.bin", content);

        Assert.True(ArtifactVerification.Matches(path, HashOf(content)));
        Assert.False(ArtifactVerification.Matches(path, HashOf(new byte[] { 0 })));
        Assert.False(ArtifactVerification.Matches(Path.Combine(_dir, "absent.bin"), HashOf(content)));
    }

    [Fact]
    public void PinnedGeneratorHashesAreWellFormed() {
        // Cheap guard against a pin being edited into something that cannot match anything,
        // which would turn every download into a hard failure.
        foreach (string pin in new[] {
            XML2PDFCommand.LinuxGeneratorSha256,
            XML2PDFCommand.WindowsGeneratorSha256,
        }) {
            Assert.Equal(64, pin.Length);
            Assert.Equal(pin, pin.ToLowerInvariant());
            Assert.True(pin.All(Uri.IsHexDigit), $"'{pin}' is not hex.");
        }
    }

    [Fact]
    public void NpxFallbackIsPinnedToACommitNotATag() {
        // A tag can be moved; a commit id cannot. The previous value also pointed at "v1.1.0",
        // a ref that does not exist in that repository at all.
        string spec = XML2PDFCommand.NpxPackageSpec;

        string committish = spec.Substring(spec.IndexOf('#') + 1);
        Assert.Equal(40, committish.Length);
        Assert.True(committish.All(Uri.IsHexDigit), $"'{committish}' is not a commit id.");
    }
}
