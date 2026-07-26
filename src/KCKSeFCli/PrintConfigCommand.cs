using System.Text.Json;

using CommandLine;

using Microsoft.Extensions.DependencyInjection;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace KCKSeFCli;

[Verb("PrintConfig", HelpText = "Print the active configuration")]
public class PrintConfigCommand : IWithConfigCommand {
    [Option("json", HelpText = "Output configuration in JSON format")]
    public bool JsonOutput { get; set; }

    [Option("reveal", HelpText = "Print secrets (private key, certificate password, KSeF token) in cleartext")]
    public bool Reveal { get; set; }

    /// <summary>Stands in for a secret that is configured but withheld.</summary>
    public const string RedactionMarker = "<redacted>";

    private static string? Mask(string? value) =>
        // An absent field stays absent: replacing null would claim a key is configured when
        // none is. An empty string carries no secret, so it is left as it is.
        string.IsNullOrEmpty(value) ? value : RedactionMarker;

    /// <summary>
    /// Returns a copy of the profile with secret values replaced, leaving everything else intact.
    ///
    /// ConfigLoader resolves Private_Key_File, Password_Env, Password_File and Password_Cmd into
    /// their contents before any command sees the profile, so by this point the profile holds
    /// the actual PEM key and the actual password however they were originally configured.
    ///
    /// The _File / _Env / _Cmd fields are kept: they say where a secret comes from without
    /// disclosing it, and "which key am I actually using" is the reason to run this command.
    /// The inline Certificate is masked too — it is normally just a public certificate, but it
    /// is bulk output of no diagnostic value and a PKCS#12 blob dropped there would be key
    /// material.
    /// </summary>
    public static ProfileConfig Redact(ProfileConfig config) => new() {
        Environment = config.Environment,
        Nip = config.Nip,
        Token = Mask(config.Token),
        Verify_Certificate_Chain = config.Verify_Certificate_Chain,
        Certificate = config.Certificate is null ? null : new CertificateConfig {
            Private_Key = Mask(config.Certificate.Private_Key),
            Certificate = Mask(config.Certificate.Certificate),
            Password = Mask(config.Certificate.Password),
            Private_Key_File = config.Certificate.Private_Key_File,
            Certificate_File = config.Certificate.Certificate_File,
            Password_Env = config.Certificate.Password_Env,
            Password_File = config.Certificate.Password_File,
            Password_Cmd = config.Certificate.Password_Cmd,
        },
    };

    /// <summary>
    /// Renders the active profile. Pure, so the "no secret reaches stdout" guarantee can be
    /// asserted on the exact string the command prints.
    /// </summary>
    public static string Render(ProfileConfigWithName config, bool json, bool reveal) {
        ProfileConfig profile = reveal ? new ProfileConfig {
            Environment = config.Environment,
            Nip = config.Nip,
            Certificate = config.Certificate,
            Token = config.Token,
            Verify_Certificate_Chain = config.Verify_Certificate_Chain,
        } : Redact(config);

        var report = new {
            active_profile = config.Name,
            profile,
        };

        if (json) {
            return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        }

        ISerializer serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        return serializer.Serialize(report);
    }

    public override Task<int> ExecuteInScopeAsync(IServiceScope scope, CancellationToken cancellationToken) {
        ProfileConfigWithName config = Config();

        if (Reveal) {
            // Goes to stderr, so it is visible in a transcript without polluting parsed output.
            Log.Warning("--reveal: printing secrets in cleartext.");
        }

        Console.WriteLine(Render(config, JsonOutput, Reveal));

        return Task.FromResult(0);
    }
}
