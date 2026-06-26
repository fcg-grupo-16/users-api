using Fcg.Users.Domain.Exceptions;
using Fcg.Users.Domain.ValueObjects;
using FluentAssertions;

namespace Fcg.Users.UnitTests.ValueObjects;

public class SenhaTests
{
    [Theory]
    [InlineData("Senha@123")]
    [InlineData("F0rte!Senha")]
    [InlineData("C0mpl3x@Pass")]
    public void DeveCriarSenha_QuandoComplexidadeValida(string valor)
    {
        var senha = new Senha(valor);

        senha.Valor.Should().Be(valor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void DeveLancarExcecao_QuandoSenhaVaziaOuNula(string? valor)
    {
        var act = () => new Senha(valor!);

        act.Should().Throw<ValidacaoException>()
            .WithMessage("A senha é obrigatória.");
    }

    [Fact]
    public void DeveLancarExcecao_QuandoMenorQue8Caracteres()
    {
        var act = () => new Senha("Ab1@");

        act.Should().Throw<ValidacaoException>();
    }

    [Fact]
    public void DeveLancarExcecao_QuandoSemLetraMaiuscula()
    {
        var act = () => new Senha("senha@123");

        act.Should().Throw<ValidacaoException>();
    }

    [Fact]
    public void DeveLancarExcecao_QuandoSemLetraMinuscula()
    {
        var act = () => new Senha("SENHA@123");

        act.Should().Throw<ValidacaoException>();
    }

    [Fact]
    public void DeveLancarExcecao_QuandoSemNumero()
    {
        var act = () => new Senha("Senha@abc");

        act.Should().Throw<ValidacaoException>();
    }

    [Fact]
    public void DeveLancarExcecao_QuandoSemCaractereEspecial()
    {
        var act = () => new Senha("Senha1234");

        act.Should().Throw<ValidacaoException>();
    }

    [Fact]
    public void ToStringDeveOcultarValor()
    {
        var senha = new Senha("Senha@123");

        senha.ToString().Should().Be("********");
    }
}
