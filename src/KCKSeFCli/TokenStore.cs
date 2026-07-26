using System.Text;
using System.Text.Json;

using KSeF.Client.Core.Models.Authorization;

namespace KCKSeFCli;

public class TokenStore {
    public record Data {
        public AuthenticationOperationStatusResponse Response { get; init; }
        public Data(AuthenticationOperationStatusResponse Response) {
            if (Response is null) {
                throw new Exception("Response is null");
            }

            if (Response.AccessToken is null) {
                throw new Exception("Response.AccessToken is null");
            }

            if (Response.RefreshToken is null) {
                throw new Exception("Response.RefreshToken is null");
            }

            this.Response = Response;
        }
    }

    public record Key(string Nazwa, ProfileConfig Profile) {
        public string ToCacheKey() {
            string profileJson = System.Text.Json.JsonSerializer.Serialize(Profile);
            string nip = Profile.Nip;
            string environment = Profile.Environment;
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(profileJson);
#if NET6_0_OR_GREATER
            byte[] hash = System.Security.Cryptography.SHA256.HashData(bytes);
#else
            byte[] hash = Compatibility.SHA256HashData(bytes);
#endif
            string hashString = Compatibility.ToHexString(hash).ToLower();
            return $"{Nazwa}_{environment}_{nip}_{hashString}";
        }
    }

    private readonly string _path;
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

#if NET7_0_OR_GREATER
    /// <summary>0600 - the store holds bearer and refresh tokens in cleartext.</summary>
    public const UnixFileMode SecretFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>0700, applied only to directories we create ourselves.</summary>
    public const UnixFileMode SecretDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode ModesBeyondOwner =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
#endif

    /// <summary>
    /// Whether this platform has POSIX permission bits to enforce. Windows ACLs already deny
    /// other users access to a file under the profile directory, so there is nothing to do there.
    /// </summary>
    [System.Runtime.Versioning.UnsupportedOSPlatformGuard("windows")]
    public static bool UnixPermissionsApply =>
#if NET7_0_OR_GREATER
        !OperatingSystem.IsWindows();
#else
        false;
#endif

    /// <summary>
    /// Creates the store's directory and file with owner-only permissions before anything is
    /// written to them.
    ///
    /// The file is created with its mode set atomically, never chmod'd after the fact, so there
    /// is no window in which a token sits in a world-readable file. An existing file created by
    /// an older build is repaired and the repair is logged.
    ///
    /// An existing directory is left alone deliberately. The path is caller-supplied via
    /// --cache, so it may be shared, and tightening it could lock other users out of something
    /// that is not ours. It is also not load-bearing: directory permissions govern listing and
    /// traversal, not reads of a 0600 file inside.
    /// </summary>
    public static void PrepareSecureStore(string path) {
        string directory = Path.GetDirectoryName(path)!;
#if NET7_0_OR_GREATER
        if (UnixPermissionsApply) {
            if (!Directory.Exists(directory)) {
                Directory.CreateDirectory(directory, SecretDirectoryMode);
            } else if ((File.GetUnixFileMode(directory) & ModesBeyondOwner) != 0) {
                Log.Warning($"Token cache directory {directory} is accessible to other users. "
                            + "The cache file itself is restricted to you.");
            }

            try {
                // Atomic: the file never exists with a wider mode, not even briefly. CreateNew
                // rather than a File.Exists check, so a concurrent creator cannot slip through.
                using FileStream _ = new(path, new FileStreamOptions {
                    Mode = System.IO.FileMode.CreateNew,
                    Access = FileAccess.Write,
                    UnixCreateMode = SecretFileMode,
                });
            } catch (IOException) when (File.Exists(path)) {
                // Already there, possibly from a build that predates this. Repair it.
                if ((File.GetUnixFileMode(path) & ModesBeyondOwner) != 0) {
                    Log.Warning($"Token cache {path} was accessible to other users. Restricting it to you.");
                    File.SetUnixFileMode(path, SecretFileMode);
                }
            }
            return;
        }
#endif
        Directory.CreateDirectory(directory);
    }

    public TokenStore(string path) {
        _path = Environment.ExpandEnvironmentVariables(path);
        PrepareSecureStore(_path);
        Log.Information($"Token store loaded from: {_path}");
    }

    private Dictionary<string, Data> LoadTokens(LockedFileStream lockFile) {
        if (lockFile.Fs.Length == 0) {
            Dictionary<string, Data> empty = new Dictionary<string, Data>();
            byte[] emptyData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(empty, _jsonOptions));
            lockFile.Fs.Write(emptyData, 0, emptyData.Length);
            lockFile.Fs.Flush(true);
            return empty;
        }
        lockFile.Fs.Seek(0, SeekOrigin.Begin);
        byte[] data = new byte[lockFile.Fs.Length];
        lockFile.Fs.ReadExactly(data, 0, data.Length);
        try {
            return JsonSerializer.Deserialize<Dictionary<string, Data>>(data, _jsonOptions) ?? new Dictionary<string, Data>();
        } catch (Exception e) when (e is JsonException || e is Exception) {
            Log.Warning($"Invalid JSON in token cache file: {_path}. Overwriting with empty data.");
            lockFile.Fs.Seek(0, SeekOrigin.Begin);
            lockFile.Fs.SetLength(0);
            Dictionary<string, Data> empty = new Dictionary<string, Data>();
            byte[] emptyData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(empty, _jsonOptions));
            lockFile.Fs.Write(emptyData, 0, emptyData.Length);
            lockFile.Fs.Flush(true);
            return empty;
        }
    }

    public Data? GetToken(Key key) {
        using (LockedFileStream lockFile = new LockedFileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
            Dictionary<string, Data> tokens = LoadTokens(lockFile);
            if (tokens.TryGetValue(key.ToCacheKey(), out Data? token)) {
                string invalidReason = token?.Response is null ? "Response is null" :
                                       token.Response.RefreshToken is null ? "RefreshToken is null" :
                                       token.Response.AccessToken is null ? "AccessToken is null" : "";
                if (!string.IsNullOrEmpty(invalidReason)) {
                    Log.Warning($"Invalid token data found in cache for key: {key.ToCacheKey()} (reason: {invalidReason}). Deleting the entry.");
                    tokens.Remove(key.ToCacheKey());
                    lockFile.Fs.Seek(0, SeekOrigin.Begin);
                    lockFile.Fs.SetLength(0);
                    byte[] newData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tokens, _jsonOptions));
                    lockFile.Fs.Write(newData, 0, newData.Length);
                    lockFile.Fs.Flush(true);
                    return null;
                }
                return token;
            }
            return null;
        }
    }

    public void SetToken(Key key, Data token) {
        using (LockedFileStream lockFile = new LockedFileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
            Dictionary<string, Data> tokens = LoadTokens(lockFile);
            tokens[key.ToCacheKey()] = token;
            lockFile.Fs.Seek(0, SeekOrigin.Begin);
            lockFile.Fs.SetLength(0);
            byte[] newData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tokens, _jsonOptions));
            lockFile.Fs.Write(newData, 0, newData.Length);
            lockFile.Fs.Flush(true);
        }
    }

    public bool RemoveToken(Key key) {
        using (LockedFileStream lockFile = new LockedFileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
            Dictionary<string, Data> tokens = LoadTokens(lockFile);
            if (tokens.Remove(key.ToCacheKey())) {
                lockFile.Fs.Seek(0, SeekOrigin.Begin);
                lockFile.Fs.SetLength(0);
                byte[] newData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tokens, _jsonOptions));
                lockFile.Fs.Write(newData, 0, newData.Length);
                lockFile.Fs.Flush(true);
                return true;
            }
            return false;
        }
    }
}
