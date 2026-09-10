using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Fcg.Users.Api.Extensions;

/// <summary>
/// Instrumentação de observabilidade do serviço (Fase 3): métricas no formato Prometheus
/// (expostas em <c>/metrics</c>) e traces distribuídos exportados por OTLP.
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// Registra OpenTelemetry para métricas e traces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Métricas.</b> Não criamos métrica customizada: o próprio ASP.NET Core publica o
    /// histograma <c>http.server.request.duration</c> com os atributos
    /// <c>http.response.status_code</c>, <c>http.request.method</c> e <c>http.route</c>. Um
    /// histograma entrega de uma vez as TRÊS métricas que a Fase 3 exige — latência (via
    /// <c>histogram_quantile</c>), contagem total (série <c>_count</c>) e contagem por status
    /// code (label). Métrica de negócio custom só se aparecer necessidade real.
    /// </para>
    /// <para>
    /// <b>Traces.</b> O MassTransit 8 propaga contexto de trace W3C entre publisher e consumer
    /// nativamente, então ligar o exportador OTLP aqui basta para o trace atravessar
    /// users-api → RabbitMQ → notifications-function. Não é preciso instrumentar a mensageria.
    /// </para>
    /// <para>
    /// <b>Configuração.</b> Tudo por variável de ambiente padrão do OTel
    /// (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, <c>OTEL_SERVICE_NAME</c>), provisionadas pelo
    /// ConfigMap do repo <c>orchestration</c> — 12-factor, nada hardcoded.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // OTEL_SERVICE_NAME é lido automaticamente pelo SDK; o fallback aqui só cobre execução
        // local sem env var (dotnet run), para o serviço não aparecer como "unknown_service".
        var serviceName = configuration["OTEL_SERVICE_NAME"] ?? "users-api";

        // Endpoint OTLP AUSENTE => traces desligados de propósito. Isso mantém `dotnet run` e os
        // testes de integração funcionando sem Jaeger no ar (sem isso, o exportador tenta conectar
        // e enche o log de erro a cada intervalo de export).
        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString() ?? "0.0.0")
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = environment.EnvironmentName
                }));

        otel.WithMetrics(metrics => metrics
            // Histograma http.server.request.duration + métricas de Kestrel/roteamento.
            .AddAspNetCoreInstrumentation()
            // Latência das chamadas HTTP de saída (útil quando o serviço passar a chamar outros).
            .AddHttpClientInstrumentation()
            // GC, heap, threadpool, exceções — responde "é o app ou é o banco?".
            .AddRuntimeInstrumentation()
            // Expõe no endpoint /metrics (mapeado no Program.cs via MapPrometheusScrapingEndpoint).
            .AddPrometheusExporter());

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            otel.WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    // NÃO gere trace para /health* e /metrics: são chamados a cada 10s pelas
                    // probes e a cada 15s pelo Prometheus, e afogariam o Jaeger com ruído,
                    // escondendo os traces que interessam.
                    options.Filter = context =>
                    {
                        var path = context.Request.Path.Value ?? string.Empty;
                        return !path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
                            && !path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase);
                    };
                    options.RecordException = true;
                })
                .AddHttpClientInstrumentation()
                // O source do MassTransit: é o que faz o span de publicação aparecer no trace e
                // amarrar publisher e consumer num único trace distribuído.
                .AddSource("MassTransit")
                .AddOtlpExporter());
        }

        return services;
    }
}
