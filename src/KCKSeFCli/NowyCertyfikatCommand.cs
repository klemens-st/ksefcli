using System.Text.Json;

using CommandLine;

using KSeF.Client.Api.Builders.Certificates;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models.Certificates;

using Microsoft.Extensions.DependencyInjection;

using KCKSeFCli;

namespace KCKSeFCli;

[Verb("NowyCertyfikat", HelpText = "Generate a new KSeF certificate.")]
public class NowyCertyfikatCommand : IWithConfigCommand {
    [Option("certificateName", Required = true, HelpText = "Name for the new certificate.")]
    public string CertificateName { get; set; }

    [Option("certificateType", Default = "Authentication", HelpText = "Type of certificate (Authentication or Offline).")]
    public string CertificateType { get; set; }

    [Option("csrOutputPath", HelpText = "Output file path to save the generated CSR (Base64 encoded).")]
    public string? CsrOutputPath { get; set; }

    [Option("privateKeyOutputPath", HelpText = "Output file path to save the generated private key (Base64 encoded).")]
    public string? PrivateKeyOutputPath { get; set; }

    [Option("certificateOutputPath", HelpText = "Output file path to save the issued certificate (Base64 encoded).")]
    public string? CertificateOutputPath { get; set; }

    [Option("validFrom", HelpText = "Start date for certificate validity (e.g., 2023-01-01). If not provided, current date is used.")]
    public string? ValidFrom { get; set; }

    [Option("yes", HelpText = "Potwierdź nieodwracalną operację w środowisku produkcyjnym bez pytania.")]
    public bool AssumeYes { get; set; }

    public override async Task<int> ExecuteInScopeAsync(IServiceScope scope, CancellationToken cancellationToken) {
        // Enrolment draws on a limited quota (see SprawdzLimitCertyfikatow), so an agent looping
        // on this burns something a later command cannot give back.
        RequireConfirmation(AssumeYes, $"wystawienie nowego certyfikatu {CertificateName}");

        IKSeFClient ksefClient = scope.ServiceProvider.GetRequiredService<IKSeFClient>();
        ICryptographyService cryptographyService = await GetCryptographicService(scope, cancellationToken).ConfigureAwait(false);
        string accessToken = await GetAccessToken(scope, cancellationToken).ConfigureAwait(false);

        if (!Enum.TryParse(CertificateType, true, out CertificateType type)) {
            throw new ArgumentException($"Invalid certificate type: {CertificateType}");
        }

        Log.Information("1. Sprawdzenie limitów (Skipped for now).");

        Log.Information("2. Pobranie danych do wniosku certyfikacyjnego.");
        CertificateEnrollmentsInfoResponse enrollmentInfo = await ksefClient.GetCertificateEnrollmentDataAsync(accessToken, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Enrollment Info: {JsonSerializer.Serialize(enrollmentInfo, new JsonSerializerOptions { WriteIndented = true })}");

        Log.Information("3. Przygotowanie CSR (Certificate Signing Request).");
        (string? csrBase64, string? privateKeyBase64) = cryptographyService.GenerateCsrWithEcdsa(enrollmentInfo);

        if (!string.IsNullOrEmpty(CsrOutputPath)) {
            File.WriteAllText(CsrOutputPath!, csrBase64!);
            Console.WriteLine($"CSR saved to {CsrOutputPath}");
        }
        if (!string.IsNullOrEmpty(PrivateKeyOutputPath)) {
            File.WriteAllText(PrivateKeyOutputPath!, privateKeyBase64!);
            Console.WriteLine($"Private key saved to {PrivateKeyOutputPath}");
        }

        Log.Information("4. Wysłanie wniosku certyfikacyjnego.");
        SendCertificateEnrollmentRequest sendRequest = SendCertificateEnrollmentRequestBuilder
            .Create()
            .WithCertificateName(CertificateName)
            .WithCertificateType(type)
            .WithCsr(csrBase64)
            .WithValidFrom(string.IsNullOrEmpty(ValidFrom) ? DateTimeOffset.UtcNow : await ParseDate.Parse(ValidFrom!, cancellationToken).ConfigureAwait(false))
            .Build();

        CertificateEnrollmentResponse enrollmentResponse = await ksefClient.SendCertificateEnrollmentAsync(sendRequest, accessToken, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Enrollment Response: {JsonSerializer.Serialize(enrollmentResponse, new JsonSerializerOptions { WriteIndented = true })}");

        Log.Information("5. Sprawdzenie statusu wniosku.");
        string referenceNumber = enrollmentResponse.ReferenceNumber;
        DateTime startTime = DateTime.UtcNow;
        TimeSpan timeout = TimeSpan.FromMinutes(5);
        CertificateEnrollmentStatusResponse statusResponse;

        do {
            statusResponse = await ksefClient.GetCertificateEnrollmentStatusAsync(referenceNumber, accessToken, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"Status: {statusResponse.Status.Code} - {statusResponse.Status.Description} | Elapsed: {DateTime.UtcNow - startTime:mm:ss}");
            if (statusResponse.Status.Code == 200 || statusResponse.Status.Code == 120) {
                break;
            }
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
        while ((DateTime.UtcNow - startTime) < timeout);

        if (statusResponse.Status.Code != 200) {
            throw new InvalidOperationException($"Certificate enrollment failed or timed out: {statusResponse.Status.Description}");
        }

        Log.Information("6. Pobieranie wystawionego certyfikatu.");
        if (!string.IsNullOrEmpty(statusResponse.CertificateSerialNumber) && !string.IsNullOrEmpty(CertificateOutputPath)) {
            CertificateListRequest certListRequest = new CertificateListRequest { CertificateSerialNumbers = new[] { statusResponse.CertificateSerialNumber! } };
            CertificateListResponse certificateListResponse = await ksefClient.GetCertificateListAsync(certListRequest, accessToken, cancellationToken).ConfigureAwait(false);
            CertificateResponse? issuedCert = certificateListResponse.Certificates.FirstOrDefault();
            if (issuedCert != null) {
                File.WriteAllText(CertificateOutputPath!, issuedCert.Certificate);
                Console.WriteLine($"Issued certificate saved to {CertificateOutputPath}");
            } else {
                Console.Error.WriteLine($"Error: Issued certificate with serial number {statusResponse.CertificateSerialNumber} not found.");
            }
        }

        return 0;
    }
}
