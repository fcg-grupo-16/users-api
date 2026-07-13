using System.Globalization;
using System.Threading.RateLimiting;
using Fcg.Users.Api.Middlewares;
using Fcg.Users.Application.Validators;
using Fcg.Users.Infrastructure.Extensions;
using Fcg.Users.Infrastructure.Seed;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

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
    builder.Services.AddJwtAuthentication(builder.Configuration);
    builder.Services.AddInfrastructureServices();
    builder.Services.AddApplicationServices();
    builder.Services.AddMessaging(builder.Configuration);

    // Conexão RabbitMQ ÚNICA e reutilizada pelo health check. Antes o AddRabbitMQ abria uma conexão
    // nova a cada readiness sem fechá-la (leak que saturava o broker). A factory cria a conexão UMA
    // vez e a reusa em todas as checagens — com auto-recovery para reconectar quando o broker volta.
    // O lock (double-checked) evita a criação concorrente se dois probes chegarem simultaneamente;
    // se a conexão estiver fechada (recovery esgotado) ela é descartada e recriada na próxima check.
    // Lazy e assíncrona (sem sync-over-async, sem bloquear o startup): se o broker estiver fora, a
    // criação falha, o check reporta 503, e uma tentativa futura reconecta (200).
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
                if (healthRabbitConnection?.IsOpen == true)
                    return healthRabbitConnection;

                var configuration = sp.GetRequiredService<IConfiguration>();
                await healthRabbitLock.WaitAsync();
                try
                {
                    if (healthRabbitConnection?.IsOpen == true)
                        return healthRabbitConnection;

                    // A conexão anterior está fechada (recovery esgotado) — descarta antes de recriar.
                    if (healthRabbitConnection is not null)
                    {
                        await healthRabbitConnection.DisposeAsync();
                        healthRabbitConnection = null;
                    }

                    healthRabbitConnection = await new RabbitMQ.Client.ConnectionFactory
                    {
                        HostName = configuration["RabbitMq:Host"] ?? "localhost",
                        UserName = configuration["RabbitMq:Username"] ?? "guest",
                        Password = configuration["RabbitMq:Password"] ?? "guest",
                        AutomaticRecoveryEnabled = true
                    }.CreateConnectionAsync();
                    return healthRabbitConnection;
                }
                finally
                {
                    healthRabbitLock.Release();
                }
            },
            name: "rabbitmq",
            tags: ["ready"]);

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

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
    app.UseSerilogRequestLogging();

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
