using System.Security.Cryptography;

namespace KCKSeFCli.Utils;

/// <summary>
/// Checks that a downloaded artifact is the exact bytes we expect before anything is done with
/// it. Used for the PDF generator, which is fetched from a GitHub release and then executed.
/// </summary>
public static class ArtifactVerification {
    /// <summary>Thrown when an artifact is not the one that was pinned.</summary>
    public sealed class IntegrityException : Exception {
        public IntegrityException(string message) : base(message) { }
    }

    /// <summary>Lowercase hex SHA-256 of the file's contents.</summary>
    public static string Sha256Hex(string path) {
        using FileStream stream = File.OpenRead(path);
#if NET6_0_OR_GREATER
        byte[] hash = SHA256.HashData(stream);
#else
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(stream);
#endif
        return Compatibility.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Throws unless <paramref name="path"/> hashes to <paramref name="expectedSha256"/>.
    ///
    /// An absent or empty expectation is a failure, not a licence to skip the check: a pin that
    /// went missing must not silently turn verification off.
    /// </summary>
    public static void Verify(string path, string expectedSha256) {
        if (string.IsNullOrWhiteSpace(expectedSha256)) {
            throw new IntegrityException(
                $"Brak oczekiwanej sumy SHA-256 dla {path}; odmawiam użycia niezweryfikowanego pliku.");
        }

        if (!File.Exists(path)) {
            throw new IntegrityException($"Plik {path} nie istnieje, więc nie da się go zweryfikować.");
        }

        string actual = Sha256Hex(path);
        if (!string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase)) {
            throw new IntegrityException(
                $"Niezgodna suma SHA-256 dla {path}: oczekiwano {expectedSha256.Trim().ToLowerInvariant()}, "
                + $"otrzymano {actual}.");
        }
    }

    /// <summary>Non-throwing form, for deciding whether a cached copy can be reused.</summary>
    public static bool Matches(string path, string expectedSha256) {
        try {
            Verify(path, expectedSha256);
            return true;
        } catch (IntegrityException) {
            return false;
        } catch (IOException) {
            return false;
        }
    }
}
