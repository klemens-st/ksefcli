using System.Globalization;

using CommandLine;

using KSeF.Client.ClientFactory;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models.Authorization;
using KSeF.Client.DI;

using KCKSeFCli.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace KCKSeFCli;

public abstract class IWithConfigCommand : IGlobalCommand {
    [Option('c', "config", HelpText = "Path to config file")]
    public string ConfigFile { get; set; } = "";

    [Option('a', "active", HelpText = "Active profile name")]
    public string ActiveProfile { get; set; } = "";

    [Option("cache", HelpText = "Path to token cache file")]
    public string TokenCache { get; set; } = System.IO.Path.Combine(IGlobalCommand.CacheDir, "tokenstore.json");

    [Option("no-tokencache", HelpText = "Disable token cache usage")]
    public bool NoTokenCache { get; set; }

    [Option("environment", HelpText = "KSeF environment")]
    public string? CmdEnvironment { get; set; }

    [Option("token", HelpText = "Authentication token")]
    public string? CmdToken { get; set; }

    private readonly Lazy<ProfileConfigWithName> _cachedProfile;
    private readonly Lazy<TokenStore> _tokenStore;

    public IWithConfigCommand() {
        _cachedProfile = new Lazy<ProfileConfigWithName>(() => {
            if (!string.IsNullOrEmpty(CmdEnvironment) || !string.IsNullOrEmpty(CmdToken)) {
                // Resolve config from command line arguments
                if (ConfigFile != "" || ActiveProfile != "") {
                    throw new InvalidOperationException("Cannot use --config or --active with command-line profile options.");
                }
                if (string.IsNullOrEmpty(CmdEnvironment)) {
                    throw new InvalidOperationException("You have to use --environment is specifying authentication on command line with --token).");
                }
                if (string.IsNullOrEmpty(CmdToken)) {
                    throw new InvalidOperationException("You have to use --token is specifying authentication on command line with --environment.");
                }
                string nip = NipUtils.ExtractNipFromToken(CmdToken!);
                ProfileConfig profile = new ProfileConfig {
                    Environment = CmdEnvironment!,
                    Nip = nip,
                    Token = CmdToken!,
                };
                return new ProfileConfigWithName(profile, ".__cmd__");
            } else {
                // Resolve config from file
                string configFileDefault = Path.Combine(IGlobalCommand.ConfigDir, "kcksefcli.yaml");
                string? configEnv = System.Environment.GetEnvironmentVariable("KCKSEFCLI_CONFIG");
                string actualConfigFileToLoad = !string.IsNullOrEmpty(ConfigFile) ? ConfigFile : !string.IsNullOrEmpty(configEnv) ? configEnv : configFileDefault;

                string? profileEnv = System.Environment.GetEnvironmentVariable("KCKSEFCLI_ACTIVE");
                string actualActiveProfileToLoad = !string.IsNullOrEmpty(ActiveProfile) ? ActiveProfile : !string.IsNullOrEmpty(profileEnv) ? profileEnv : "";

                Log.Information($"Loading config from {actualConfigFileToLoad} with active={actualActiveProfileToLoad}");
                KCKSeFCliConfig config = ConfigLoader.Load(actualConfigFileToLoad, actualActiveProfileToLoad);
                ProfileConfig profile = config.Profiles[config.ActiveProfile];
                return new ProfileConfigWithName(profile, config.ActiveProfile);
            }
        });
        _tokenStore = new Lazy<TokenStore>(() => new TokenStore(TokenCache));
    }

    protected TokenStore GetTokenStore() => _tokenStore.Value;

    public ProfileConfigWithName Config() => _cachedProfile.Value;

    /// <summary>
    /// Guard for operations no later command can undo. Throws unless the active environment is
    /// a pre-production one, the operator passed --yes, or a human at a terminal says so.
    ///
    /// Decision logic and its rationale live in
    /// <see cref="KCKSeFCli.Utils.DangerousOperation"/>; this only carries it out.
    /// </summary>
    protected void RequireConfirmation(bool assumeYes, string operation) {
        string environment = Config().Environment;
        ConfirmationRequirement requirement = DangerousOperation.Evaluate(
            environment, assumeYes, interactive: !Console.IsInputRedirected);

        switch (requirement) {
            case ConfirmationRequirement.NotRequired:
            case ConfirmationRequirement.SatisfiedByFlag:
                return;

            case ConfirmationRequirement.Prompt:
                Console.Error.Write(
                    $"UWAGA: {operation} w środowisku produkcyjnym ({environment}). "
                    + "Tej operacji nie da się cofnąć. Kontynuować? [t/N] ");
                if (DangerousOperation.IsAffirmative(Console.ReadLine())) {
                    return;
                }
                throw new OperationRefusedException($"Anulowano: {operation}.");

            default:
                throw new OperationRefusedException(
                    $"Odmowa: {operation} w środowisku produkcyjnym ({environment}) bez terminala. "
                    + "Uruchom polecenie interaktywnie albo podaj --yes, jeśli świadomie "
                    + "autoryzujesz tę operację. Zobacz docs/BezpieczenstwoAgentow.md.");
        }
    }

    public TokenStore.Key GetTokenStoreKey() {
        ProfileConfigWithName config = Config();
        return new TokenStore.Key(config.Name, config);
    }

    public override async Task<int> ExecuteAsync(CancellationToken cancellationToken) {
        using IServiceScope scope = GetScope();
        return await ExecuteInScopeAsync(scope, cancellationToken).ConfigureAwait(false);
    }

    public abstract Task<int> ExecuteInScopeAsync(IServiceScope scope, CancellationToken cancellationToken);

    public static string Dtisoformat(DateTimeOffset dt) {
        return dt.ToString("yyyy-MM-ddTHH\\:mm\\:ss.fffffffzzz", CultureInfo.InvariantCulture);
    }

    public async Task<AuthenticationOperationStatusResponse> Auth(IServiceScope scope, CancellationToken cancellationToken) {
        ProfileConfigWithName config = Config();
        AuthenticationOperationStatusResponse response = config.AuthMethod switch {
            AuthMethod.KsefToken => await Authenticate.TokenAuth(config, scope, GetCryptographicService, cancellationToken).ConfigureAwait(false),
            AuthMethod.Xades => await Authenticate.CertAuth(config, scope, GetCryptographicService, cancellationToken).ConfigureAwait(false),
            _ => throw new Exception($"Invalid authmethod in profile: {config.Environment}")
        };
        Log.Information($"Acquired accessToken until {Dtisoformat(response.AccessToken.ValidUntil)}, refreshToken until {Dtisoformat(response.RefreshToken.ValidUntil)}");
        return response;
    }

    public async Task<string> GetAccessToken(IServiceScope scope, CancellationToken cancellationToken) {
        if (NoTokenCache) {
            Log.Information("Token cache disabled, starting new auth");
            AuthenticationOperationStatusResponse response = await Auth(scope, cancellationToken).ConfigureAwait(false);
            return response.AccessToken.Token;
        }

        TokenStore tokenStore = GetTokenStore();
        TokenStore.Key key = GetTokenStoreKey();
        TokenStore.Data? storedToken = tokenStore.GetToken(key);

        if (storedToken == null) {
            Log.Information("No token found in store, starting new auth");
        } else {
            Log.Information($"Stored accessToken until {Dtisoformat(storedToken.Response.AccessToken.ValidUntil)}, refreshToken until {Dtisoformat(storedToken.Response.RefreshToken.ValidUntil)}");
        }
        if (storedToken == null || storedToken.Response.RefreshToken.ValidUntil < DateTimeOffset.UtcNow.AddMinutes(-1)) {
            Log.Information("Stored refresh token is nearing expiration, refreshing token");
            AuthenticationOperationStatusResponse response = await Auth(scope, cancellationToken).ConfigureAwait(false);
            tokenStore.SetToken(key, new TokenStore.Data(response));
            return response.AccessToken.Token;
        }
        if (storedToken.Response.AccessToken.ValidUntil < DateTimeOffset.UtcNow.AddMinutes(-1)) {
            Log.Information("Stored access token is nearing expiration, refreshing token");
            AuthenticationOperationStatusResponse response = await TokenRefresh(scope, storedToken.Response.RefreshToken, cancellationToken).ConfigureAwait(false);
            Log.Information($"Acquired accessToken until {Dtisoformat(response.AccessToken.ValidUntil)}, refreshToken until {Dtisoformat(response.RefreshToken.ValidUntil)}");
            tokenStore.SetToken(key, new TokenStore.Data(response));
            return response.AccessToken.Token;
        }
        return storedToken.Response.AccessToken.Token;
    }

    public async Task<AuthenticationOperationStatusResponse> TokenRefresh(IServiceScope scope, TokenInfo refreshToken, CancellationToken cancellationToken) {
        IKSeFClient ksefClient = scope.ServiceProvider.GetRequiredService<IKSeFClient>();
        RefreshTokenResponse response = await ksefClient.RefreshAccessTokenAsync(refreshToken.Token, cancellationToken).ConfigureAwait(false);
        return new AuthenticationOperationStatusResponse {
            AccessToken = response.AccessToken,
            RefreshToken = refreshToken,
        };
    }

    private IServiceScope GetScope() {
        ProfileConfigWithName config = Config();
        IServiceCollection services = new ServiceCollection();
        KSeF.Client.ClientFactory.Environment environment = config.Environment.ToUpper() switch {
            "PROD" => KSeF.Client.ClientFactory.Environment.Prod,
            "DEMO" => KSeF.Client.ClientFactory.Environment.Demo,
            "TEST" => KSeF.Client.ClientFactory.Environment.Test,
            _ => throw new Exception($"Invalid environment in profile: {config.Environment}")
        };
        services.AddSingleton<ProfileConfig>((ProfileConfig)config);
        services.AddKSeFClient(options => {
            options.BaseUrl = KsefEnvironmentConfig.BaseUrls[environment];
        });
        ServiceCollectionExtensions.AddCryptographyClient(services);
        ServiceProvider provider = services.BuildServiceProvider();
        IServiceScope scope = provider.CreateScope();
        return scope;
    }

    public async Task<ICryptographyService> GetCryptographicService(IServiceScope scope, CancellationToken cancellationToken) {
        ICryptographyService cryptographyService = scope.ServiceProvider.GetRequiredService<ICryptographyService>();
        await cryptographyService.WarmupAsync(cancellationToken).ConfigureAwait(false);
        return cryptographyService;
    }
}

