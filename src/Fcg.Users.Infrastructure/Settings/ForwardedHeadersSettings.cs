namespace Fcg.Users.Infrastructure.Settings;

/// <summary>
/// Configuração da confiança em headers <c>X-Forwarded-*</c> (Fase 3, tráfego via API Gateway).
/// </summary>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// Liga o processamento dos headers. <c>false</c> por padrão: só faz sentido quando existe um
    /// proxy reverso na frente. Ligado sem proxy, o serviço passaria a confiar num header que o
    /// cliente controla.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Redes (CIDR) das quais os headers são aceitos. Devem ser as redes do cluster onde o Kong
    /// roda — ex.: <c>10.244.0.0/16</c> (pods do minikube) e <c>10.96.0.0/12</c> (Services).
    /// Lista vazia = confia em qualquer origem: aceitável apenas em ambiente fechado de demo, e
    /// registrado no log como aviso no startup.
    /// </summary>
    public string[] KnownNetworks { get; init; } = [];

    /// <summary>
    /// Quantos saltos de proxy considerar. Temos exatamente UM (o Kong), então 1. Valor maior que o
    /// número real de proxies permite ao cliente injetar entradas extras em X-Forwarded-For e se
    /// passar por outro IP.
    /// </summary>
    public int ForwardLimit { get; init; } = 1;
}
