using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Fcg.Users.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using RabbitMQ.Client;
using StackExchange.Redis;

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
    public async Task ObterUsuario_UsaCache_EAtualizarInvalidaChaveDoItem()
    {
        var email = $"cache-{Guid.NewGuid():N}@it.local";
        var cadastro = await _client.PostAsJsonAsync("/api/v1/usuarios", new
        {
            nome = "Usuario Cache",
            email,
            senha = "Senha@1234"
        });

        cadastro.StatusCode.Should().Be(HttpStatusCode.Created);
        var usuario = await ReadAsAsync<UsuarioResponse>(cadastro);
        var login = await LoginAsync(email, "Senha@1234");
        var authorization = CreateBearer(login.Token);

        var primeiraLeitura = await _client.GetAsync($"/api/v1/usuarios/{usuario.Id}", authorization);
        primeiraLeitura.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.RedisConnectionString);
        var database = redis.GetDatabase();
        var cacheKey = $"fcg:users:usuario:{usuario.Id}";
        (await database.KeyExistsAsync(cacheKey)).Should().BeTrue();

        var segundaLeitura = await _client.GetAsync($"/api/v1/usuarios/{usuario.Id}", authorization);
        segundaLeitura.StatusCode.Should().Be(HttpStatusCode.OK);

        var atualizacao = await _client.PutAsJsonAsync(
            $"/api/v1/usuarios/{usuario.Id}",
            new { nome = "Usuario Cache Atualizado", email },
            authorization);

        atualizacao.StatusCode.Should().Be(HttpStatusCode.OK);
        (await database.KeyExistsAsync(cacheKey)).Should().BeFalse();
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

    [Theory(DisplayName = "Login com e-mail malformado devolve 401, igual a e-mail inexistente")]
    [InlineData("a@b")]
    [InlineData("x y@fcg.com")]
    public async Task Login_EmailMalformado_DeveRetornar401(string emailMalformado)
    {
        // Antes da #28 estas duas entradas devolviam 500: elas passam pelo validador do DTO e
        // reprovam no value object, e a ValidacaoException saía embrulhada de dentro do LINQ.
        var resposta = await PostLoginAsync(_client, emailMalformado, "Senha@1234");

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Malformado e inexistente são indistinguíveis pela resposta")]
    public async Task Login_MalformadoEInexistente_TemRespostaIdentica()
    {
        var malformado = await PostLoginAsync(_client, "a@b", "Senha@1234");
        var inexistente = await PostLoginAsync(_client, $"nao-existe-{Guid.NewGuid():N}@fcg.com", "Senha@1234");

        malformado.StatusCode.Should().Be(inexistente.StatusCode);

        // O corpo também: status igual com mensagem diferente continua sendo oráculo de enumeração.
        var corpoMalformado = await malformado.Content.ReadAsStringAsync();
        var corpoInexistente = await inexistente.Content.ReadAsStringAsync();
        corpoMalformado.Should().Be(corpoInexistente);
    }

    [Fact(DisplayName = "Conflito de e-mail duplicado é logado como 409, e não como 500/Error")]
    public async Task CriarUsuario_Duplicado_LogaStatusRespondido()
    {
        var email = $"dup-{Guid.NewGuid():N}@it.local";
        var corpo = new { nome = "Dup", email, senha = "Senha@1234" };

        (await _client.PostAsJsonAsync("/api/v1/usuarios", corpo)).StatusCode
            .Should().Be(HttpStatusCode.Created);

        factory.Log.Limpar();

        var conflito = await _client.PostAsJsonAsync("/api/v1/usuarios", corpo);
        conflito.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // O que o CLIENTE recebe já estava certo antes da #29. O que estava errado era o log: o
        // request-logging do Serilog era interno ao handler de exceção, via a exceção escapar e
        // registrava Error/500 para uma requisição que respondeu 409.
        var requisicoes = factory.Log.DeRequisicao("/api/v1/usuarios")
            .Where(e => CapturaDeLog.StatusDe(e) is not null)
            .ToList();

        requisicoes.Should().NotBeEmpty("o request-logging precisa registrar a requisição");
        requisicoes.Should().OnlyContain(e => CapturaDeLog.StatusDe(e) == 409);
        requisicoes.Should().NotContain(e => e.Level >= Serilog.Events.LogEventLevel.Error);

        // GUARDA CONTRA REGRESSÃO DE ORDEM: mover o request-logging para fora do handler é fácil de
        // fazer errado — na primeira tentativa desta correção eu o deixei ANTES do push de TraceId, e
        // a linha de request perdeu a correlação com o trace sem nenhum teste reclamar.
        requisicoes.Should().OnlyContain(
            e => e.Properties.ContainsKey("TraceId"),
            "a linha de request precisa continuar correlacionada com o trace");
    }

    [Fact(DisplayName = "Contato: token de SERVIÇO lê o e-mail de qualquer usuário")]
    public async Task ObterContato_ComTokenDeServico_DeveRetornar200ComOEmail()
    {
        var email = $"contato-{Guid.NewGuid():N}@it.local";
        var criado = await _client.PostAsJsonAsync("/api/v1/usuarios",
            new { nome = "Contato", email, senha = "Senha@1234" });
        criado.StatusCode.Should().Be(HttpStatusCode.Created);
        var usuario = await ReadAsAsync<UsuarioResponse>(criado);

        var requisicao = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/usuarios/{usuario.Id}/contato");
        requisicao.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", FcgWebAppFactory.GerarTokenDeServico());

        var resposta = await _client.SendAsync(requisicao);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        var corpo = await resposta.Content.ReadAsStringAsync();
        corpo.Should().Contain(email);

        // MENOR SUPERFÍCIE: o DTO tem um campo só. Se alguém trocar por UsuarioResponseDto, nome,
        // tipo e data de criação passariam a vazar para quem só precisa despachar um e-mail.
        corpo.Should().NotContain("\"nome\"");
        corpo.Should().NotContain("\"tipo\"");
    }

    [Fact(DisplayName = "Contato: sem token devolve 401")]
    public async Task ObterContato_SemToken_DeveRetornar401()
    {
        var resposta = await _client.GetAsync("/api/v1/usuarios/507f1f77bcf86cd799439011/contato");

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Contato: token de USUÁRIO comum não serve, nem o de administrador")]
    public async Task ObterContato_ComTokenDeUsuario_NaoDeveAutorizar()
    {
        // ESTE é o teste que prova a separação: o token abaixo é válido para todo o resto da API,
        // porém assinado com a chave dos USUÁRIOS. A política prende o esquema de serviço, então ele
        // não passa — mesmo que alguém consiga forjar a role.
        var email = $"comum-{Guid.NewGuid():N}@it.local";
        await _client.PostAsJsonAsync("/api/v1/usuarios", new { nome = "Comum", email, senha = "Senha@1234" });
        var login = await LoginAsync(email, "Senha@1234");

        var requisicao = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuarios/507f1f77bcf86cd799439011/contato");
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

        var resposta = await _client.SendAsync(requisicao);

        resposta.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "Contato: token de serviço SEM a role Servico é recusado")]
    public async Task ObterContato_TokenDeServicoSemRole_DeveRecusar()
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usuarios/507f1f77bcf86cd799439011/contato");
        requisicao.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", FcgWebAppFactory.GerarTokenDeServico(role: "Administrador"));

        var resposta = await _client.SendAsync(requisicao);

        resposta.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
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

    [Fact(DisplayName = "Com ForwardedHeaders habilitado, o rate limit de login particiona pelo IP real do cliente")]
    public async Task RateLimit_ParticionaPeloIpRealDoCliente()
    {
        using var forwardedHeadersFactory = CreateRateLimitedFactory(
            permitLimit: 5,
            windowSeconds: 60,
            forwardedHeadersEnabled: true);
        using var client = forwardedHeadersFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        for (var tentativa = 1; tentativa <= 5; tentativa++)
        {
            var response = await PostLoginAsync(
                client,
                "naoexiste@email.com",
                "errada",
                forwardedFor: "203.0.113.10");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var blockedResponse = await PostLoginAsync(
            client,
            "naoexiste@email.com",
            "errada",
            forwardedFor: "203.0.113.10");
        blockedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var separateBucketResponse = await PostLoginAsync(
            client,
            "naoexiste@email.com",
            "errada",
            forwardedFor: "203.0.113.99");
        separateBucketResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Sem ForwardedHeaders habilitado, X-Forwarded-For é ignorado")]
    public async Task ForwardedHeaders_Desabilitado_IgnoraHeaderDoCliente()
    {
        using var forwardedHeadersFactory = CreateRateLimitedFactory(
            permitLimit: 5,
            windowSeconds: 60,
            forwardedHeadersEnabled: false);
        using var client = forwardedHeadersFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        for (var tentativa = 1; tentativa <= 5; tentativa++)
        {
            var response = await PostLoginAsync(
                client,
                "naoexiste@email.com",
                "errada",
                forwardedFor: $"203.0.113.{tentativa}");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var blockedResponse = await PostLoginAsync(
            client,
            "naoexiste@email.com",
            "errada",
            forwardedFor: "203.0.113.99");
        blockedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
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

        // PAUSA em vez de parar: parar republicaria as portas e mudaria a porta mapeada, e o bus
        // do app — configurado no startup — ficaria apontando para a antiga. Ver FcgWebAppFactory.
        await factory.PausarRabbitMqAsync();

        var email = $"outbox-{Guid.NewGuid():N}@it.local";

        var cadastro = await _client.PostAsJsonAsync("/api/v1/usuarios", new
        {
            nome = "Usuario Outbox",
            email,
            senha = "Senha@1234"
        });

        cadastro.StatusCode.Should().Be(HttpStatusCode.Created);

        await factory.RetomarRabbitMqAsync();

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

    private WebApplicationFactory<Program> CreateRateLimitedFactory(
        int permitLimit,
        int windowSeconds,
        bool forwardedHeadersEnabled = false)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Login:PermitLimit", permitLimit.ToString());
            builder.UseSetting("RateLimiting:Login:WindowSeconds", windowSeconds.ToString());
            builder.UseSetting("ForwardedHeaders:Enabled", forwardedHeadersEnabled.ToString());
            builder.UseSetting("ForwardedHeaders:KnownNetworks:0", "127.0.0.1/32");
            builder.UseSetting("ForwardedHeaders:KnownNetworks:1", "::1/128");
        });
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        string email,
        string senha,
        string? forwardedFor = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email, senha })
        };

        if (forwardedFor is not null)
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);

        return await client.SendAsync(request);
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
