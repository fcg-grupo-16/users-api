using System.Net;
using FluentAssertions;
using Fcg.Users.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Fcg.Users.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class HealthChecksIntegrationTests(FcgWebAppFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    [Fact]
    public async Task HealthLive_DeveRetornar200_QuandoAplicacaoEstaAtiva()
    {
        var response = await _client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_DeveRetornar200_QuandoDependenciasEstaoDisponiveis()
    {
        await WaitForStatusAsync("/health/ready", HttpStatusCode.OK, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task HealthChecks_QuandoMongoIndisponivel_LivePermanece200_E_ReadyRetorna503()
    {
        using var faultyMongoFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("MongoDbSettings:ConnectionString", "mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=1000&connectTimeoutMS=1000");
        });

        using var client = faultyMongoFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await WaitForStatusAsync(client, "/health/live", HttpStatusCode.OK, TimeSpan.FromSeconds(10));
        await WaitForStatusAsync(client, "/health/ready", HttpStatusCode.ServiceUnavailable, TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task HealthChecks_QuandoRabbitIndisponivel_LivePermanece200_E_ReadyRetorna503()
    {
        using var faultyRabbitFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RabbitMq:Host", "invalid-rabbit-host");
        });

        using var client = faultyRabbitFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await WaitForStatusAsync(client, "/health/live", HttpStatusCode.OK, TimeSpan.FromSeconds(10));
        await WaitForStatusAsync(client, "/health/ready", HttpStatusCode.ServiceUnavailable, TimeSpan.FromSeconds(20));
    }

    private Task WaitForStatusAsync(string path, HttpStatusCode expected, TimeSpan timeout)
        => WaitForStatusAsync(_client, path, expected, timeout);

    private static async Task WaitForStatusAsync(HttpClient client, string path, HttpStatusCode expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        HttpStatusCode? lastStatus = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(path);
                lastStatus = response.StatusCode;

                if (response.StatusCode == expected)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Durante restart de dependencias pode haver falha transiente de conexao.
            }

            await Task.Delay(500);
        }

        lastStatus.Should().Be(expected, $"endpoint {path} deveria retornar {expected} dentro de {timeout}.");
    }
}
