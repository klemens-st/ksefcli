using CommandLine;
using CommandLine.Text;

namespace KCKSeFCli;

public class Program {
    public static async Task<int> Main(string[] args) {
        // https://github.com/commandlineparser/commandline/wiki/How-To
        StringWriter helpWriter = new StringWriter();
        Parser parser = new Parser(with => {
            with.HelpWriter = helpWriter;
            with.EnableDashDash = true;
        });

        Type[] commandTypes = new[]
        {
            typeof(TestAuthCommand),
            typeof(TestCertAuthCommand),
            typeof(TestSkiaSharpCommand),
            typeof(TestTokenAuthCommand),
            typeof(CheckAuthNipCommand),
            typeof(DodajPozycjeNaFakturzeCommand),
            typeof(GetFakturaCommand),
            typeof(LinkDoFakturyCommand),
            typeof(LinkWeryfikacjiFaktury),
            typeof(NowyCertyfikatCommand),
            typeof(NowaFakturaCommand),
            typeof(ParseDateCommand),
            typeof(PobierzCertyfikatCommand),
            typeof(PobierzInfoONipCommand),
            typeof(PobierzFakturyCommand),
            typeof(PokazLimityCommand),
            typeof(PrintConfigCommand),
            typeof(PrzeslijFakturyCommand),
            typeof(QRDoFakturyCommand),
            typeof(QRWeryfikacjiFakturyCommand),
            typeof(SprawdzLimitCertyfikatowCommand),
            typeof(SzukajFakturCommand),
            typeof(TestTokenAuthCommand),
            typeof(TestTokenRefreshCommand),
            typeof(UniewaznijCertyfikatCommand),
            typeof(XMLRemoveNamespaceCommand),
            typeof(WeryfikujXMLCommand),
            typeof(WylistujCertyfikatyCommand),
            typeof(WystawFaktureOfflineCommand),
            typeof(WystawPodobnaFaktureCommand),
            typeof(WystawKorekteCommand),
            typeof(XMLExtractCommand),
            typeof(XML2PDFCommand)
        }.OrderBy(t => ((VerbAttribute)t.GetCustomAttributes(typeof(VerbAttribute), true)[0]).Name).ToArray();

        ParserResult<object> result = parser.ParseArguments(args, commandTypes);


        CancellationTokenSource cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => {
            Console.WriteLine("Canceling...");
            cts.Cancel();
            e.Cancel = true;
        };

        try {
            return await result.MapResult(
                (IGlobalCommand cmd) => {
                    cmd.ConfigureLogging();
                    return cmd.ExecuteAsync(cts.Token);
                },
                errs => {
                    HelpText helpText = HelpText.AutoBuild(result, h => {
                        h.Copyright = "Copyright (C) 2026 Kamil Cukrowski. Source code lisenced under GPLv3.";
                        // new CopyrightInfo("Kamil Cukrowski", 2026);
                        h.AdditionalNewLineAfterOption = false;
                        return h;
                    });
                    Console.WriteLine(helpText);

                    if (errs.Any(e => e is HelpRequestedError or HelpVerbRequestedError or VersionRequestedError)) {
                        return Task.FromResult(0);
                    }

                    return Task.FromResult(1);
                }
            ).ConfigureAwait(false);
        } catch (KCKSeFCli.Utils.OperationRefusedException ex) {
            // A declined or unauthorised operation is an ordinary failure, not a crash. Exit 3
            // is reserved for unhandled exceptions, and a stack trace would bury the message.
            Console.Error.WriteLine(ex.Message);
            return 1;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.StackTrace);
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
    }
}
