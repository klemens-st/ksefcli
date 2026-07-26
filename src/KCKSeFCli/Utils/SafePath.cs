namespace KCKSeFCli.Utils;

/// <summary>
/// Turns KSeF-supplied identifiers into filenames that cannot escape the directory they are
/// written into.
///
/// Invoice numbers are chosen by whoever issued the invoice and KSeF numbers are chosen by
/// KSeF; neither is ours, so neither belongs in a path unfiltered.
/// </summary>
public static class SafePath {
    /// <summary>Used when nothing usable survives sanitisation.</summary>
    public const string Fallback = "bez-nazwy";

    /// <summary>
    /// Leaves room for the ".json" / ".xml" / ".pdf" callers append, under the 255-byte limit
    /// most filesystems put on a single path component.
    /// </summary>
    private const int MaxLength = 200;

    /// <summary>
    /// Device names Windows reserves regardless of extension: CON.xml opens the console.
    /// </summary>
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase) {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Reduces <paramref name="name"/> to a single path component that is safe to combine with
    /// an output directory.
    ///
    /// Everything outside letters, digits, '-', '_' and '.' becomes '_', which covers both
    /// separators and the ordinary case: Polish invoice numbers routinely contain a slash
    /// (0004/26), so before this they crashed the download rather than escaping anywhere.
    /// Letters are matched by Unicode category, so Polish diacritics survive intact.
    ///
    /// The result never contains a separator, is never "." or "..", never ends in a dot or
    /// space, never collides with a Windows device name, and is never empty. That is what makes
    /// Path.Combine safe again — given a rooted second argument it discards the first entirely,
    /// so an invoice numbered "/etc/cron.d/x" used to write outside the output directory.
    /// </summary>
    public static string SafeFileName(string? name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return Fallback;
        }

        char[] chars = name!.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++) {
            char c = chars[i];
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.') {
                chars[i] = '_';
            }
        }

        // Windows strips trailing dots and spaces, which would silently merge names that look
        // distinct.
        string safe = new string(chars).TrimEnd('.', ' ');

        // "." and ".." are directory references, not names, whatever extension gets appended.
        if (safe.Length == 0 || safe.Trim('.').Length == 0) {
            return Fallback;
        }

        if (safe.Length > MaxLength) {
            safe = safe.Substring(0, MaxLength).TrimEnd('.', ' ');
            if (safe.Length == 0) {
                return Fallback;
            }
        }

        // Windows matches the stem before the first dot, so check that rather than the whole.
        int dot = safe.IndexOf('.');
        string stem = dot < 0 ? safe : safe.Substring(0, dot);
        if (WindowsReservedNames.Contains(stem)) {
            safe = "_" + safe;
        }

        return safe;
    }

    /// <summary>
    /// <see cref="SafeFileName"/>, logging when the name had to be changed so the mapping from
    /// invoice number to file on disk is never silent.
    /// </summary>
    public static string SafeFileNameLogged(string? name) {
        string safe = SafeFileName(name);
        if (safe != name) {
            Log.Warning($"Nazwa pliku '{name}' nie nadaje się do zapisu; używam '{safe}'.");
        }
        return safe;
    }
}
