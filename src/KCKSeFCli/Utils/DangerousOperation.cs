#nullable enable
namespace KCKSeFCli.Utils;

/// <summary>
/// Thrown when a dangerous operation is not authorised. Distinct from a crash: Program.cs
/// reports it as an ordinary failure (exit 1) with the message alone, because exit 3 means
/// "unhandled exception" and a stack trace buries the one line the operator needs.
/// </summary>
public class OperationRefusedException : Exception {
    public OperationRefusedException(string message) : base(message) { }
}

/// <summary>Wynik oceny, czy operacja nieodwracalna wymaga potwierdzenia.</summary>
public enum ConfirmationRequirement {
    /// <summary>Środowisko nieprodukcyjne - można wykonać bez pytania.</summary>
    NotRequired,

    /// <summary>Produkcja, ale operator podał --yes.</summary>
    SatisfiedByFlag,

    /// <summary>Produkcja i jest terminal - zapytaj człowieka.</summary>
    Prompt,

    /// <summary>Produkcja, brak terminala i brak --yes - odmów.</summary>
    Refuse,
}

/// <summary>
/// Gate for the operations no later command can undo: filing invoices with KSeF, revoking a
/// certificate, consuming the certificate enrolment quota.
///
/// The rule that does the work is the headless default. An agent has no terminal, so it cannot
/// answer a prompt; treating "no terminal" as consent would make this decorative. Production
/// plus non-interactive plus no explicit --yes is therefore a refusal.
///
/// See docs/BezpieczenstwoAgentow.md.
/// </summary>
public static class DangerousOperation {
    /// <summary>
    /// Only the two KSeF pre-production environments are treated as safe. Anything else,
    /// including an empty or misspelled name, counts as production, so a profile typo fails
    /// closed rather than quietly disabling the gate. Same reasoning as
    /// <see cref="ProfileConfig.VerifyCertificateChain"/>.
    /// </summary>
    public static bool IsProduction(string? environment) =>
        !(string.Equals(environment, "test", StringComparison.OrdinalIgnoreCase)
          || string.Equals(environment, "demo", StringComparison.OrdinalIgnoreCase));

    public static ConfirmationRequirement Evaluate(string? environment, bool assumeYes, bool interactive) {
        if (!IsProduction(environment)) {
            return ConfirmationRequirement.NotRequired;
        }
        if (assumeYes) {
            return ConfirmationRequirement.SatisfiedByFlag;
        }
        return interactive ? ConfirmationRequirement.Prompt : ConfirmationRequirement.Refuse;
    }

    /// <summary>
    /// A bare Enter means no. Accepts Polish and English affirmatives, since the prompt is
    /// Polish but the habit of typing "y" is not.
    /// </summary>
    public static bool IsAffirmative(string? answer) {
        string trimmed = (answer ?? "").Trim();
        return string.Equals(trimmed, "t", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "tak", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
