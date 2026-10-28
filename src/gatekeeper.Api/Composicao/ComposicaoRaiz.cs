using gatekeeper.Persistence.Adapters;
using gatekeeper.Api;
using gatekeeper.Messaging.Adapters;
using gatekeeper.Health;
using gatekeeper.Messaging;
using gatekeeper.Files;
using gatekeeper.Files.Adapters;
using gatekeeper.Caching.Adapters;
using gatekeeper.Infrastructure;
using gatekeeper.Observability.Adapters;
using gatekeeper.Audit;
using gatekeeper.Audit.Adapters;
using gatekeeper.Governance;
using gatekeeper.Governance.Http;
using gatekeeper.Identity;
using gatekeeper.Identity.Http;
using gatekeeper.Authorization;
using gatekeeper.Authorization.Adapters;
using gatekeeper.Authorization.Http;
using gatekeeper.Billing;
using gatekeeper.Billing.Http;
using gatekeeper.Identity.Adapters;
using gatekeeper.Observability;
using gatekeeper.Health.Adapters;
using gatekeeper.Workflows.Adapters;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace gatekeeper.Api;

/// <summary>
/// Unico lugar que amarra porta e adaptador. Controller nao registra dependencia.
/// O repositorio SQL nao conhece o driver: o provedor e registrado aqui.
/// </summary>
public static class ComposicaoRaiz
{
    public static IServiceCollection AddEsqueleto(this IServiceCollection services, IConfiguration configuracao)
    {
        var saude = services.AddHealthChecks();
        saude.AddCheck("processo", () => HealthCheckResult.Healthy("ok"), tags: new[] { "live" });
        saude.AddCheck("identity", () => HealthCheckResult.Healthy(string.IsNullOrWhiteSpace(configuracao["Identity:Issuer"]) ? "local" : "federated"), tags: new[] { "ready" });
        saude.AddCheck("authorization", () => HealthCheckResult.Healthy(string.IsNullOrWhiteSpace(configuracao["Authorization:Endpoint"]) ? "memory" : "cerbos"), tags: new[] { "ready" });
        saude.AddCheck("audit", () => HealthCheckResult.Healthy(DestinoDaTrilha(configuracao)), tags: new[] { "ready" });
        saude.AddCheck("billing", () => HealthCheckResult.Healthy(string.IsNullOrWhiteSpace(configuracao["Database:ConnectionString"]) ? "memory" : "sql"), tags: new[] { "ready" });
        AdicionarBanco(services, configuracao);
        AdicionarArquivos(services, configuracao);
        AdicionarCache(services, configuracao);
        AdicionarMensagens(services, configuracao);
        AdicionarWorkflow(services, configuracao);
        foreach (var nome in NomesDeSonda.Todos)
        {
            RegistrarSonda(saude, nome);
        }

        services.AddControllers();
        GarantirBorda(services, configuracao);
        services.AddSingleton<IModuloDeRotas, ModuloDeRotasDeSaude>();
        services.AddSingleton<IModuloDeRotas, BillingRoutes>();
        services.AddSingleton<IModuloDeRotas, OfficeRoutes>();
        services.AddSingleton<IModuloDeRotas, IngestRoutes>();
        services.AddSingleton<IModuloDeRotas, FederationRoutes>();
        services.AddSingleton<IModuloDeRotas, AccountRoutes>();
        services.AddSingleton<IModuloDeRotas, PolicyRoutes>();
        services.AddSingleton<IBillingStore, InMemoryBillingStore>();
        if (!string.IsNullOrWhiteSpace(configuracao["Database:ConnectionString"]))
        {
            services.AddSingleton<IBillingStore, SqlBillingStore>();
        }
        services.AddSingleton<BillingCatalog>();
        services.AddSingleton<InMemoryOfficeStore>();
        services.AddSingleton<IOfficeStore>(provedor => provedor.GetRequiredService<InMemoryOfficeStore>());
        services.AddSingleton<IMachineKeyGate>(provedor => provedor.GetRequiredService<InMemoryOfficeStore>());
        if (!string.IsNullOrWhiteSpace(configuracao["Database:ConnectionString"]))
        {
            services.AddSingleton<SqlOfficeStore>();
            services.AddSingleton<IOfficeStore>(provedor => provedor.GetRequiredService<SqlOfficeStore>());
            services.AddSingleton<IMachineKeyGate>(provedor => provedor.GetRequiredService<SqlOfficeStore>());
        }
        services.AddHostedService<DespachanteDeCaixa>();
        services.AddSingleton<ITelemetry, TelemetriaDeServico>();
        services.AddSingleton<IAccessLogPublisher, PublicadorDeLog>();
        services.AddSingleton<IAccessLogStore, LogsNoLago>();
        services.AddSingleton<IngestCatalog>();
        services.AddSingleton<IAuditPublisher, OutboxAuditPublisher>();
        services.AddSingleton<IAuditTrail, TrilhaNoLago>();
        services.AddSingleton(provedor => new OfficeDependencies(
            provedor.GetRequiredService<IOfficeStore>(),
            provedor.GetRequiredService<IBillingStore>(),
            provedor.GetRequiredService<IAuditTrail>()));
        services.AddSingleton<OfficeCatalog>();
        AdicionarTelemetria(services, configuracao);
        return services;
    }

    private static void AdicionarTelemetria(IServiceCollection services, IConfiguration configuracao)
    {
        var endpoint = configuracao["Observability:OtlpEndpoint"];
        var exportar = !string.IsNullOrWhiteSpace(endpoint);
        services.AddOpenTelemetry()
            .ConfigureResource(recurso => recurso.AddService("gatekeeper"))
            .WithTracing(rastreio =>
            {
                rastreio.AddSource(TelemetryCatalog.Fonte);
                rastreio.AddAspNetCoreInstrumentation(opcoes =>
                {
                    opcoes.EnrichWithHttpResponse = (atividade, _) => ProcessadorDeRedacao.Aplicar(atividade);
                });
                rastreio.AddHttpClientInstrumentation();
                rastreio.AddProcessor(new ProcessadorDeRedacao());
                if (exportar)
                {
                    rastreio.AddOtlpExporter(opcoes => opcoes.Endpoint = new Uri(endpoint!));
                }
            })
            .WithMetrics(medidas =>
            {
                medidas.AddMeter(TelemetryCatalog.Medidor);
                medidas.AddAspNetCoreInstrumentation();
                medidas.AddHttpClientInstrumentation();
                if (exportar)
                {
                    medidas.AddOtlpExporter(opcoes => opcoes.Endpoint = new Uri(endpoint!));
                }
            })
            .WithLogging(
                null,
                opcoes =>
                {
                    opcoes.IncludeScopes = true;
                    opcoes.IncludeFormattedMessage = true;
                    opcoes.AddProcessor(new ProcessadorDeLog());
                    if (exportar)
                    {
                        opcoes.AddOtlpExporter(exportacao => exportacao.Endpoint = new Uri(endpoint!));
                    }
                });
    }

    private static string DestinoDaTrilha(IConfiguration configuracao)
    {
        var bucket = configuracao["Files:Bucket"];
        var acesso = configuracao["Files:AccessKey"];
        var segredo = configuracao["Files:SecretKey"];
        if (string.IsNullOrWhiteSpace(bucket) || string.IsNullOrWhiteSpace(acesso) || string.IsNullOrWhiteSpace(segredo))
        {
            return "memory";
        }

        var provedor = (configuracao["Files:Provider"] ?? string.Empty).Trim();
        if (provedor.Length == 0 || string.Equals(provedor, "wasabi", StringComparison.OrdinalIgnoreCase))
        {
            return "wasabi";
        }

        return "s3";
    }

    private static void AdicionarBanco(IServiceCollection services, IConfiguration configuracao)
    {
        var connectionString = configuracao["Database:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddInfraestruturaEmMemoria();
            return;
        }

        var invariante = ProvedoresConhecidos.Invariante(configuracao["Database:Provider"] ?? string.Empty);
        RegistroDeProvedores.Registrar(invariante);
        services.AddRepositorioSql(new OpcoesDeBanco(invariante, connectionString));
    }

    private static void AdicionarArquivos(IServiceCollection services, IConfiguration configuracao)
    {
        var accessKey = configuracao["Files:AccessKey"];
        var secretKey = configuracao["Files:SecretKey"];
        var bucket = configuracao["Files:Bucket"];
        if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey) || string.IsNullOrWhiteSpace(bucket))
        {
            services.AddInMemoryFileStore();
            return;
        }

        var opcoes = OpcoesDeArquivo.Montar(
            configuracao["Files:Provider"] ?? string.Empty,
            configuracao["Files:Endpoint"],
            configuracao["Files:Region"],
            bucket,
            accessKey,
            secretKey,
            LerBool(configuracao, "Files:ForcePathStyle"));
        services.AddS3FileStore(opcoes);
    }

    private static void AdicionarCache(IServiceCollection services, IConfiguration configuracao)
    {
        var connectionString = configuracao["Caching:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddInMemoryCache();
            return;
        }

        var provedor = ProvedoresDeCache.Canonico(configuracao["Caching:Provider"] ?? string.Empty);
        if (provedor != ProvedoresDeCache.Redis && provedor != ProvedoresDeCache.Valkey)
        {
            throw new InvalidOperationException("Cache '" + provedor + "' nao e Redis nem Valkey.");
        }

        var prefixo = (configuracao["Caching:Prefix"] ?? string.Empty).Trim();
        if (prefixo.Length == 0 && string.Equals(configuracao["Caching:IsolarHospedeiro"], "true", StringComparison.OrdinalIgnoreCase))
        {
            prefixo = Guid.NewGuid().ToString("N");
        }

        services.AddValkeyCache(connectionString, prefixo);
    }

    private static void AdicionarMensagens(IServiceCollection services, IConfiguration configuracao)
    {
        AdicionarMensagem(services, configuracao, MessageRoles.Interno, NomesDeSonda.MensagensInterno, "Messaging:Internal");
        AdicionarMensagem(services, configuracao, MessageRoles.Externo, NomesDeSonda.MensagensExterno, "Messaging:External");
    }

    private static void AdicionarMensagem(
        IServiceCollection services,
        IConfiguration configuracao,
        string papel,
        string nome,
        string secao)
    {
        var endpoint = configuracao[secao + ":Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            services.AddMensagensEmMemoria(papel, nome);
            return;
        }

        var opcoes = DestinoDeMensagens.Montar(
            nome,
            endpoint,
            configuracao[secao + ":Token"],
            configuracao[secao + ":Stream"],
            configuracao[secao + ":Subject"]);
        services.AddMensagensJetStream(papel, opcoes);
    }

    private static void AdicionarWorkflow(IServiceCollection services, IConfiguration configuracao)
    {
        var endpoint = configuracao["Workflows:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            services.AddWorkflowEmMemoria();
            return;
        }

        var opcoes = DestinoDeWorkflow.Montar(endpoint, configuracao["Workflows:Namespace"], configuracao["Workflows:Queue"]);
        services.AddWorkflowTemporal(opcoes);
    }

    private static void RegistrarSonda(IHealthChecksBuilder saude, string nome)
    {
        saude.Add(new HealthCheckRegistration(
            nome,
            provedor => new AdaptadorDeSonda(
                provedor.GetServices<ISondaDeInfraestrutura>().First(sonda => sonda.Nome == nome),
                provedor.GetRequiredService<ITelemetry>()),
            HealthStatus.Unhealthy,
            new[] { "complete" },
            TimeSpan.FromSeconds(5)));
    }

    private static void GarantirBorda(IServiceCollection services, IConfiguration configuracao)
    {
        var jwt = !string.IsNullOrWhiteSpace(configuracao["Seguranca:Emissor"]);
        services.AddSingleton(new OpcoesDeAutenticacao(
            configuracao["Auth:ApiKey"],
            jwt,
            configuracao["Seguranca:Emissor"],
            configuracao["Seguranca:Audiencia"],
            configuracao["Seguranca:Chave"],
            configuracao["Identity:Issuer"],
            configuracao["Identity:Audience"],
            configuracao["Identity:SessionIssuer"],
            configuracao["Identity:SessionAudience"],
            configuracao["Identity:SessionKey"]));
        services.AddSingleton<IAutenticador, AutenticadorPorChave>();
        services.AddSingleton<IMachineCredential>(provedor => new MachineCredential(provedor.GetRequiredService<IAutenticador>()));
        services.AddSingleton<IOrganizationDirectory, BillingOrganizationDirectory>();
        RegistrarPlataforma(services, configuracao);
        RegistrarAutorizacao(services, configuracao);
        var teto = 1000;
        if (int.TryParse(configuracao["Seguranca:TetoDeOrigem"], out var lido) && lido > 0)
        {
            teto = lido;
        }

        services.AddSingleton(new LimitadorDeOrigem(teto));
    }

    private static void RegistrarAutorizacao(IServiceCollection services, IConfiguration configuracao)
    {
        var endpoint = configuracao["Authorization:Endpoint"];
        services.AddSingleton<InMemoryPolicyStore>();
        services.AddSingleton<IPolicyStore>(provedor => provedor.GetRequiredService<InMemoryPolicyStore>());
        if (!string.IsNullOrWhiteSpace(configuracao["Database:ConnectionString"]))
        {
            services.AddSingleton<IPolicyStore, SqlPolicyStore>();
        }
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            services.AddSingleton<IPolicyPublisher, LocalPolicyPublisher>();
            services.AddSingleton<InMemoryAuthorizer>();
            services.AddSingleton<IAuthorizer>(provedor => new TimedAuthorizer(provedor.GetRequiredService<InMemoryAuthorizer>()));
        }
        else
        {
            var cliente = new HttpClient
            {
                BaseAddress = new Uri(endpoint.Trim()),
                Timeout = TimeSpan.FromSeconds(5),
            };
            var usuario = configuracao["Authorization:Username"];
            var senha = configuracao["Authorization:Password"];
            if (!string.IsNullOrWhiteSpace(usuario))
            {
                var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(usuario.Trim() + ":" + (senha ?? string.Empty)));
                cliente.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Basic " + token);
            }

            services.AddSingleton<IPolicyPublisher>(new CerbosPolicyPublisher(cliente));
            services.AddSingleton<IAuthorizer>(new TimedAuthorizer(new CerbosAuthorizer(cliente)));
            services.AddHostedService<PolicySync>();
        }

        services.AddSingleton<PolicyDependencies>();
        services.AddSingleton<PolicyCatalog>();
        RegistrarConta(services, configuracao);
    }

    private static void RegistrarConta(IServiceCollection services, IConfiguration configuracao)
    {
        var mode = GovernanceEnvironment.Resolve(configuracao["App:Perfil"], configuracao["DEPLOYMENT_ENV"], string.Equals(configuracao["ALLOW_LOCAL_BOOTSTRAP"], "true", StringComparison.OrdinalIgnoreCase));
        if (mode == GovernanceMode.Abort)
        {
            throw new InvalidOperationException("App:Perfil local nao sobe quando o ambiente e homologacao ou producao.");
        }

        services.AddSingleton(new GovernanceLedger(configuracao["Identity:KeyPepper"] ?? string.Empty));
        if (!string.IsNullOrWhiteSpace(configuracao["Database:ConnectionString"]))
        {
            services.AddSingleton<IWindowStore, SqlWindowStore>();
        }

        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<SessionClock>();
        services.AddSingleton<IntegrationIntake>();
        services.AddSingleton<MutationBudget>();
        services.AddSingleton(new LocalBootstrap(GovernanceEnvironment.AllowsLocalBootstrap(mode)));
        var emissor = configuracao["Identity:Issuer"];
        if (string.IsNullOrWhiteSpace(emissor))
        {
            services.AddSingleton<IAccountDirectory, LocalAccountDirectory>();
            services.AddSingleton<IFlowableLogin, FlowableLoginUnavailable>();
            return;
        }

        var cliente = new HttpClient
        {
            BaseAddress = new Uri(emissor.Trim()),
            Timeout = TimeSpan.FromSeconds(30),
        };
        services.AddSingleton(new ZitadelSettings(
            emissor,
            configuracao["Identity:ServiceToken"] ?? string.Empty,
            configuracao["Identity:ProjectId"] ?? string.Empty,
            configuracao["Identity:OrganizationId"] ?? string.Empty));
        services.AddSingleton<IAccountDirectory>(provedor => new ZitadelAccountDirectory(cliente, provedor.GetRequiredService<ZitadelSettings>()));

        // Login do console do orquestrador Flowable nas telas do próprio console. O cliente não segue redirecionamentos:
        // o id do pedido de autorização vem do próprio redirecionamento. O código de redefinição só volta na resposta
        // em desenvolvimento e quando pedido explicitamente; fora disso vai por e-mail.
        var desenvolvimento = string.Equals(configuracao["ASPNETCORE_ENVIRONMENT"], "Development", StringComparison.OrdinalIgnoreCase);
        var clienteLogin = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(emissor.Trim()),
            Timeout = TimeSpan.FromSeconds(30),
        };
        var configuracaoLogin = new FlowableLoginSettings(
            configuracao["Flowable:Login:ClientId"] ?? string.Empty,
            configuracao["Flowable:Login:ProjectId"] ?? string.Empty,
            configuracao["Flowable:Login:RedirectUri"] ?? "http://localhost:5173/",
            configuracao["Flowable:Login:ResetUrlTemplate"] ?? "http://localhost:5173/?reset=1&userId={{.UserID}}&code={{.Code}}",
            desenvolvimento && string.Equals(configuracao["Flowable:Login:ReturnResetCode"], "true", StringComparison.OrdinalIgnoreCase));
        // O login usa o próprio usuário de serviço (IAM_LOGIN_CLIENT e ORG_USER_MANAGER, ver zitadel/provisionar.sh).
        // Sem Flowable:Login:ServiceToken, cai no token de Identity:ServiceToken, que só funciona se tiver essas permissões.
        var tokenDoLogin = configuracao["Flowable:Login:ServiceToken"];
        var configuracaoDeServico = new ZitadelSettings(
            emissor,
            string.IsNullOrWhiteSpace(tokenDoLogin) ? configuracao["Identity:ServiceToken"] ?? string.Empty : tokenDoLogin,
            configuracao["Identity:ProjectId"] ?? string.Empty,
            configuracao["Identity:OrganizationId"] ?? string.Empty);
        services.AddSingleton<IFlowableLogin>(provedor => new ZitadelFlowableLogin(clienteLogin, configuracaoDeServico, configuracaoLogin, provedor.GetRequiredService<ILoggerFactory>().CreateLogger("gatekeeper.FlowableLogin")));
    }

    private static void RegistrarPlataforma(IServiceCollection services, IConfiguration configuracao)
    {
        var emissor = configuracao["Identity:Issuer"];
        if (string.IsNullOrWhiteSpace(emissor))
        {
            services.AddSingleton<ISigningKeySource, EmptySigningKeySource>();
            services.AddSingleton<IPlatformTokenValidator, InactivePlatformTokenValidator>();
            return;
        }

        var modulo = configuracao["Identity:KeyModulus"];
        var expoente = configuracao["Identity:KeyExponent"];
        if (!string.IsNullOrWhiteSpace(modulo) && !string.IsNullOrWhiteSpace(expoente))
        {
            services.AddSingleton<ISigningKeySource>(new StaticSigningKeySource(modulo, expoente));
        }
        else
        {
            var jwks = configuracao["Identity:Jwks"];
            var endereco = string.IsNullOrWhiteSpace(jwks)
                ? emissor.Trim().TrimEnd('/') + "/oauth/v2/keys"
                : jwks.Trim();
            var cliente = new HttpClient
            {
                BaseAddress = new Uri(endereco),
                Timeout = TimeSpan.FromSeconds(5),
            };
            services.AddSingleton<ISigningKeySource>(new HttpJwksSource(cliente));
        }

        services.AddSingleton<IPlatformTokenValidator>(provedor => new PlatformTokenValidator(
            new PlatformTokenOptions(emissor, configuracao["Identity:Audience"] ?? string.Empty),
            provedor.GetRequiredService<ISigningKeySource>()));
    }

    private static bool? LerBool(IConfiguration configuracao, string chave)
    {
        var texto = configuracao[chave];
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        if (bool.TryParse(texto, out var valor))
        {
            return valor;
        }

        throw new InvalidOperationException(chave + " precisa ser true ou false.");
    }
}
