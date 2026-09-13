using Fcg.Users.Api.Extensions;
using Fcg.Users.Infrastructure.Settings;
using FluentAssertions;

namespace Fcg.Users.IntegrationTests;

/// <summary>
/// Testes da interpretação de <c>ForwardedHeaders:KnownNetworks</c> (issue #25).
/// </summary>
/// <remarks>
/// Classe deliberadamente <b>sem</b> <c>[Collection]</c>: estes testes são puros, não usam a
/// <c>FcgWebAppFactory</c> e portanto não pagam o custo dos containers. Vivem neste projeto porque
/// é o único que referencia <c>Fcg.Users.Api</c>, onde a extensão mora — o <c>Fcg.Users.UnitTests</c>
/// referencia apenas Domain e Application.
/// </remarks>
public sealed class ForwardedHeadersConfiguracaoTests
{
    private static ForwardedHeadersSettings ComRedes(params string[] redes) =>
        new() { Enabled = true, KnownNetworks = redes };

    [Fact(DisplayName = "Configuração sã interpreta os CIDRs do cluster e não gera aviso")]
    public void ConfiguracaoSa_InterpretaTudoESemAviso()
    {
        var resultado = ForwardedHeadersExtensions.InterpretarRedesConhecidas(
            ComRedes("10.244.0.0/16", "10.96.0.0/12"));

        resultado.Redes.Should().HaveCount(2);
        resultado.Avisos.Should().BeEmpty();
    }

    [Fact(DisplayName = "KnownNetworks vazia avisa que passa a confiar em qualquer origem")]
    public void ListaVazia_AvisaQueConfiaEmQualquerOrigem()
    {
        var resultado = ForwardedHeadersExtensions.InterpretarRedesConhecidas(ComRedes());

        resultado.Redes.Should().BeEmpty();
        // O aviso tem de nomear a CONSEQUÊNCIA, não só dizer "lista vazia": é o que faz alguém
        // agir ao ver a linha no log.
        resultado.Avisos.Should().ContainSingle()
            .Which.Should().Contain("QUALQUER origem").And.Contain("rate limit");
    }

    [Fact(DisplayName = "CIDR malformado é nomeado no aviso e não invalida as entradas válidas")]
    public void CidrMalformado_AvisaSemDescartarOsValidos()
    {
        var resultado = ForwardedHeadersExtensions.InterpretarRedesConhecidas(
            ComRedes("10.244.0.0/16", "nao-e-cidr", "10.96.0.0/12"));

        resultado.Redes.Should().HaveCount(2, "as duas entradas válidas continuam valendo");
        resultado.Avisos.Should().ContainSingle()
            .Which.Should().Contain("nao-e-cidr", "o valor recusado precisa aparecer no log");
    }

    [Theory(DisplayName = "Prefixo fora da faixa gera aviso em vez de lançar")]
    [InlineData("10.0.0.0/99")]
    [InlineData("10.0.0.0/-1")]
    [InlineData("::1/129")]
    public void PrefixoForaDaFaixa_AvisaEmVezDeLancar(string cidr)
    {
        // Regressão: antes, int.TryParse aceitava o valor e o construtor de IPNetwork LANÇAVA.
        // Como a construção acontecia dentro do lambda de Configure, isso virava erro 500 ao
        // resolver as options na primeira requisição — não um erro visível no boot.
        var act = () => ForwardedHeadersExtensions.InterpretarRedesConhecidas(ComRedes(cidr));

        act.Should().NotThrow();

        var resultado = act();
        resultado.Redes.Should().BeEmpty();
        resultado.Avisos.Should().Contain(a => a.Contains(cidr));
    }

    [Fact(DisplayName = "Quando toda entrada é inválida, avisa o CIDR e também a confiança irrestrita")]
    public void TodasInvalidas_AvisaAsDuasCoisas()
    {
        var resultado = ForwardedHeadersExtensions.InterpretarRedesConhecidas(ComRedes("xxx"));

        resultado.Redes.Should().BeEmpty();
        resultado.Avisos.Should().HaveCount(2);
        resultado.Avisos.Should().Contain(a => a.Contains("xxx"));
        resultado.Avisos.Should().Contain(a => a.Contains("QUALQUER origem"));
    }
}
