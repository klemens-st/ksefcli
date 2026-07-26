using CommandLine;

using KSeF.Client.Api.Builders.Certificates;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Models.Certificates;

using Microsoft.Extensions.DependencyInjection;

namespace KCKSeFCli;

[Verb("UniewaznijCertyfikat", HelpText = "Revoke a KSeF certificate.")]
public class UniewaznijCertyfikatCommand : IWithConfigCommand {
    [Value(0, Required = true, HelpText = "Certificate serial number to revoke.")]
    public string CertificateSerialNumber { get; set; }

    [Option("reason", Default = "Other", HelpText = "Revocation reason. Possible values: KeyCompromise, AffiliationChanged, Superseded, CessationOfOperation, Other.")]
    public string RevocationReason { get; set; }

    [Option("yes", HelpText = "Potwierdź nieodwracalną operację w środowisku produkcyjnym bez pytania.")]
    public bool AssumeYes { get; set; }

    public override async Task<int> ExecuteInScopeAsync(IServiceScope scope, CancellationToken cancellationToken) {
        RequireConfirmation(AssumeYes, $"unieważnienie certyfikatu {CertificateSerialNumber}");

        IKSeFClient ksefClient = scope.ServiceProvider.GetRequiredService<IKSeFClient>();
        string accessToken = await GetAccessToken(scope, cancellationToken).ConfigureAwait(false);

        if (!Enum.TryParse(RevocationReason, true, out CertificateRevocationReason reason)) {
            throw new ArgumentException($"Invalid revocation reason: {RevocationReason}");
        }

        CertificateRevokeRequest request = RevokeCertificateRequestBuilder
            .Create()
            .WithRevocationReason(reason)
            .Build();

        await ksefClient.RevokeCertificateAsync(request, CertificateSerialNumber, accessToken, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Certificate {CertificateSerialNumber} revoked successfully.");

        return 0;
    }
}
