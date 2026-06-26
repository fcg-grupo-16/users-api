using Fcg.Users.Domain.Exceptions;
using Fcg.Users.Domain.ValueObjects;
using FluentAssertions;

namespace Fcg.Users.UnitTests.ValueObjects;

public class EmailTests
{
    [Theory]
    [InlineData("usuario@email.com")]
    [InlineData("TESTE@DOMINIO.COM.BR")]
    [InlineData("nome.sobrenome@empresa.co")]
    public void DeveCriarEmail_QuandoFormatoValido(string endereco)
    {
        var email = new Email(endereco);

        email.Endereco.Should().Be(endereco.Trim().ToLowerInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void DeveLancarExcecao_QuandoEmailVazioOuNulo(string? endereco)
    {
        var act = () => new Email(endereco!);

        act.Should().Throw<ValidacaoException>()
            .WithMessage("O e-mail é obrigatório.");
    }

    [Theory]
    [InlineData("semdominio")]
    [InlineData("sem@")]
    [InlineData("@semlocal.com")]
    [InlineData("espacos no meio@email.com")]
    [InlineData("falta.tld@dominio")]
    public void DeveLancarExcecao_QuandoFormatoInvalido(string endereco)
    {
        var act = () => new Email(endereco);

        act.Should().Throw<ValidacaoException>()
            .WithMessage("O formato do e-mail é inválido.");
    }

    [Fact]
    public void DeveNormalizarParaMinusculo()
    {
        var email = new Email("Teste@Email.COM");

        email.Endereco.Should().Be("teste@email.com");
    }

    [Fact]
    public void DeveSerIgualQuandoMesmoEndereco()
    {
        var email1 = new Email("teste@email.com");
        var email2 = new Email("TESTE@EMAIL.COM");

        email1.Should().Be(email2);
    }
}
