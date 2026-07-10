using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Fcg.Users.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using RabbitMQ.Client;

namespace Fcg.Users.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class UsuariosApiIntegrationTests(FcgWebAppFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    [Fact]
    public async Task Cadastro_Login_Crud_Deve_Funcionar_De_Ponta_A_Ponta()
    {
        var emailOriginal = $"user-{Guid.NewGuid():N}@it.local";
        var senha = "Senha@1234";

        var criarResponse = await _client.PostAsJsonAsync("/api/v1/usuarios", new
        {
            nome = "Usuario Integracao",
            email = emailOriginal,
            senha
        });

        criarResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var usuarioCriado = await ReadAsAsync<UsuarioResponse>(criarResponse);
        usuarioCriado.Id.Should().NotBeNullOrWhiteSpace();

        var loginUsuario = await LoginAsync(emailOriginal, senha);
        loginUsuario.Token.Should().NotBeNullOrWhiteSpace();

        var obterResponse = await _client.GetAsync(
            $"/api/v1/usuarios/{usuarioCriado.Id}",
            CreateBearer(loginUsuario.Token));

        obterResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var emailAtualizado = $"updated-{Guid.NewGuid():N}@it.local";
        var atualizarResponse = await _client.PutAsJsonAsync(
            $"/api/v1/usuarios/{usuarioCriado.Id}",
            new
            {
                nome = "Usuario Integracao Atualizado",
                email = emailAtualizado
            },
            CreateBearer(loginUsuario.Token));

        atualizarResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var usuarioAtualizado = await ReadAsAsync<UsuarioResponse>(atualizarResponse);
        usuarioAtualizado.Email.Should().Be(emailAtualizado);

        var loginAdmin = await LoginAsync("admin@fcg.com", "Admin@123456");

        var deleteResponse = await _client.DeleteAsync(
            $"/api/v1/usuarios/{usuarioCriado.Id}",
            CreateBearer(loginAdmin.Token));

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var obterRemovidoResponse = await _client.GetAsync(
            $"/api/v1/usuarios/{usuarioCriado.Id}",
            CreateBearer(loginAdmin.Token));

        obterRemovidoResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CriarUsuario_ComPayloadInvalido_DeveRetornar422()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/usuarios", new
        {
            nome = "",
            email = "invalido",
            senha = "123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CriarUsuario_ComEmailDuplicado_DeveRetornar409()
    {
        var email = $"duplicado-{Guid.NewGuid():N}@it.local";

        var primeiroCadastro = await _client.PostAsJsonAsync("/api/v1/usuarios", new
        {
            nome = "Primeiro Cadastro",
            email,
            senha = "Senha@1234"
        });

        primeiroCadastro.StatusCode.Should().Be(HttpStatusCode.Created);

        var segundoCadastro = await _client.PostAsJsonAsync("/api/v1/usuarios", new
        {
            nome = "Segundo Cadastro",
            email,
            senha = "Senha@1234"
        });

        segundoCadastro.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ListarUsuarios_SemToken_DeveRetornar401()
    {
        var semTokenResponse = await _client.GetAsync("/api/v1/usuarios");
        semTokenResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListarUsuarios_ComTokenDeUsuarioComum_DeveRetornar403()
    {

        var email = $"nao-admin-{Guid.NewGuid():N}@it.local";
        var senha = "Senha@1234";

        var cadastro = await _client.PostAsJsonAsync("/api/v1/usuarios", new
        {
            nome = "Usuario Comum",
            email,
            senha
        });

        cadastro.StatusCode.Should().Be(HttpStatusCode.Created);

        var login = await LoginAsync(email, senha);

        var comTokenUsuarioComum = await _client.GetAsync(
            "/api/v1/usuarios",
            CreateBearer(login.Token));

        comTokenUsuarioComum.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Login_AcimaDoLimite_DeveRetornar429()
    {
        using var rateLimitedFactory = CreateRateLimitedFactory(permitLimit: 5, windowSeconds: 60);
        using var client = rateLimitedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        for (var tentativa = 1; tentativa <= 5; tentativa++)
        {
            var response = await PostLoginAsync(client, "naoexiste@email.com", "errada");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var blockedResponse = await PostLoginAsync(client, "naoexiste@email.com", "errada");
        blockedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Login_DentroDoLimite_DeveFuncionarNormalmente()
    {
        using var rateLimitedFactory = CreateRateLimitedFactory(permitLimit: 5, windowSeconds: 60);
        using var client = rateLimitedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await PostLoginAsync(client, "admin@fcg.com", "Admin@123456");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_NaoDeveSerAfetadoPelaPoliticaDeLogin()
    {
        using var rateLimitedFactory = CreateRateLimitedFactory(permitLimit: 2, windowSeconds: 60);
        using var client = rateLimitedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        for (var tentativa = 1; tentativa <= 3; tentativa++)
        {
            var response = await PostLoginAsync(client, "naoexiste@email.com", "errada");

            if (tentativa <= 2)
            {
                response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            }
            else
            {
                response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            }
        }

        var healthResponse = await client.GetAsync("/health");
        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CriarUsuario_DevePublicar_UserCreatedEvent_NoRabbitMq()
    {
        await using var connection = await CreateRabbitConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        const string exchangeName = "Fcg.Contracts.Events:UserCreatedEvent";

        await channel.ExchangeDeclareAsync(exchangeName, ExchangeType.Fanout, durable: true, autoDelete: false);

        var queue = await channel.QueueDeclareAsync(
            queue: string.Empty,
            durable: false,
            exclusive: true,
            autoDelete: true);

        await channel.QueueBindAsync(queue.QueueName, exchangeName, routingKey: string.Empty);

        var email = $"event-{Guid.NewGuid():N}@it.local";

        var cadastro = await _client.PostAsJsonAsync("/api/v1/usuarios", new
        {
            nome = "Usuario Evento",
            email,
            senha = "Senha@1234"
        });

        cadastro.StatusCode.Should().Be(HttpStatusCode.Created);

        var envelope = await WaitForMessageAsync(channel, queue.QueueName, TimeSpan.FromSeconds(30));

        envelope.Should().NotBeNull();
        envelope!.RootElement.GetProperty("messageType")
            .EnumerateArray()
            .Select(x => x.GetString())
            .Should().Contain("urn:message:Fcg.Contracts.Events:UserCreatedEvent");

        var messageNode = envelope.RootElement.GetProperty("message");
        var emailNode = messageNode.TryGetProperty("email", out var emailCamel)
            ? emailCamel
            : messageNode.GetProperty("Email");

        emailNode.GetString().Should().Be(email);
    }

    [Fact]
    public async Task CriarUsuario_ComRabbitIndisponivel_DeveRetornar201_E_PublicarEvento_AposRetornoDoBroker()
    {
        var queueName = $"users-outbox-it-{Guid.NewGuid():N}";
        const string exchangeName = "Fcg.Contracts.Events:UserCreatedEvent";

        await using (var setupConnection = await CreateRabbitConnectionAsync())
        await using (var setupChannel = await setupConnection.CreateChannelAsync())
        {
            await setupChannel.ExchangeDeclareAsync(exchangeName, ExchangeType.Fanout, durable: true, autoDelete: false);
            await setupChannel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: true);
            await setupChannel.QueueBindAsync(queueName, exchangeName, routingKey: string.Empty);
        }

        await factory.StopRabbitMqAsync();

        var email = $"outbox-{Guid.NewGuid():N}@it.local";

        var cadastro = await _client.PostAsJsonAsync("/api/v1/usuarios", new
        {
            nome = "Usuario Outbox",
            email,
            senha = "Senha@1234"
        });

        cadastro.StatusCode.Should().Be(HttpStatusCode.Created);

        await factory.StartRabbitMqAsync();

        await using var assertConnection = await CreateRabbitConnectionWithRetryAsync(TimeSpan.FromSeconds(30));
        await using var assertChannel = await assertConnection.CreateChannelAsync();

        var envelope = await WaitForMessageAsync(assertChannel, queueName, TimeSpan.FromSeconds(60));

        envelope.Should().NotBeNull();
        envelope!.RootElement.GetProperty("messageType")
            .EnumerateArray()
            .Select(x => x.GetString())
            .Should().Contain("urn:message:Fcg.Contracts.Events:UserCreatedEvent");

        var messageNode = envelope.RootElement.GetProperty("message");
        var emailNode = messageNode.TryGetProperty("email", out var emailCamel)
            ? emailCamel
            : messageNode.GetProperty("Email");

        emailNode.GetString().Should().Be(email);
    }

    private async Task<TokenResponse> LoginAsync(string email, string senha)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            senha
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await ReadAsAsync<TokenResponse>(response);
    }

    private async Task<IConnection> CreateRabbitConnectionAsync()
    {
        var connectionFactory = new ConnectionFactory
        {
            HostName = factory.RabbitHost,
            Port = factory.RabbitPort,
            UserName = factory.RabbitUsernameValue,
            Password = factory.RabbitPasswordValue
        };

        return await connectionFactory.CreateConnectionAsync();
    }

    private WebApplicationFactory<Program> CreateRateLimitedFactory(int permitLimit, int windowSeconds)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Login:PermitLimit", permitLimit.ToString());
            builder.UseSetting("RateLimiting:Login:WindowSeconds", windowSeconds.ToString());
        });
    }

    private static Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string email, string senha)
    {
        return client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            senha
        });
    }

    private async Task<IConnection> CreateRabbitConnectionWithRetryAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                return await CreateRabbitConnectionAsync();
            }
            catch (Exception ex)
            {
                lastError = ex;
                await Task.Delay(500);
            }
        }

        throw new TimeoutException("Não foi possível conectar ao RabbitMQ dentro do tempo esperado.", lastError);
    }

    private static async Task<JsonDocument?> WaitForMessageAsync(IChannel channel, string queueName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var message = await channel.BasicGetAsync(queueName, autoAck: true);
            if (message is not null)
            {
                return JsonDocument.Parse(message.Body.ToArray());
            }

            await Task.Delay(500);
        }

        return null;
    }

    private static AuthenticationHeaderValue CreateBearer(string token) =>
        new("Bearer", token);

    private async Task<T> ReadAsAsync<T>(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        var parsed = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);

        parsed.Should().NotBeNull();
        return parsed!;
    }
}

internal static class HttpClientAuthExtensions
{
    public static Task<HttpResponseMessage> GetAsync(
        this HttpClient client,
        string requestUri,
        AuthenticationHeaderValue authorization)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri)
        {
            Headers = { Authorization = authorization }
        };

        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> DeleteAsync(
        this HttpClient client,
        string requestUri,
        AuthenticationHeaderValue authorization)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, requestUri)
        {
            Headers = { Authorization = authorization }
        };

        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> PutAsJsonAsync<T>(
        this HttpClient client,
        string requestUri,
        T value,
        AuthenticationHeaderValue authorization)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Headers = { Authorization = authorization },
            Content = JsonContent.Create(value)
        };

        return client.SendAsync(request);
    }
}

internal sealed record UsuarioResponse(
    string Id,
    string Nome,
    string Email);

internal sealed record TokenResponse(
    string Token,
    DateTime Expiracao,
    string RefreshToken,
    DateTime RefreshTokenExpiracao);
