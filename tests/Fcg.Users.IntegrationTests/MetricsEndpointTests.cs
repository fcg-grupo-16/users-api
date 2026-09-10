using System.Net;
using Fcg.Users.IntegrationTests.Infrastructure;

namespace Fcg.Users.IntegrationTests;

[Collection(nameof(IntegrationTestCollection))]
public sealed class MetricsEndpointTests(FcgWebAppFactory factory)
{
    [Fact(DisplayName = "GET /metrics expõe as métricas HTTP no formato Prometheus")]
    public async Task Metrics_ExpoeMetricasHttp()
    {
        var client = factory.CreateClient();

        // Gera pelo menos uma requisição, senão o histograma não tem amostra e não é emitido.
        await client.GetAsync("/health");

        var response = await client.GetAsync("/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        // Formato de exposição do Prometheus (não JSON).
        Assert.Contains("# TYPE", body);
        // A métrica que sustenta os 3 painéis obrigatórios do dashboard: latência, contagem e
        // contagem por status code. Se este assert quebrar, o dashboard do Grafana fica vazio.
        Assert.Contains("http_server_request_duration_seconds", body);
    }
}
