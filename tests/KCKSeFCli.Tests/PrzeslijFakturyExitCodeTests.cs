using KSeF.Client.Core.Models;
using KSeF.Client.Core.Models.ApiResponses;
using KSeF.Client.Core.Models.Sessions;

using Xunit;

namespace KCKSeFCli.Tests;

/// <summary>
/// Regression tests for defect #1: PrzeslijFaktury used to poll until SuccessfulInvoiceCount
/// became non-null and then unconditionally `return 0`, never once reading
/// FailedInvoiceCount — which was referenced nowhere in the codebase.
///
/// An upload where KSeF rejected every invoice therefore exited 0. That is the failure an
/// agent genuinely cannot detect: nothing in the exit status distinguishes "10 invoices filed"
/// from "10 invoices bounced", so a workflow marks the batch done and moves on.
///
/// The counts are also not always populated. When the whole batch is rejected before
/// per-invoice processing (code 445, "brak poprawnych faktur"), the session can settle with
/// both counts at zero or null, so "counts are present" is not a sound completion signal
/// either — hence IsTerminal keying off the status code instead.
/// </summary>
public class PrzeslijFakturyExitCodeTests {
    private static SessionStatusResponse Status(
        int code, int? invoices = null, int? successful = null, int? failed = null) =>
        new() {
            Status = new OperationStatusInfo { Code = code, Description = $"code {code}" },
            InvoiceCount = invoices,
            SuccessfulInvoiceCount = successful,
            FailedInvoiceCount = failed,
        };

    // ---- completion detection -------------------------------------------------------------

    [Theory]
    [InlineData(BatchSessionCodeResponse.SessionStarted)]
    [InlineData(BatchSessionCodeResponse.Processing)]
    public void SessionStillRunningIsNotTerminal(int code) {
        Assert.False(PrzeslijFakturyCommand.IsTerminal(Status(code)));
    }

    [Theory]
    [InlineData(BatchSessionCodeResponse.ProcessedSuccessfully)]
    [InlineData(BatchSessionCodeResponse.ValidationError)]
    [InlineData(BatchSessionCodeResponse.NoValidInvoices)]
    [InlineData(BatchSessionCodeResponse.InvoiceLimitExceeded)]
    [InlineData(BatchSessionCodeResponse.SessionTimeoutCancelled)]
    [InlineData(BatchSessionCodeResponse.UnknownError)]
    public void SettledSessionIsTerminal(int code) {
        Assert.True(PrzeslijFakturyCommand.IsTerminal(Status(code)));
    }

    [Fact]
    public void WholesaleRejectionIsTerminalEvenWithNoCounts() {
        // The old predicate waited for SuccessfulInvoiceCount to appear, so this shape polled
        // until it ran out of attempts instead of reporting the rejection.
        Assert.True(PrzeslijFakturyCommand.IsTerminal(Status(BatchSessionCodeResponse.NoValidInvoices)));
    }

    [Fact]
    public void MissingStatusIsNotTerminal() {
        Assert.False(PrzeslijFakturyCommand.IsTerminal(null));
        Assert.False(PrzeslijFakturyCommand.IsTerminal(new SessionStatusResponse()));
    }

    // ---- exit code ------------------------------------------------------------------------

    [Fact]
    public void EveryInvoiceAcceptedExitsZero() {
        PrzeslijFakturyCommand.UploadOutcome outcome = PrzeslijFakturyCommand.DetermineOutcome(
            Status(BatchSessionCodeResponse.ProcessedSuccessfully, invoices: 5, successful: 5, failed: 0));

        Assert.Equal(PrzeslijFakturyCommand.ExitAccepted, outcome.ExitCode);
        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public void EveryInvoiceRejectedExitsNonZero() {
        // The headline case. Before the fix this returned 0.
        PrzeslijFakturyCommand.UploadOutcome outcome = PrzeslijFakturyCommand.DetermineOutcome(
            Status(BatchSessionCodeResponse.NoValidInvoices, invoices: 5, successful: 0, failed: 5));

        Assert.Equal(PrzeslijFakturyCommand.ExitRejected, outcome.ExitCode);
        Assert.False(outcome.IsSuccess);
    }

    [Fact]
    public void PartialSuccessGetsItsOwnExitCode() {
        // Distinct from a total rejection: some invoices are filed and must not be re-sent,
        // so a caller retrying blindly would create duplicates.
        PrzeslijFakturyCommand.UploadOutcome outcome = PrzeslijFakturyCommand.DetermineOutcome(
            Status(BatchSessionCodeResponse.ProcessedSuccessfully, invoices: 5, successful: 3, failed: 2));

        Assert.Equal(PrzeslijFakturyCommand.ExitPartiallyAccepted, outcome.ExitCode);
        Assert.False(outcome.IsSuccess);
        Assert.NotEqual(PrzeslijFakturyCommand.ExitRejected, outcome.ExitCode);
    }

    [Fact]
    public void UnpopulatedCountsAreNeverTreatedAsSuccess() {
        // A 200 with no counts at all proves nothing was accepted. Silently exiting 0 here is
        // exactly how the original bug slipped through.
        PrzeslijFakturyCommand.UploadOutcome outcome = PrzeslijFakturyCommand.DetermineOutcome(
            Status(BatchSessionCodeResponse.ProcessedSuccessfully));

        Assert.NotEqual(PrzeslijFakturyCommand.ExitAccepted, outcome.ExitCode);
    }

    [Fact]
    public void UnaccountedInvoicesAreNotSuccess() {
        // 5 declared, 3 accepted, 0 explicitly failed: the missing 2 must not round up to OK.
        PrzeslijFakturyCommand.UploadOutcome outcome = PrzeslijFakturyCommand.DetermineOutcome(
            Status(BatchSessionCodeResponse.ProcessedSuccessfully, invoices: 5, successful: 3, failed: 0));

        Assert.Equal(PrzeslijFakturyCommand.ExitPartiallyAccepted, outcome.ExitCode);
    }

    [Fact]
    public void SessionLevelErrorExitsNonZeroEvenWithoutFailedCount() {
        // Batch bounced at the session level (bad key, bad archive, over the limit). Counts
        // stay empty because KSeF never got as far as individual invoices.
        foreach (int code in new[] {
            BatchSessionCodeResponse.ValidationError,
            BatchSessionCodeResponse.KeyDecryptionError,
            BatchSessionCodeResponse.InvoiceLimitExceeded,
            BatchSessionCodeResponse.ArchiveDecompressionError,
            BatchSessionCodeResponse.SessionTimeoutCancelled,
            BatchSessionCodeResponse.UnknownError,
        }) {
            PrzeslijFakturyCommand.UploadOutcome outcome =
                PrzeslijFakturyCommand.DetermineOutcome(Status(code));

            Assert.Equal(PrzeslijFakturyCommand.ExitRejected, outcome.ExitCode);
        }
    }

    [Fact]
    public void MissingStatusExitsNonZero() {
        Assert.Equal(PrzeslijFakturyCommand.ExitRejected,
                     PrzeslijFakturyCommand.DetermineOutcome(null).ExitCode);
        Assert.Equal(PrzeslijFakturyCommand.ExitRejected,
                     PrzeslijFakturyCommand.DetermineOutcome(new SessionStatusResponse()).ExitCode);
    }

    [Fact]
    public void StillProcessingExitsNonZero() {
        // Reached only if polling gave up; a session that never settled is not a success.
        PrzeslijFakturyCommand.UploadOutcome outcome = PrzeslijFakturyCommand.DetermineOutcome(
            Status(BatchSessionCodeResponse.Processing, invoices: 5, successful: 5, failed: 0));

        Assert.Equal(PrzeslijFakturyCommand.ExitRejected, outcome.ExitCode);
    }

    [Fact]
    public void SummaryNamesTheCountsSoTheFailureIsLegible() {
        PrzeslijFakturyCommand.UploadOutcome outcome = PrzeslijFakturyCommand.DetermineOutcome(
            Status(BatchSessionCodeResponse.ProcessedSuccessfully, invoices: 7, successful: 4, failed: 3));

        Assert.Contains("4", outcome.Summary);
        Assert.Contains("3", outcome.Summary);
        Assert.Contains("7", outcome.Summary);
    }
}
