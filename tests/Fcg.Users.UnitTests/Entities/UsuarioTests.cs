using Fcg.Users.Domain.Entities;
using Fcg.Users.Domain.Enums;
using Fcg.Users.Domain.Exceptions;
using Fcg.Users.Domain.ValueObjects;
using FluentAssertions;

namespace Fcg.Users.UnitTests.Entities;

public class UsuarioTests
{
    private static readonly Email EmailValido = new("teste@email.com");
    private const string SenhaHashValida = "$2a$12$hashficticio";

    [Fact]
    public void DeveCriarUsuario_QuandoDadosValidos()
    {
        var usuario = new Usuario("Felipe", EmailValido, SenhaHashValida);

        usuario.Nome.Should().Be("Felipe");
        usuario.Email.Should().Be(EmailValido);
        usuario.SenhaHash.Should().Be(SenhaHashValida);
        usuario.Tipo.Should().Be(TipoUsuario.Usuario);
        usuario.Ativo.Should().BeTrue();
        usuario.DataCriacao.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void DeveCriarUsuarioAdministrador()
    {
        var usuario = new Usuario("Admin", EmailValido, SenhaHashValida, TipoUsuario.Administrador);

        usuario.Tipo.Should().Be(TipoUsuario.Administrador);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void DeveLancarExcecao_QuandoNomeVazioOuNulo(string? nome)
    {
        var act = () => new Usuario(nome!, EmailValido, SenhaHashValida);

        act.Should().Throw<ValidacaoException>()
            .WithMessage("O nome do usuário é obrigatório.");
    }

    [Fact]
    public void DeveLancarExcecao_QuandoEmailNulo()
    {
        var act = () => new Usuario("Felipe", null!, SenhaHashValida);

        act.Should().Throw<ValidacaoException>()
            .WithMessage("O e-mail é obrigatório.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void DeveLancarExcecao_QuandoSenhaHashVaziaOuNula(string? senhaHash)
    {
        var act = () => new Usuario("Felipe", EmailValido, senhaHash!);

        act.Should().Throw<ValidacaoException>()
            .WithMessage("O hash da senha é obrigatório.");
    }

    [Fact]
    public void DeveAtualizarNome()
    {
        var usuario = new Usuario("Felipe", EmailValido, SenhaHashValida);

        usuario.AtualizarNome("Novo Nome");

        usuario.Nome.Should().Be("Novo Nome");
    }

    [Fact]
    public void DeveDesativarEAtivar()
    {
        var usuario = new Usuario("Felipe", EmailValido, SenhaHashValida);

        usuario.Desativar();
        usuario.Ativo.Should().BeFalse();

        usuario.Ativar();
        usuario.Ativo.Should().BeTrue();
    }

    [Fact]
    public void DeveAtualizarEmail()
    {
        var usuario = new Usuario("Felipe", EmailValido, SenhaHashValida);
        var novoEmail = new Email("novo@email.com");

        usuario.AtualizarEmail(novoEmail);

        usuario.Email.Should().Be(novoEmail);
    }

    [Fact]
    public void DeveLancarExcecao_QuandoAtualizarEmailNulo()
    {
        var usuario = new Usuario("Felipe", EmailValido, SenhaHashValida);

        var act = () => usuario.AtualizarEmail(null!);

        act.Should().Throw<ValidacaoException>()
            .WithMessage("O e-mail é obrigatório.");
    }

    [Fact]
    public void DeveAtualizarSenha()
    {
        var usuario = new Usuario("Felipe", EmailValido, SenhaHashValida);

        usuario.AtualizarSenha("$2a$12$novohash");

        usuario.SenhaHash.Should().Be("$2a$12$novohash");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void DeveLancarExcecao_QuandoAtualizarSenhaVaziaOuNula(string? senhaHash)
    {
        var usuario = new Usuario("Felipe", EmailValido, SenhaHashValida);

        var act = () => usuario.AtualizarSenha(senhaHash!);

        act.Should().Throw<ValidacaoException>()
            .WithMessage("O hash da senha é obrigatório.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void DeveLancarExcecao_QuandoAtualizarNomeVazioOuNulo(string? nome)
    {
        var usuario = new Usuario("Felipe", EmailValido, SenhaHashValida);

        var act = () => usuario.AtualizarNome(nome!);

        act.Should().Throw<ValidacaoException>()
            .WithMessage("O nome do usuário é obrigatório.");
    }
}
