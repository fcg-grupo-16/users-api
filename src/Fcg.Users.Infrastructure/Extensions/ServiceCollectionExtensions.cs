using System.Text;
using Fcg.Users.Application.Interfaces;
using Fcg.Users.Application.Services;
using Fcg.Users.Domain.Repositories;
using Fcg.Users.Infrastructure.Caching;
using Fcg.Users.Infrastructure.Messaging;
using Fcg.Users.Infrastructure.Persistence;
using Fcg.Users.Infrastructure.Repositories;
using Fcg.Users.Infrastructure.Services;
using Fcg.Users.Infrastructure.Settings;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Fcg.Users.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMongoDb(this IServiceCollection services, IConfiguration configuration)
    {
        var mongoSettings = configuration.GetSection(MongoDbSettings.SectionName).Get<MongoDbSettings>()
            ?? throw new InvalidOperationException("MongoDbSettings não configurado.");

        services.Configure<MongoDbSettings>(configuration.GetSection(MongoDbSettings.SectionName));

        var mongoClient = new MongoClient(mongoSettings.ConnectionString);
        var mongoDatabase = mongoClient.GetDatabase(mongoSettings.DatabaseName);

        services.AddSingleton<IMongoClient>(mongoClient);
        services.AddSingleton(mongoDatabase);

        services.AddDbContext<AppDbContext>(options =>
            options.UseMongoDB(mongoClient, mongoSettings.DatabaseName));

        return services;
    }

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
            ?? throw new InvalidOperationException("JwtSettings não configurado.");

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
                ClockSkew = TimeSpan.Zero
            };
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("ApenasAdmin", policy =>
                policy.RequireRole("Administrador"));
            options.AddPolicy("UsuarioAutenticado", policy =>
                policy.RequireAuthenticatedUser());
        });

        return services;
    }

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IUsuarioRepository, UsuarioRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();

        // Cache: o decorator é o IUsuarioService que os controllers recebem; UsuarioService é
        // registrado pelo tipo CONCRETO para o decorator poder resolvê-lo sem recursão infinita.
        services.AddScoped<UsuarioService>();
        services.AddScoped<IUsuarioService>(sp => new CachedUsuarioService(
            sp.GetRequiredService<UsuarioService>(),
            sp.GetRequiredService<ICacheService>()));

        return services;
    }

    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMassTransit(x =>
        {
            x.AddMongoDbOutbox(o =>
            {
                o.QueryDelay = TimeSpan.FromSeconds(5);
                o.ClientFactory(provider => provider.GetRequiredService<IMongoClient>());
                o.DatabaseFactory(provider => provider.GetRequiredService<IMongoDatabase>());
                o.DuplicateDetectionWindow = TimeSpan.FromSeconds(30);
                o.UseBusOutbox();
            });

            x.UsingRabbitMq((ctx, cfg) =>
            {
                var host = configuration["RabbitMq:Host"] ?? "localhost";
                var user = configuration["RabbitMq:Username"] ?? "guest";
                var pass = configuration["RabbitMq:Password"] ?? "guest";
                cfg.Host(host, "/", h => { h.Username(user); h.Password(pass); });
                cfg.ConfigureEndpoints(ctx);
            });
        });

        return services;
    }

    public static void AddSwaggerExtension(this IServiceCollection service)
    {
        service.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "FIAP Cloud Games - Users API",
                Version = "v1",
                Description = "Microsserviço de cadastro, autenticação e autorização de usuários da plataforma FIAP Cloud Games."
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "Informe apenas o token JWT (sem o prefixo 'Bearer').",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });

            var xmlFiles = Directory.GetFiles(AppContext.BaseDirectory, "*.xml", SearchOption.TopDirectoryOnly);
            foreach (var xmlFile in xmlFiles)
            {
                options.IncludeXmlComments(xmlFile);
            }
        });
    }

    /// <summary>
    /// Registra o cache distribuído (Redis) e o health check correspondente.
    /// </summary>
    /// <remarks>
    /// Sem connection string (ou com <c>Redis:Enabled=false</c>) o serviço sobe com
    /// <see cref="NoOpCacheService"/> — dev local e testes de integração funcionam sem Redis.
    /// </remarks>
    public static IServiceCollection AddDistributedCaching(
        this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(RedisSettings.SectionName).Get<RedisSettings>()
            ?? new RedisSettings();

        services.Configure<RedisSettings>(configuration.GetSection(RedisSettings.SectionName));

        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            services.AddSingleton<ICacheService, NoOpCacheService>();
            return services;
        }

        var options = ConfigurationOptions.Parse(settings.ConnectionString);
        // AbortOnConnectFail=false: o app SOBE mesmo com o Redis fora e reconecta depois. Com o
        // default (true) o multiplexer lança no startup e o pod entra em CrashLoopBackOff — ou
        // seja, o cache derrubaria o serviço, exatamente o oposto do que queremos.
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 2000;
        options.SyncTimeout = 2000;

        var multiplexer = ConnectionMultiplexer.Connect(options);
        services.AddSingleton<IConnectionMultiplexer>(multiplexer);

        services.AddStackExchangeRedisCache(redis =>
        {
            redis.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(multiplexer);
            // Prefixo de TODA chave: isolamento lógico por serviço no mesmo Redis.
            redis.InstanceName = settings.InstanceName;
        });

        services.AddSingleton<ICacheService, RedisCacheService>();
        return services;
    }
}
