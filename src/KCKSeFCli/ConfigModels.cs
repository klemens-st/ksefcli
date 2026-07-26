using KSeF.Client.Core.Models.Authorization;

namespace KCKSeFCli;

public sealed class KCKSeFCliConfig {
    public string ActiveProfile { get; init; } = "";
    public Dictionary<string, ProfileConfig> Profiles { get; init; } = new();
}

public class ProfileConfig {
    public string Environment { get; init; } = "";
    public string Nip { get; init; } = "";
    public CertificateConfig? Certificate { get; init; }
    public string? Token { get; init; }

    /// <summary>
    /// Overrides <see cref="VerifyCertificateChain"/>. Leave unset to follow the environment.
    /// </summary>
    public bool? Verify_Certificate_Chain { get; init; }

    public AuthMethod AuthMethod => Certificate != null ? AuthMethod.Xades : AuthMethod.KsefToken;

    /// <summary>
    /// Whether KSeF should verify that the certificate signing the XAdES authentication request
    /// chains to a trusted CA.
    ///
    /// It is a query parameter on /v2/auth/xades-signature, so this asks the server to perform
    /// the check rather than performing one locally. Off is right for the test environment,
    /// where self-signed certificates are the norm; anywhere else it discards a check KSeF is
    /// offering to make. Anything other than "test" — including an unrecognised environment —
    /// therefore verifies.
    /// </summary>
    public bool VerifyCertificateChain =>
        Verify_Certificate_Chain ?? !string.Equals(Environment, "test", StringComparison.OrdinalIgnoreCase);
}

public sealed class CertificateConfig {
    public string? Private_Key { get; init; }
    public string? Private_Key_File { get; init; }
    public string? Certificate { get; init; }
    public string? Certificate_File { get; init; }
    public string? Password { get; init; }
    public string? Password_Env { get; init; }
    public string? Password_File { get; init; }
    public List<string>? Password_Cmd { get; init; }

    public AuthenticationTokenSubjectIdentifierTypeEnum SubjectIdentifierType => AuthenticationTokenSubjectIdentifierTypeEnum.CertificateSubject;
}

public sealed class ProfileConfigWithName : ProfileConfig {
    public string Name { get; set; }

    public ProfileConfigWithName(ProfileConfig original, string name) {
        Name = name;
        Environment = original.Environment;
        Nip = original.Nip;
        Certificate = original.Certificate;
        Token = original.Token;
        Verify_Certificate_Chain = original.Verify_Certificate_Chain;
    }

    public ProfileConfigWithName(ProfileConfigWithName original) {
        Name = original.Name;
        Environment = original.Environment;
        Nip = original.Nip;
        Certificate = original.Certificate;
        Token = original.Token;
        Verify_Certificate_Chain = original.Verify_Certificate_Chain;
    }
}

public enum AuthMethod {
    Xades,
    KsefToken
}
