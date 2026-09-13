using System.Net;
using System.Net.Sockets;
using Fcg.Users.Infrastructure.Settings;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
// Alias obrigatório: Microsoft.AspNetCore.HttpOverrides também declara um IPNetwork (obsoleto
// desde o .NET 9), então o nome fica ambíguo com os dois namespaces importados. Aponta para o
// tipo do BCL, que é o que KnownIPNetworks espera.
using IPNetwork = System.Net.IPNetwork;

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

        var resultado = InterpretarRedesConhecidas(settings);

        // O Serilog já está disponível aqui: o Program.cs cria o bootstrap logger antes de chamar
        // esta extensão. Warning, e não Debug, porque cada um destes casos significa que a
        // confiança em X-Forwarded-* está mais ampla do que o operador provavelmente pretendia.
        foreach (var aviso in resultado.Avisos)
        {
            Log.Warning("{AvisoDeConfiguracao}", aviso);
        }

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

            foreach (var rede in resultado.Redes)
            {
                options.KnownIPNetworks.Add(rede);
            }
        });

        return services;
    }

    /// <summary>
    /// Redes interpretadas a partir da configuração, junto dos problemas que o operador precisa ver.
    /// </summary>
    /// <param name="Redes">CIDRs válidos, na ordem em que foram declarados.</param>
    /// <param name="Avisos">Mensagens prontas para log. Vazio quando a configuração está sã.</param>
    public sealed record ResultadoDasRedesConhecidas(
        IReadOnlyList<IPNetwork> Redes,
        IReadOnlyList<string> Avisos);

    /// <summary>
    /// Interpreta os CIDRs de <see cref="ForwardedHeadersSettings.KnownNetworks"/> e devolve,
    /// junto, os avisos operacionais encontrados.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Método PÚBLICO e PURO de propósito: é o que permite cobrir os casos de configuração ruim
    /// com teste de unidade, sem subir a aplicação nem inspecionar sink de log. Antes, a
    /// interpretação vivia dentro do lambda de <c>Configure&lt;ForwardedHeadersOptions&gt;</c> e
    /// era, na prática, inalcançável por teste.
    /// </para>
    /// <para>
    /// Só é chamado quando <see cref="ForwardedHeadersSettings.Enabled"/> é <c>true</c> — por isso
    /// o aviso de lista vazia afirma que o processamento está habilitado.
    /// </para>
    /// </remarks>
    public static ResultadoDasRedesConhecidas InterpretarRedesConhecidas(ForwardedHeadersSettings settings)
    {
        var redes = new List<IPNetwork>();
        var avisos = new List<string>();

        foreach (var cidr in settings.KnownNetworks)
        {
            var partes = (cidr ?? string.Empty).Split('/', 2);

            if (partes.Length == 2
                && IPAddress.TryParse(partes[0], out var prefixo)
                && int.TryParse(partes[1], out var tamanho)
                && TamanhoDePrefixoEhValido(prefixo, tamanho))
            {
                redes.Add(new IPNetwork(prefixo, tamanho));
                continue;
            }

            // Entrada inválida NÃO derruba o processo — config ruim não pode impedir o pod de
            // subir —, mas também não passa em silêncio como antes.
            avisos.Add(
                $"CIDR inválido em ForwardedHeaders:KnownNetworks: '{cidr}'. A entrada foi IGNORADA, "
                + "então os headers X-Forwarded-* vindos dessa rede NÃO serão aceitos.");
        }

        if (redes.Count == 0)
        {
            avisos.Add(
                "ForwardedHeaders está HABILITADO com KnownNetworks vazia (ou inteiramente inválida): "
                + "o middleware passa a aceitar X-Forwarded-* de QUALQUER origem. Um cliente pode "
                + "forjar X-Forwarded-For a cada tentativa e escapar do rate limit de login. Declare "
                + "as redes do cluster (ex.: 10.244.0.0/16 e 10.96.0.0/12).");
        }

        return new ResultadoDasRedesConhecidas(redes, avisos);
    }

    /// <summary>Valida o tamanho do prefixo para a família do endereço (IPv4 até 32, IPv6 até 128).</summary>
    /// <remarks>
    /// Sem esta checagem, <c>int.TryParse</c> aceita algo como <c>10.0.0.0/99</c> e o construtor de
    /// <see cref="IPNetwork"/> LANÇA. Como a construção acontecia dentro do lambda de
    /// <c>Configure</c>, o estouro não aparecia no boot: virava erro 500 ao resolver as options na
    /// primeira requisição — o oposto do "config ruim não impede o pod de subir" que o código
    /// pretendia garantir.
    /// </remarks>
    private static bool TamanhoDePrefixoEhValido(IPAddress prefixo, int tamanho)
    {
        var maximo = prefixo.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        return tamanho >= 0 && tamanho <= maximo;
    }
}
