using KCKSeFCli;

using Xunit;

namespace KCKSeFCli.Tests;

/// <summary>
/// Smoke tests proving the test project is wired to the application assembly.
/// The substantive coverage lives in the per-defect regression suites.
/// </summary>
public class InfrastructureTests {
    [Fact]
    public void ApplicationTypesAreReachableFromTests() {
        // NipUtils is pure logic with no DI or network, so it is a safe canary for the
        // ProjectReference resolving correctly.
        Assert.Equal("5252611332", NipUtils.NormalizeNip("525-261-13-32"));
    }

    [Theory]
    [InlineData("5252611332")]
    [InlineData("525-261-13-32")]
    public void ValidNipsPassChecksumValidation(string nip) {
        NipUtils.AssertNipIsValid(nip);
    }

    [Theory]
    [InlineData("1234567890")]   // checksum digit does not match
    [InlineData("52526113")]     // too short
    [InlineData("")]
    public void InvalidNipsAreRejected(string nip) {
        Assert.ThrowsAny<Exception>(() => NipUtils.AssertNipIsValid(nip));
    }
}
