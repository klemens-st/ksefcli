// Bounds the blast radius of an agent driving this CLI.
//
// The Phase 3 fixes reduced the chance of a mistake; this decides what a mistake can reach.
// Three verbs do something a later command cannot undo: PrzeslijFaktury files invoices with
// the tax authority, UniewaznijCertyfikat revokes a certificate, NowyCertyfikat consumes a
// limited enrolment quota.
//
// The protection that matters is the default in the headless case. An agent runs with no
// terminal, so it cannot answer a prompt; if the answer to "no terminal" were "go ahead", the
// gate would be decorative. So production plus non-interactive plus no explicit --yes is a
// refusal, and the operator has to opt in per invocation.
//
// Unknown environment names are treated as production on purpose, matching
// ProfileConfig.VerifyCertificateChain: a typo in the profile must fail closed.
using KCKSeFCli.Utils;

using Xunit;

namespace KCKSeFCli.Tests;

public class DangerousOperationTests {
    [Theory]
    [InlineData("test")]
    [InlineData("TEST")]
    [InlineData("Test")]
    [InlineData("demo")]
    [InlineData("DEMO")]
    public void NonProductionEnvironmentsNeedNoConfirmation(string environment) {
        // Agents are meant to live here, so the gate must not make the safe path annoying.
        Assert.Equal(
            ConfirmationRequirement.NotRequired,
            DangerousOperation.Evaluate(environment, assumeYes: false, interactive: false));
    }

    [Theory]
    [InlineData("prod")]
    [InlineData("PROD")]
    [InlineData("Prod")]
    public void ProductionWithoutATerminalOrFlagIsRefused(string environment) {
        Assert.Equal(
            ConfirmationRequirement.Refuse,
            DangerousOperation.Evaluate(environment, assumeYes: false, interactive: false));
    }

    [Fact]
    public void ProductionPromptsWhenAHumanIsPresent() {
        Assert.Equal(
            ConfirmationRequirement.Prompt,
            DangerousOperation.Evaluate("prod", assumeYes: false, interactive: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitYesSatisfiesProductionWithOrWithoutATerminal(bool interactive) {
        Assert.Equal(
            ConfirmationRequirement.SatisfiedByFlag,
            DangerousOperation.Evaluate("prod", assumeYes: true, interactive: interactive));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("prodd")]
    [InlineData("produkcja")]
    [InlineData("tset")]
    [InlineData(" test")]
    public void UnrecognisedEnvironmentsAreTreatedAsProduction(string? environment) {
        // A profile typo must not silently downgrade the gate.
        Assert.Equal(
            ConfirmationRequirement.Refuse,
            DangerousOperation.Evaluate(environment, assumeYes: false, interactive: false));
    }

    [Fact]
    public void IsProductionAgreesWithTheGate() {
        Assert.False(DangerousOperation.IsProduction("test"));
        Assert.False(DangerousOperation.IsProduction("demo"));
        Assert.True(DangerousOperation.IsProduction("prod"));
        Assert.True(DangerousOperation.IsProduction("anything-else"));
    }

    [Theory]
    [InlineData("t")]
    [InlineData("T")]
    [InlineData("tak")]
    [InlineData("TAK")]
    [InlineData("y")]
    [InlineData("yes")]
    [InlineData(" tak ")]
    public void AffirmativeAnswersAreAccepted(string answer) {
        Assert.True(DangerousOperation.IsAffirmative(answer));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("n")]
    [InlineData("nie")]
    [InlineData("no")]
    [InlineData("cokolwiek")]
    public void EverythingElseIsARefusal(string? answer) {
        // Enter on its own must mean no, not yes.
        Assert.False(DangerousOperation.IsAffirmative(answer));
    }
}
