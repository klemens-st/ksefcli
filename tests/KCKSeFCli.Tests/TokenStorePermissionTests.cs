using KSeF.Client.Core.Models.Authorization;

using Xunit;

namespace KCKSeFCli.Tests;

/// <summary>
/// Regression tests for defect #2: TokenStore wrote KSeF access and refresh tokens as plain
/// JSON through a default FileStream, so the cache landed at 0644 and its directory at 0755.
/// Every other account on the machine could read live credentials for filing invoices.
///
/// docs/Configuration.md described the cache as "bezpiecznie zapisywany"; it was not.
///
/// The mode is set when the file is created, not applied afterwards, so these tests are also
/// asserting that no window exists in which a token sits in a world-readable file.
/// </summary>
public class TokenStorePermissionTests : IDisposable {
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "kcksefcli-tokenstore-tests-" + Guid.NewGuid().ToString("N"));

    private string StorePath => Path.Combine(_root, "cache", "tokenstore.json");

    public void Dispose() {
        if (Directory.Exists(_root)) {
            Directory.Delete(_root, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private static TokenStore.Data SampleToken() => new(new AuthenticationOperationStatusResponse {
        AccessToken = new TokenInfo { Token = "access-secret", ValidUntil = DateTimeOffset.UtcNow.AddHours(1) },
        RefreshToken = new TokenInfo { Token = "refresh-secret", ValidUntil = DateTimeOffset.UtcNow.AddDays(1) },
    });

    private static TokenStore.Key SampleKey() =>
        new("test", new ProfileConfig { Nip = "5260202588", Environment = "test" });

    [Fact]
    public void NewStoreIsCreatedOwnerOnly() {
        if (!TokenStore.UnixPermissionsApply) {
            return; // POSIX-only assertion; on Windows the profile directory ACL is the control.
        }

        _ = new TokenStore(StorePath);

        Assert.True(File.Exists(StorePath), "Constructing the store should create the cache file.");
        Assert.Equal(TokenStore.SecretFileMode, File.GetUnixFileMode(StorePath));
    }

    [Fact]
    public void DirectoryWeCreateIsOwnerOnly() {
        if (!TokenStore.UnixPermissionsApply) {
            return; // POSIX-only assertion; on Windows the profile directory ACL is the control.
        }

        _ = new TokenStore(StorePath);

        Assert.Equal(TokenStore.SecretDirectoryMode,
                     File.GetUnixFileMode(Path.GetDirectoryName(StorePath)!));
    }

    [Fact]
    public void WritingATokenDoesNotWidenPermissions() {
        if (!TokenStore.UnixPermissionsApply) {
            return; // POSIX-only assertion; on Windows the profile directory ACL is the control.
        }

        TokenStore store = new(StorePath);
        store.SetToken(SampleKey(), SampleToken());

        Assert.Equal(TokenStore.SecretFileMode, File.GetUnixFileMode(StorePath));
    }

    [Fact]
    public void TokensAreActuallyInTheFileSoThePermissionsMatter() {
        // Keeps the tests above honest: if the store ever stopped writing cleartext tokens,
        // this fails and the mode assertions can be revisited rather than quietly guarding
        // nothing.
        TokenStore store = new(StorePath);
        store.SetToken(SampleKey(), SampleToken());

        string contents = File.ReadAllText(StorePath);

        Assert.Contains("access-secret", contents);
        Assert.Contains("refresh-secret", contents);
    }

    [Fact]
    public void AWorldReadableCacheFromAnEarlierBuildIsRepaired() {
        if (!TokenStore.UnixPermissionsApply) {
            return; // POSIX-only assertion; on Windows the profile directory ACL is the control.
        }

        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, "{}");
        File.SetUnixFileMode(StorePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite |
            UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        _ = new TokenStore(StorePath);

        Assert.Equal(TokenStore.SecretFileMode, File.GetUnixFileMode(StorePath));
    }

    [Fact]
    public void AnExistingDirectoryIsLeftAlone() {
        if (!TokenStore.UnixPermissionsApply) {
            return; // POSIX-only assertion; on Windows the profile directory ACL is the control.
        }

        // --cache can point anywhere, including a shared directory that is not ours to
        // re-permission. The 0600 file is what protects the tokens; directory mode governs
        // listing and traversal, not reads of the file inside it.
        string dir = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(dir);
        UnixFileMode original = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(dir, original);

        _ = new TokenStore(StorePath);

        Assert.Equal(original, File.GetUnixFileMode(dir));
        // The file inside is still locked down, which is the part that matters.
        Assert.Equal(TokenStore.SecretFileMode, File.GetUnixFileMode(StorePath));
    }

    [Fact]
    public void StoredTokenRoundTrips() {
        // The hardening must not break the thing it protects.
        TokenStore store = new(StorePath);
        TokenStore.Key key = SampleKey();
        store.SetToken(key, SampleToken());

        TokenStore.Data? read = new TokenStore(StorePath).GetToken(key);

        Assert.NotNull(read);
        Assert.Equal("access-secret", read!.Response.AccessToken.Token);
        Assert.Equal("refresh-secret", read.Response.RefreshToken.Token);
    }
}
