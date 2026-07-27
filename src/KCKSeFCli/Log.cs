using Microsoft.Extensions.Logging;

public static class Log {
    private static ILoggerFactory? _loggerFactory;

    public static ILogger Logger { get; private set; } = default!;

    public static void ConfigureLogging(bool verbose = false, bool quiet = false) {
        // Polecenia bywają konfigurowane dwa razy (Program, a potem samo polecenie). Bez tego
        // pierwsza fabryka zostawała osierocona razem ze swoją kolejką i nikt jej już nie
        // opróżniał.
        Flush();

        _loggerFactory = LoggerFactory.Create(builder => {
            LogLevel kcksefCliLevel = LogLevel.Information;
            LogLevel microsoftLevel = LogLevel.Warning;
            LogLevel systemLevel = LogLevel.Warning;

            if (verbose) {
                kcksefCliLevel = LogLevel.Debug;
                microsoftLevel = LogLevel.Debug;
                systemLevel = LogLevel.Debug;
            }

            if (quiet) {
                kcksefCliLevel = LogLevel.Warning;
            }

            builder.AddFilter("KCKSeFCli", kcksefCliLevel)
                   .AddFilter("Microsoft", microsoftLevel)
                   .AddFilter("System", systemLevel)
                   .AddConsole(options => {
                       options.LogToStandardErrorThreshold = LogLevel.Trace;
                   })
                   .AddSimpleConsole(options => {
                       options.SingleLine = true;
                       options.TimestampFormat = "HH:mm:ss ";
                   });
        });

        Logger = _loggerFactory.CreateLogger("KCKSeFCli");
    }

    /// <summary>
    /// Domyka fabrykę logowania, czekając aż wątek pisarza opróżni kolejkę.
    ///
    /// AddConsole kolejkuje komunikaty do wątku w tle. Fabryka trzymana w statycznym polu i
    /// nigdy niedomykana oznaczała, że wszystko, co zostało w kolejce w chwili wyjścia z
    /// procesu, było po prostu porzucane — CLI kończyło się nie wypisawszy niczego. Awaria
    /// całkowita: żadnego komunikatu o przyczynie, tylko kod wyjścia. Dla agenta, który nie ma
    /// na czym oprzeć decyzji, naturalną reakcją jest ponowienie — a to najgorsza możliwa
    /// odpowiedź dla polecenia wysyłającego faktury.
    ///
    /// Wywoływane z Program.Main na każdej ścieżce wyjścia i przy ponownej konfiguracji.
    /// </summary>
    public static void Flush() {
        ILoggerFactory? factory = _loggerFactory;
        _loggerFactory = null;
        factory?.Dispose();
    }

    public static void Trace(string message) => Logger.LogTrace(message);
    public static void Debug(string message) => Logger.LogDebug(message);
    public static void Information(string message) => Logger.LogInformation(message);
    public static void Warning(string message) => Logger.LogWarning(message);
    public static void Error(string message) => Logger.LogError(message);
    public static void Critical(string message) => Logger.LogCritical(message);
}
