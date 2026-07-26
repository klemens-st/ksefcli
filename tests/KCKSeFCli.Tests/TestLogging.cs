using System.Runtime.CompilerServices;

namespace KCKSeFCli.Tests;

/// <summary>
/// Log.Logger is left null until a command calls ConfigureLogging, which Program does before
/// dispatch. Tests exercise the production types directly, so they have to do it themselves —
/// otherwise anything that logs throws ArgumentNullException on the logger.
///
/// Quiet keeps ordinary progress messages out of the test output; warnings and errors still
/// surface, which is what we want when a test is diagnosing one.
/// </summary>
internal static class TestLogging {
    [ModuleInitializer]
    internal static void Initialize() => Log.ConfigureLogging(quiet: true);
}
