using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace KCKSeFCli;

public static class ConfigLoader {
    public static KCKSeFCliConfig Load(string configPath, string? activeProfileNameOverride) {
        string absoluteConfigPath = Path.GetFullPath(configPath);
        if (!File.Exists(absoluteConfigPath)) {
            throw new FileNotFoundException($"Configuration file not found at {absoluteConfigPath}");
        }

        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        KCKSeFCliConfig config;
        try {
            config = deserializer.Deserialize<KCKSeFCliConfig>(
                File.ReadAllText(absoluteConfigPath)
            );
        } catch (YamlException ex) {
            throw new Exception($"Exception during deserialization of '{absoluteConfigPath}'", ex);
        }

        string activeProfile = activeProfileNameOverride ?? config.ActiveProfile;

        if (string.IsNullOrWhiteSpace(activeProfile)) {
            if (config.Profiles.Count == 1) {
                activeProfile = config.Profiles.Keys.First();
            } else {
                throw new InvalidOperationException("Active profile not specified in config file or via --active option.");
            }
        }

        if (!config.Profiles.TryGetValue(activeProfile, out ProfileConfig? profile)) {
            throw new InvalidOperationException($"Active profile '{activeProfile}' not found in configuration.");
        }

        string? configDir = Path.GetDirectoryName(absoluteConfigPath);

        Dictionary<string, ProfileConfig> resolvedProfiles = new Dictionary<string, ProfileConfig>();
        foreach ((string? profileName, ProfileConfig? profileConfig) in config.Profiles) {
            if (profileConfig != null && profileConfig.Certificate is not null && configDir is not null) {
                CertificateConfig cert = profileConfig.Certificate;

                int pkCount = (cert.Private_Key != null ? 1 : 0) + (cert.Private_Key_File != null ? 1 : 0);
                if (pkCount > 1) {
                    throw new InvalidOperationException($"Profile '{profileName}' has conflicting private key configurations. Specify only one of 'private_key' or 'private_key_file'.");
                }

                int certCount = (cert.Certificate != null ? 1 : 0) + (cert.Certificate_File != null ? 1 : 0);
                if (certCount > 1) {
                    throw new InvalidOperationException($"Profile '{profileName}' has conflicting certificate configurations. Specify only one of 'certificate' or 'certificate_file'.");
                }

                int passCount = (cert.Password != null ? 1 : 0) + (cert.Password_Env != null ? 1 : 0) + (cert.Password_File != null ? 1 : 0) + (cert.Password_Cmd != null ? 1 : 0);
                if (passCount > 1) {
                    throw new InvalidOperationException($"Profile '{profileName}' has conflicting password configurations. Specify only one of 'password', 'password_env', 'password_file', or 'password_cmd'.");
                }

                string? resolvedPrivateKey = ResolveContent(cert.Private_Key, cert.Private_Key_File, configDir);
                string? resolvedCertificate = ResolveContent(cert.Certificate, cert.Certificate_File, configDir);
                
                string? resolvedPassword = cert.Password ??
                                           (cert.Password_Env is not null ? System.Environment.GetEnvironmentVariable(cert.Password_Env) : null) ??
                                           ResolveContent(null, cert.Password_File, configDir);
                if (resolvedPassword == null && cert.Password_Cmd is not null && cert.Password_Cmd.Count > 0) {
                    Subprocess subprocess = new Subprocess(cert.Password_Cmd, configDir);
                    byte[] output = subprocess.CheckOutputAsync().GetAwaiter().GetResult();
                    resolvedPassword = System.Text.Encoding.UTF8.GetString(output).TrimEnd('\r', '\n');
                }

                CertificateConfig newCert = new CertificateConfig {
                    Private_Key = resolvedPrivateKey,
                    Certificate = resolvedCertificate,
                    Password = resolvedPassword,
                    Private_Key_File = cert.Private_Key_File,
                    Certificate_File = cert.Certificate_File,
                    Password_Env = cert.Password_Env,
                    Password_File = cert.Password_File,
                    Password_Cmd = cert.Password_Cmd,
                };

                resolvedProfiles[profileName!] = new ProfileConfig {
                    Certificate = newCert,
                    Environment = profileConfig.Environment,
                    Nip = !string.IsNullOrEmpty(profileConfig.Nip) ? profileConfig.Nip : NipUtils.GetNipFromCertificate(newCert.Certificate) ?? "",
                    Token = profileConfig.Token,
                    Verify_Certificate_Chain = profileConfig.Verify_Certificate_Chain,
                };
            } else if (profileConfig != null) {
                resolvedProfiles[profileName!] = new ProfileConfig {
                    Certificate = null,
                    Environment = profileConfig.Environment,
                    Nip = !string.IsNullOrEmpty(profileConfig.Nip) ? profileConfig.Nip : !string.IsNullOrEmpty(profileConfig.Token) ? NipUtils.ExtractNipFromToken(profileConfig.Token!) : "",
                    Token = profileConfig.Token,
                    Verify_Certificate_Chain = profileConfig.Verify_Certificate_Chain,
                };
            }
        }

        KCKSeFCliConfig finalConfig = new KCKSeFCliConfig {
            ActiveProfile = activeProfile,
            Profiles = resolvedProfiles,
        };

        ValidateProfile(finalConfig.Profiles[finalConfig.ActiveProfile]);

        return finalConfig;
    }

    private static string? ResolveContent(string? content, string? filePath, string configDir) {
        if (!string.IsNullOrWhiteSpace(content)) {
            return content;
        }

        if (string.IsNullOrWhiteSpace(filePath)) {
            return null;
        }

        string path = ExpandTilde(filePath!);
        if (!Path.IsPathRooted(path)) {
            path = Path.GetFullPath(Path.Combine(configDir, path));
        }

        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    private static void ValidateProfile(ProfileConfig profile) {
        bool hasCert = profile.Certificate != null;
        bool hasToken = !string.IsNullOrWhiteSpace(profile.Token);

        if (!string.IsNullOrEmpty(profile.Nip)) {
            NipUtils.AssertNipIsValid(profile.Nip);
        }

        if (hasCert == hasToken) {
            throw new InvalidOperationException(
                "Profile must define either certificate or token, exactly one."
            );
        }

        if (hasCert) {
            if (string.IsNullOrEmpty(profile.Certificate!.Private_Key)) {
                throw new InvalidOperationException("Private key is not configured.");
            }

            if (string.IsNullOrEmpty(profile.Certificate.Certificate)) {
                throw new InvalidOperationException("Certificate is not configured.");
            }

            if (string.IsNullOrEmpty(profile.Certificate.Password)) {
                throw new InvalidOperationException("Certificate password is not set.");
            }
        }
    }

    private static string ExpandTilde(string path) {
        if (string.IsNullOrEmpty(path) || !path.StartsWith("~")) {
            return path;
        }

        return path.Replace("~", System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile));
    }
}
