using System.Globalization;
using System.Threading.RateLimiting;
using Fcg.Users.Api.Extensions;
using Fcg.Users.Api.Middlewares;
using Fcg.Users.Application.Validators;
using Fcg.Users.Infrastructure.Extensions;
using Fcg.Users.Infrastructure.Seed;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using StackExchange.Redis;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    builder.Services.AddSwaggerExtension();
    builder.Services.AddValidatorsFromAssemblyContaining<CriarUsuarioValidator>();

    builder.Services.AddMongoDb(builder.Configuration);
    builder.Services.AddDistributedCaching(builder.Configuration);
    builder.Services.AddJwtAuthentication(builder.Configuration);
    builder.Services.AddInfrastructureServices();
    builder.Services.AddApplicationServices();
    builder.Services.AddMessaging(builder.Configuration);

    // Observabilidade (Fase 3): métricas Prometheus em /metrics + traces OTLP.
    builder.Services.AddObservability(builder.Configuration, builder.Environment);
    builder.Services.AddGatewayForwardedHeaders(builder.Configuration);

    // Conexão RabbitMQ ÚNICA e reutilizada pelo health check. Antes o AddRabbitMQ abria uma conexão
    // nova a cada readiness sem fechá-la (leak que saturava o broker). A factory cria a conexão UMA
    // vez e a reusa em todas as checagens — com auto-recovery para reconectar quando o broker volta.
    // O lock (double-checked) evita a criação concorrente se dois probes chegarem simultaneamente; se
    // a conexão estiver fechada (recovery esgotado) ela é descartada e RECRIADA na próxima check — por
    // isso não usamos Lazy<Task<IConnection>>, que cachearia uma Task falhada (broker fora no 1º check)
    // e deixaria o readiness preso em 503 mesmo após o broker voltar. Lazy e assíncrona (sem
    // sync-over-async, sem bloquear o startup): o processo sobe mesmo com o broker fora, o check
    // reporta 503, e uma tentativa futura reconecta (200).
    var healthRabbitLock = new SemaphoreSlim(1, 1);
    RabbitMQ.Client.IConnection? healthRabbitConnection = null;

    builder.Services.AddHealthChecks()
        .AddMongoDb(
            dbFactory: static sp => sp.GetRequiredService<MongoDB.Driver.IMongoDatabase>(),
            name: "mongodb",
            tags: ["ready"])
        .AddRabbitMQ(
            factory: async sp =>
            {
                var current = Volatile.Read(ref healthRabbitConnection);
                if (current?.IsOpen == true)
                    return current;

                var configuration = sp.GetRequiredService<IConfiguration>();
                // Respeita RabbitMq:Port (porta dinâmica nos testes; 5672 em compose/k8s).
                // Sem isto o health check iria à 5672 enquanto o bus fala com a porta mapeada,
                // e o readiness reportaria 503 com o broker perfeitamente no ar (issue #27).
                var rabbitPort = ushort.TryParse(configuration["RabbitMq:Port"], out var parsedPort)
                    ? parsedPort
                    : (ushort)5672;
                await healthRabbitLock.WaitAsync();
                try
                {
                    current = Volatile.Read(ref healthRabbitConnection);
                    if (current?.IsOpen == true)
                        return current;

                    // A conexão anterior está fechada (recovery esgotado) — descarta antes de recriar.
                    if (current is not null)
                    {
                        await current.DisposeAsync();
                        Volatile.Write(ref healthRabbitConnection, null);
                    }

                    var created = await new RabbitMQ.Client.ConnectionFactory
                    {
                        HostName = configuration["RabbitMq:Host"] ?? "localhost",
                        Port = rabbitPort,
                        UserName = configuration["RabbitMq:Username"] ?? "guest",
                        Password = configuration["RabbitMq:Password"] ?? "guest",
                        AutomaticRecoveryEnabled = true
                    }.CreateConnectionAsync();
                    Volatile.Write(ref healthRabbitConnection, created);
                    return created;
                }
                finally
                {
                    healthRabbitLock.Release();
                }
            },
            name: "rabbitmq",
            tags: ["ready"])
        .AddRedis(
            connectionMultiplexerFactory: static sp => sp.GetRequiredService<IConnectionMultiplexer>(),
            name: "redis",
            // ⚠️ tag "cache", NÃO "ready": o Redis é degradação, não indisponibilidade. Se
            // entrasse em "ready", um Redis fora tiraria o pod do balanceador — o exato oposto
            // do fail-open que o RedisCacheService implementa.
            tags: ["cache"]);

    var loginPermitLimit = Math.Max(
        1,
        builder.Configuration.GetValue<int?>("RateLimiting:Login:PermitLimit") ?? 5);

    var loginWindowSeconds = Math.Max(
        1,
        builder.Configuration.GetValue<int?>("RateLimiting:Login:WindowSeconds") ?? 60);

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.OnRejected = static (context, cancellationToken) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            }

            return ValueTask.CompletedTask;
        };

        options.AddPolicy("login", httpContext =>
        {
            var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString();

            if (string.IsNullOrWhiteSpace(partitionKey))
            {
                partitionKey = httpContext.Request.Headers.Host.ToString();
            }

            if (string.IsNullOrWhiteSpace(partitionKey))
            {
                partitionKey = "unknown-client";
            }

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: partitionKey,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = loginPermitLimit,
                    Window = TimeSpan.FromSeconds(loginWindowSeconds),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
        });
    });

    var app = builder.Build();

    // PRIMEIRO middleware do pipeline, antes de qualquer outro. Ele reescreve
    // Connection.RemoteIpAddress / Request.Scheme / Request.Host; tudo que roda antes dele veria
    // os valores do proxy (IP do pod do Kong) em vez dos do cliente real. Em particular o
    // CorrelationIdMiddleware, o Serilog request logging e o rate limiter dependem disso.
    app.UseForwardedHeaders();

    app.UseMiddleware<CorrelationIdMiddleware>();
    // Injeta TraceId/SpanId no contexto do Serilog: é o que permite pular de um span lento no
    // Jaeger para as linhas de log exatas daquele request (e vice-versa).
    app.Use(async (context, next) =>
    {
        var activity = System.Diagnostics.Activity.Current;
        if (activity is not null)
        {
            using (Serilog.Context.LogContext.PushProperty("TraceId", activity.TraceId.ToString()))
            using (Serilog.Context.LogContext.PushProperty("SpanId", activity.SpanId.ToString()))
            {
                await next(context);
                return;
            }
        }

        await next(context);
    });

    // O REQUEST-LOGGING FICA FORA DO HANDLER DE EXCEÇÃO, e a ordem aqui é o conserto da #29.
    //
    // Registrado DEPOIS, o Serilog virava o middleware INTERNO: a exceção escapava por ele antes de
    // chegar ao handler, ele a via como não tratada e registrava nível `Error` com `StatusCode` 500 —
    // enquanto o cliente recebia 409. Medido: 3 POSTs de e-mail duplicado respondiam 409 e produziam
    // 3 linhas `Error` com "StatusCode":500, 1:1 com as requisições.
    //
    // Registrado ANTES, ele é o EXTERNO: o handler já traduziu o status e engoliu a exceção, então o
    // request-logging observa 409 sem exceção e cai no nível Information do GetLevel abaixo.
    //
    // ⚠️ Ele precisa continuar DEPOIS do CorrelationIdMiddleware e do push de TraceId: invertê-los
    // faria a linha de request perder o enriquecimento de correlação, que é o que liga o log ao trace.
    app.UseSerilogRequestLogging(options =>
    {
        // Rebaixa para Verbose (fora do nível padrão) o log de request de endpoints de
        // infraestrutura, que são chamados de segundos em segundos pelas probes e pelo Prometheus.
        options.GetLevel = static (httpContext, elapsed, ex) =>
        {
            if (ex is not null || httpContext.Response.StatusCode >= 500)
                return Serilog.Events.LogEventLevel.Error;

            var path = httpContext.Request.Path.Value ?? string.Empty;
            if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase))
                return Serilog.Events.LogEventLevel.Verbose;

            return Serilog.Events.LogEventLevel.Information;
        };
    });

    app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "FIAP Cloud Games - Users API v1");
            options.DocumentTitle = "FIAP Cloud Games - Users API - Documentação";
        });
    }

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();
    app.MapControllers();

    // Endpoint de scrape do Prometheus. Deliberadamente SEM autenticação e NÃO exposto no API
    // Gateway (orchestration#26 não cria rota para /metrics): só o Prometheus, de dentro do
    // cluster, alcança este endereço.
    app.MapPrometheusScrapingEndpoint();

    // Liveness: valida apenas se o processo responde HTTP.
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false
    });

    // Readiness: valida dependências externas necessárias para atender requisições.
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });

    // Endpoint legado/agregado para compatibilidade.
    app.MapHealthChecks("/health");

    try
    {
        await DatabaseSeed.SeedAsync(app.Services);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Seed de dados falhou. A aplicação continuará sem dados iniciais.");
    }

    await app.RunAsync();

    // Libera recursos da conexão de health check após o host encerrar (sem concorrência possível).
    if (healthRabbitConnection is not null)
        await healthRabbitConnection.DisposeAsync();
    healthRabbitLock.Dispose();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Aplicação encerrada inesperadamente");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
