using System.Text.Json;
using System.Xml.Linq;

using CommandLine;

using KSeF.Client.Api.Builders.Batch;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models.ApiResponses;
using KSeF.Client.Core.Models.Invoices;
using KSeF.Client.Core.Models.Sessions;
using KSeF.Client.Core.Models.Sessions.BatchSession;
using KCKSeFCli.Utils;

using Microsoft.Extensions.DependencyInjection;



namespace KCKSeFCli;

[Verb("PrzeslijFaktury", HelpText = "Upload invoices in XML format.")]
public class PrzeslijFakturyCommand : IWithConfigCommand {
    [Value(0, Min = 1, Required = true, HelpText = "Paths to XML invoice files.")]
    public required IEnumerable<string> Pliki { get; set; }

    [Option('u', "upodir", Required = false, HelpText = "katalog do zapisu plikow upo")]
    public string? UpoDir { get; set; }

    [Option("upopdf", Required = false, HelpText = "convertuj upo od razu na pdf")]
    public bool UpoPdf { get; set; }

    [Option("uposesji", Required = false, HelpText = "Zapisz UPO sesji (zbiorcze upo)")]
    public bool UpoSesji { get; set; } = false;

    [Option("offlinemode", Required = false, HelpText = "Ustaw jeśli chcesz ustawic offline mode")]
    public bool OfflineModeOption { get; set; } = false;

    public static IEnumerable<(string FileName, byte[] Content)> GetFilesWithContent(IEnumerable<string> paths) {
        return paths.Select(path => (
            FileName: Path.GetFileName(path),
            Content: File.ReadAllBytes(path)
        ));
    }

    private sealed record OpenBatchSessionResult(
        string ReferenceNumber,
        OpenBatchSessionResponse OpenBatchSessionResponse,
        List<BatchPartSendingInfo> EncryptedParts
    );

    private const SystemCode DefaultSystemCode = SystemCode.FA3;
    private const string DefaultSchemaVersion = "1-0E";
    private const string DefaultValue = "FA";

    /// <summary>Every declared invoice was accepted by KSeF.</summary>
    public const int ExitAccepted = 0;

    /// <summary>Nothing was accepted: the batch, or every invoice in it, was rejected.</summary>
    public const int ExitRejected = 1;

    /// <summary>
    /// Some invoices were accepted and some were not. Deliberately distinct from
    /// <see cref="ExitRejected"/>: the accepted ones are filed, so re-sending the whole batch
    /// would duplicate them.
    /// </summary>
    public const int ExitPartiallyAccepted = 2;

    /// <summary>Verdict on a finished upload: the process exit code and a one-line summary.</summary>
    public readonly record struct UploadOutcome(int ExitCode, string Summary) {
        public bool IsSuccess => ExitCode == ExitAccepted;
    }

    /// <summary>
    /// Whether the batch session has settled, so polling can stop.
    ///
    /// Keyed off the status code rather than the invoice counts. A batch rejected wholesale
    /// (405, 445, …) can settle with both counts still null, so waiting for a count to appear
    /// waits forever on exactly the runs that most need reporting.
    /// </summary>
    public static bool IsTerminal(SessionStatusResponse? sessionStatus) {
        int? code = sessionStatus?.Status?.Code;
        return code is not null
            && code != BatchSessionCodeResponse.SessionStarted
            && code != BatchSessionCodeResponse.Processing;
    }

    /// <summary>
    /// Turns a settled session status into an exit code.
    ///
    /// Success is asserted, never assumed: it requires a 200 and a confirmed accepted count
    /// matching the declared one. Everything else — a rejection code, any failed invoice,
    /// invoices unaccounted for, absent counts — is a non-zero exit.
    /// </summary>
    public static UploadOutcome DetermineOutcome(SessionStatusResponse? sessionStatus) {
        if (sessionStatus is null) {
            return new UploadOutcome(ExitRejected, "Brak statusu sesji - wynik wysyłki nieznany.");
        }

        int? code = sessionStatus.Status?.Code;
        string description = sessionStatus.Status?.Description ?? "";

        if (!IsTerminal(sessionStatus)) {
            return new UploadOutcome(ExitRejected,
                $"Sesja nie osiągnęła stanu końcowego (kod={code?.ToString() ?? "brak"} {description}). "
                + "Status faktur nieznany.");
        }

        int successful = sessionStatus.SuccessfulInvoiceCount ?? 0;
        int failed = sessionStatus.FailedInvoiceCount ?? 0;
        // When KSeF never reported a declared count, fall back to what it did account for.
        int total = sessionStatus.InvoiceCount ?? (successful + failed);
        string counts = $"przyjęte={successful}, odrzucone={failed}, zadeklarowane={total}, "
                        + $"kod={code} {description}";

        // Anything unaccounted for counts against us: an invoice neither confirmed accepted
        // nor confirmed rejected must not be reported as filed.
        if (failed > 0 || successful < total) {
            return successful > 0
                ? new UploadOutcome(ExitPartiallyAccepted,
                    $"Część faktur nie została przyjęta ({counts}).")
                : new UploadOutcome(ExitRejected,
                    $"Żadna faktura nie została przyjęta ({counts}).");
        }

        if (code != BatchSessionCodeResponse.ProcessedSuccessfully) {
            return new UploadOutcome(ExitRejected, $"Sesja zakończona błędem ({counts}).");
        }

        if (successful == 0) {
            return new UploadOutcome(ExitRejected,
                $"Sesja zakończona bez potwierdzenia przyjęcia jakiejkolwiek faktury ({counts}).");
        }

        return new UploadOutcome(ExitAccepted, $"Wszystkie faktury przyjęte ({counts}).");
    }

    /// <summary>
    /// Buduje żądanie otwarcia sesji wsadowej z kodem formularza i listą zaszyfrowanych partów.
    /// </summary>
    /// <param name="zipMeta">Metadane pliku ZIP.</param>
    /// <param name="encryption">Dane szyfrowania.</param>
    /// <param name="encryptedParts">Lista zaszyfrowanych partów.</param>
    /// <param name="systemCode">Kod systemowy formularza.</param>
    /// <param name="schemaVersion">Wersja schematu.</param>
    /// <param name="value">Wartość formularza.</param>
    /// <returns>Obiekt żądania otwarcia sesji wsadowej.</returns>
    private static OpenBatchSessionRequest BuildOpenBatchRequest(
        FileMetadata zipMeta,
        EncryptionData encryption,
        IEnumerable<BatchPartSendingInfo> encryptedParts,
        SystemCode systemCode = DefaultSystemCode,
        string schemaVersion = DefaultSchemaVersion,
        string value = DefaultValue,
        bool offlineMode = false) {
        IOpenBatchSessionRequestBuilderBatchFile builder = OpenBatchSessionRequestBuilder
            .Create()
            .WithFormCode(systemCode: SystemCodeHelper.GetSystemCode(systemCode), schemaVersion: schemaVersion, value: value)
            .WithOfflineMode(offlineMode)
            .WithBatchFile(fileSize: zipMeta.FileSize, fileHash: zipMeta.HashSHA);

        foreach (BatchPartSendingInfo p in encryptedParts) {
            builder = builder.AddBatchFilePart(
                ordinalNumber: p.OrdinalNumber,
                fileSize: p.Metadata.FileSize,
                fileHash: p.Metadata.HashSHA);
        }

        return builder
            .EndBatchFile()
            .WithEncryption(
                encryptedSymmetricKey: encryption.EncryptionInfo.EncryptedSymmetricKey,
                initializationVector: encryption.EncryptionInfo.InitializationVector)
            .Build();
    }

    private async Task<OpenBatchSessionResult> PrepareAndOpenBatchSessionAsync(
            IEnumerable<(string FileName, byte[] Content)> invoices,
            IKSeFClient ksefClient,
        ICryptographyService cryptographyService,
        string accessToken) {
        EncryptionData encryptionData = cryptographyService.GetEncryptionData();

        Log.Information("1. Przygotowanie paczki ZIP");
        (byte[] zipBytes, FileMetadata zipMeta) =
            BatchUtils.BuildZip(invoices, cryptographyService);

        Log.Information("2. Podział binarny paczki ZIP na części oraz 3. Zaszyfrowanie części paczki");
        List<BatchPartSendingInfo> encryptedParts =
            BatchUtils.EncryptAndSplit(zipBytes, encryptionData, cryptographyService);

        Log.Information("4. Otwarcie sesji wsadowej");
        OpenBatchSessionRequest openBatchRequest = BuildOpenBatchRequest(zipMeta, encryptionData, encryptedParts,
         DefaultSystemCode,
         DefaultSchemaVersion,
         DefaultValue,
         OfflineModeOption);

        OpenBatchSessionResponse openBatchSessionResponse =
            await BatchUtils.OpenBatchAsync(ksefClient, openBatchRequest, accessToken).ConfigureAwait(false);

        return new OpenBatchSessionResult(
            openBatchSessionResponse.ReferenceNumber,
            openBatchSessionResponse,
            encryptedParts
        );
    }

    private static async Task PobranieInformacjiNaTematPrzeslanychFaktur(
            IKSeFClient ksefClient,
            string referenceNumber,
            string accessToken,
            CancellationToken cancellationToken) {
        const int pageSize = 50;
        string? continuationtoken = null;
        do {
            SessionInvoicesResponse sessionInvoices = await ksefClient
                                        .GetSessionInvoicesAsync(
                                        referenceNumber,
                                        accessToken,
                                        pageSize,
                                        continuationtoken,
                                        cancellationToken).ConfigureAwait(false);

            foreach (SessionInvoice sessionInvoice in sessionInvoices.Invoices) {
                Console.Out.WriteLine(JsonSerializer.Serialize(sessionInvoice, new JsonSerializerOptions {
                    WriteIndented = true
                }));
            }

            continuationtoken = sessionInvoices.ContinuationToken;
        }
        while (continuationtoken != null);
    }

    public override async Task<int> ExecuteInScopeAsync(IServiceScope scope, CancellationToken cancellationToken) {
        XML2PDFCommand.Runner? pdfRunner = null;
        if (UpoPdf) {
            pdfRunner = await XML2PDFCommand.GetRunner(cancellationToken).ConfigureAwait(false);
        }

        IEnumerable<(string FileName, byte[] Content)> invoices = GetFilesWithContent(Pliki);

        string accessToken = await GetAccessToken(scope, cancellationToken).ConfigureAwait(false);
        IKSeFClient ksefClient = scope.ServiceProvider.GetRequiredService<IKSeFClient>();
        ICryptographyService cryptographyService = await GetCryptographicService(scope, cancellationToken).ConfigureAwait(false);

        OpenBatchSessionResult result = await PrepareAndOpenBatchSessionAsync(invoices, ksefClient, cryptographyService, accessToken).ConfigureAwait(false);
        string referenceNumber = result.ReferenceNumber;
        Log.Information($"ReferenceNumber={result.ReferenceNumber}");

        Log.Information("5. Przesłanie zadeklarowanych części paczki");
        await ksefClient.SendBatchPartsAsync(result.OpenBatchSessionResponse, result.EncryptedParts).ConfigureAwait(false);

        Log.Information("6. Zamknięcie sesji wsadowej");
        await ksefClient.CloseBatchSessionAsync(result.ReferenceNumber, accessToken).ConfigureAwait(false);

        /* ---------------------------------------------------------------------- */
        Log.Information("sesja-sprawdzenie-stanu-i-pobranie-upo.md");

        Log.Information("4) Oczekiwanie na przetworzenie faktury");
        SessionStatusResponse sessionStatus;
        try {
            sessionStatus = await AsyncPollingUtils.PollWithBackoffAsync(
                action: () => ksefClient.GetSessionStatusAsync(referenceNumber, accessToken, cancellationToken),
                IsTerminal,
                initialDelay: TimeSpan.FromSeconds(1),
                maxDelay: TimeSpan.FromSeconds(5),
                maxAttempts: 30,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        } catch (TimeoutException ex) {
            // Report the unknown outcome rather than letting a stack trace stand in for it.
            // The invoices may or may not have been filed; ReferenceNumber is the way to find out.
            Log.Error($"Sesja {referenceNumber} nie zakończyła przetwarzania w wyznaczonym czasie: {ex.Message}");
            Log.Error("Status faktur nieznany - sprawdź sesję po numerze referencyjnym przed ponowną wysyłką.");
            return ExitRejected;
        }

        Log.Information("3. Pobranie informacji na temat przesłanych faktur");
        await PobranieInformacjiNaTematPrzeslanychFaktur(ksefClient, referenceNumber, accessToken, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(UpoDir)) {
            Directory.CreateDirectory(UpoDir);

            if (UpoSesji && sessionStatus.Upo is not null) {
                // Zbiorcze UPO
                foreach (UpoPageResponse? upo in sessionStatus.Upo.Pages) {
                    Log.Information($"Pobieranie zbiorczego UPO: {upo.ReferenceNumber}");
                    string upoContent = await ksefClient.GetSessionUpoAsync(referenceNumber, upo.ReferenceNumber, accessToken, cancellationToken).ConfigureAwait(false);
                    string upoPath = Path.Combine(UpoDir, $"uposesji-{upo.ReferenceNumber}.xml");
                    File.WriteAllText(upoPath, XDocument.Parse(upoContent).ToString() + "\n");
                    if (UpoPdf) {
                        Log.Information($"Generowanie PDF dla zbiorczego UPO: {upo.ReferenceNumber}");
                        byte[] pdfContent = await pdfRunner!.XML2PDF(upoContent, Quiet, true, null, null, null, cancellationToken).ConfigureAwait(false);
                        File.WriteAllBytes(Path.ChangeExtension(upoPath, ".pdf"), pdfContent);
                    }
                }
            }

            // Indywidualne UPO
            const int pageSize = 50;
            string? continuationtoken = null;
            do {
                SessionInvoicesResponse sessionInvoices = await ksefClient
                   .GetSessionInvoicesAsync(
                       referenceNumber,
                       accessToken,
                       pageSize,
                       continuationtoken,
                       cancellationToken).ConfigureAwait(false);

                foreach (SessionInvoice? invoice in sessionInvoices.Invoices.Where(i => i.KsefNumber is not null)) {
                    Log.Information($"Pobieranie indywidualnego UPO dla faktury: {invoice.KsefNumber}");
                    string upoContent = await ksefClient.GetSessionInvoiceUpoByKsefNumberAsync(referenceNumber, invoice.KsefNumber, accessToken, cancellationToken).ConfigureAwait(false);
                    string upoPath = Path.Combine(UpoDir, $"upo-{invoice.KsefNumber}.xml");
                    File.WriteAllText(upoPath, XDocument.Parse(upoContent).ToString() + "\n");
                    if (UpoPdf) {
                        Log.Information($"Generowanie PDF dla indywidualnego UPO: {invoice.KsefNumber}");
                        byte[] pdfContent = await pdfRunner!.XML2PDF(upoContent, Quiet, true, null, null, null, cancellationToken).ConfigureAwait(false);
                        File.WriteAllBytes(Path.ChangeExtension(upoPath, ".pdf"), pdfContent);
                    }
                }

                continuationtoken = sessionInvoices.ContinuationToken;
            } while (continuationtoken != null);
        }

        // Report what KSeF actually did with the invoices. UPO retrieval above still runs on a
        // partial failure, because the invoices that were accepted are filed and have one.
        UploadOutcome outcome = DetermineOutcome(sessionStatus);
        if (outcome.IsSuccess) {
            Log.Information(outcome.Summary);
        } else {
            Log.Error(outcome.Summary);
        }
        return outcome.ExitCode;
    }
}
