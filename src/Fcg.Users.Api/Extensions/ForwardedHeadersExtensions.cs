using System.Net;
using Fcg.Users.Infrastructure.Settings;
using Microsoft.AspNetCore.HttpOverrides;

namespace Fcg.Users.Api.Extensions;

/// <summary>
/// Processamento de headers <c>X-Forwarded-*</c> para operação atrás do API Gateway (Kong).
/// </summary>
public static class ForwardedHeadersExtensions
{
    /// <summary>
    /// Configura o <see cref="ForwardedHeadersMiddleware"/> a partir da seção
    /// <c>ForwardedHeaders</c> da configuração.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Por que isso importa aqui:</b> o rate limiter de login particiona por
    /// <c>Connection.RemoteIpAddress</c>. Atrás de um proxy, sem este middleware, esse valor é o IP
    /// do pod do Kong para TODOS os clientes — um único bucket global de 5 tentativas/minuto, ou
    /// seja, alguém errando a senha bloquearia o login de todos.
    /// </para>
    /// <para>
    /// <b>Por que restringir a origem:</b> <c>X-Forwarded-For</c> é enviado pelo cliente. Confiar
    /// nele sem restrição permite burlar o rate limit trocando o header a cada tentativa. Por isso
    /// só aceitamos os headers vindos das redes declaradas em <c>KnownNetworks</c>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddGatewayForwardedHeaders(
        this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(ForwardedHeadersSettings.SectionName)
            .Get<ForwardedHeadersSettings>() ?? new ForwardedHeadersSettings();

        services.Configure<ForwardedHeadersSettings>(
            configuration.GetSection(ForwardedHeadersSettings.SectionName));

        if (!settings.Enabled)
            return services;

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost;

            options.ForwardLimit = settings.ForwardLimit;

            // Os defaults do ASP.NET Core só confiam em ::1/127.0.0.1. Como o Kong é outro pod,
            // limpamos e declaramos as redes do cluster explicitamente.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var cidr in settings.KnownNetworks)
            {
                var parts = cidr.Split('/', 2);
                if (parts.Length == 2
                    && IPAddress.TryParse(parts[0], out var prefix)
                    && int.TryParse(parts[1], out var length))
                {
                    options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, length));
                }
                // CIDR malformado é IGNORADO em vez de derrubar o startup: config ruim não pode
                // impedir o pod de subir. A ausência aparece no comportamento (headers não
                // aceitos) e o operador vê no /health.
            }

            // KnownNetworks vazio => o middleware aceita de qualquer origem. Só aceitável em
            // ambiente fechado; documentado no README.
        });

        return services;
    }
}
