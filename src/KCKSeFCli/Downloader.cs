using KCKSeFCli.Utils;

namespace KCKSeFCli;

public static class Downloader {
    private static readonly HttpClient HttpClient = new();

    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destinationPath"/> and refuses to
    /// leave anything there that does not hash to <paramref name="expectedSha256"/>.
    ///
    /// The cache is content-addressed rather than timestamp-based. With the content pinned,
    /// "is the cached copy current" collapses into "does it hash correctly", which needs no
    /// HEAD request and cannot be fooled by an mtime. The previous timestamp check was worse
    /// than no cache at all: once a file sat there with a recent mtime it was executed forever
    /// without being re-fetched or re-examined.
    ///
    /// The download lands in a sibling temp file and is verified before being moved into place,
    /// so a truncated or substituted artifact never occupies the destination even briefly.
    /// </summary>
    public static async Task DownloadVerifiedFileAsync(
        string url, string destinationPath, string expectedSha256, CancellationToken cancellationToken) {
        if (ArtifactVerification.Matches(destinationPath, expectedSha256)) {
            Log.Information($"{Path.GetFileName(destinationPath)} jest w pamięci podręcznej i zgadza się z sumą SHA-256.");
            return;
        }

        if (File.Exists(destinationPath)) {
            Log.Warning($"Plik {destinationPath} nie zgadza się z oczekiwaną sumą SHA-256; pobieram ponownie.");
        }

        string tempPath = destinationPath + ".download-" + Guid.NewGuid().ToString("N");
        try {
            Log.Information($"Pobieranie {url} -> {destinationPath}");
            byte[] fileBytes = await Compatibility.GetByteArrayAsync(HttpClient, url, cancellationToken).ConfigureAwait(false);
            File.WriteAllBytes(tempPath, fileBytes);

            // Before the move, so a mismatched artifact never reaches the destination.
            ArtifactVerification.Verify(tempPath, expectedSha256);

            if (File.Exists(destinationPath)) {
                File.Delete(destinationPath);
            }
            File.Move(tempPath, destinationPath);
            Log.Information($"Pobrano i zweryfikowano {Path.GetFileName(destinationPath)}.");
        } catch (HttpRequestException e) {
            // No usable cached copy: Matches would have returned above if there were one.
            throw new Exception(
                $"Nie udało się pobrać {url}, a w pamięci podręcznej nie ma zweryfikowanej kopii ({destinationPath}).", e);
        } finally {
            if (File.Exists(tempPath)) {
                File.Delete(tempPath);
            }
        }
    }
}
