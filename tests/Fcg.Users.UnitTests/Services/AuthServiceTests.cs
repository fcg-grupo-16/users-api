using Fcg.Users.Application.DTOs.Request;
using Fcg.Users.Application.Interfaces;
using Fcg.Users.Application.Services;
using Fcg.Users.Domain.Entities;
using Fcg.Users.Domain.Enums;
using Fcg.Users.Domain.Exceptions;
using Fcg.Users.Domain.Repositories;
using Fcg.Users.Domain.ValueObjects;
using FluentAssertions;
using Moq;

namespace Fcg.Users.UnitTests.Services;

public class AuthServiceTests
{
    private readonly Mock<IUsuarioRepository> _repositoryMock = new();
    private readonly Mock<IPasswordHasher> _hasherMock = new();
    private readonly Mock<ITokenService> _tokenMock = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock = new();
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _service = new AuthService(
            _repositoryMock.Object,
            _hasherMock.Object,
            _tokenMock.Object,
            _refreshTokenRepositoryMock.Object);
    }

    private static Usuario CriarUsuario() =>
        new("Felipe", new Email("felipe@email.com"), "$2a$12$hash", TipoUsuario.Usuario);

    [Fact]
    public async Task LoginAsync_DeveRetornarToken_QuandoCredenciaisValidas()
    {
        var usuario = CriarUsuario();
        var dto = new LoginRequestDto("felipe@email.com", "Senha@123");
        var expiracao = DateTime.UtcNow.AddMinutes(15);
        var refreshExpiracao = DateTime.UtcNow.AddDays(7);

        _repositoryMock.Setup(r => r.ObterPorEmailAsync("felipe@email.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);
        _hasherMock.Setup(h => h.Verify("Senha@123", usuario.SenhaHash)).Returns(true);
        _tokenMock.Setup(t => t.GerarToken(usuario)).Returns("jwt-token");
        _tokenMock.Setup(t => t.ObterExpiracao()).Returns(expiracao);
        _tokenMock.Setup(t => t.GerarRefreshToken()).Returns("refresh-token");
        _tokenMock.Setup(t => t.ObterExpiracaoRefreshToken()).Returns(refreshExpiracao);

        var result = await _service.LoginAsync(dto);

        result.Token.Should().Be("jwt-token");
        result.Expiracao.Should().Be(expiracao);
        result.Tipo.Should().Be(TipoUsuario.Usuario);
        result.RefreshToken.Should().Be("refresh-token");
        result.RefreshTokenExpiracao.Should().Be(refreshExpiracao);
        _refreshTokenRepositoryMock.Verify(r => r.CriarAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_DeveLancarExcecao_QuandoEmailNaoEncontrado()
    {
        var dto = new LoginRequestDto("inexistente@email.com", "Senha@123");

        _repositoryMock.Setup(r => r.ObterPorEmailAsync("inexistente@email.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Usuario?)null);

        var act = () => _service.LoginAsync(dto);

        await act.Should().ThrowAsync<CredenciaisInvalidasException>();
    }

    [Fact]
    public async Task LoginAsync_DeveLancarExcecao_QuandoSenhaInvalida()
    {
        var usuario = CriarUsuario();
        var dto = new LoginRequestDto("felipe@email.com", "SenhaErrada@1");

        _repositoryMock.Setup(r => r.ObterPorEmailAsync("felipe@email.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);
        _hasherMock.Setup(h => h.Verify("SenhaErrada@1", usuario.SenhaHash)).Returns(false);

        var act = () => _service.LoginAsync(dto);

        await act.Should().ThrowAsync<CredenciaisInvalidasException>();
    }

    [Fact]
    public async Task LoginAsync_DeveLancarExcecao_QuandoUsuarioInativo()
    {
        var usuario = CriarUsuario();
        usuario.Desativar();
        var dto = new LoginRequestDto("felipe@email.com", "Senha@123");

        _repositoryMock.Setup(r => r.ObterPorEmailAsync("felipe@email.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);
        _hasherMock.Setup(h => h.Verify("Senha@123", usuario.SenhaHash)).Returns(true);

        var act = () => _service.LoginAsync(dto);

        await act.Should().ThrowAsync<CredenciaisInvalidasException>()
            .WithMessage("*desativada*");
    }

    [Fact]
    public async Task LoginAsync_MesmaExcecaoParaEmailInexistenteESenhaErrada()
    {
        // Garante que não há enumeração de emails
        var dtoEmailErrado = new LoginRequestDto("nao@existe.com", "Senha@123");
        _repositoryMock.Setup(r => r.ObterPorEmailAsync("nao@existe.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Usuario?)null);

        var actEmail = () => _service.LoginAsync(dtoEmailErrado);
        var exEmail = await actEmail.Should().ThrowAsync<CredenciaisInvalidasException>();

        var usuario = CriarUsuario();
        var dtoSenhaErrada = new LoginRequestDto("felipe@email.com", "Errada@123");
        _repositoryMock.Setup(r => r.ObterPorEmailAsync("felipe@email.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);
        _hasherMock.Setup(h => h.Verify("Errada@123", usuario.SenhaHash)).Returns(false);

        var actSenha = () => _service.LoginAsync(dtoSenhaErrada);
        var exSenha = await actSenha.Should().ThrowAsync<CredenciaisInvalidasException>();

        // Mesma mensagem para ambos — evita enumeração
        exEmail.Which.Message.Should().Be(exSenha.Which.Message);
    }

    [Fact]
    public async Task RefreshAsync_DeveRotacionarRefreshToken_QuandoTokenValido()
    {
        var usuario = CriarUsuario();
        var dto = new RefreshTokenRequestDto("refresh-token-atual");
        var refreshAtual = new RefreshToken(usuario.Id, "HASH_ANTIGO", DateTime.UtcNow.AddDays(1));
        var expiracao = DateTime.UtcNow.AddMinutes(15);
        var refreshExpiracao = DateTime.UtcNow.AddDays(7);

        _refreshTokenRepositoryMock
            .Setup(r => r.ObterPorTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(refreshAtual);
        _repositoryMock.Setup(r => r.ObterPorIdAsync(usuario.Id, It.IsAny<CancellationToken>())).ReturnsAsync(usuario);
        _tokenMock.Setup(t => t.GerarToken(usuario)).Returns("novo-jwt");
        _tokenMock.Setup(t => t.ObterExpiracao()).Returns(expiracao);
        _tokenMock.Setup(t => t.GerarRefreshToken()).Returns("novo-refresh");
        _tokenMock.Setup(t => t.ObterExpiracaoRefreshToken()).Returns(refreshExpiracao);

        var result = await _service.RefreshAsync(dto);

        result.Token.Should().Be("novo-jwt");
        result.RefreshToken.Should().Be("novo-refresh");
        result.Expiracao.Should().Be(expiracao);
        result.RefreshTokenExpiracao.Should().Be(refreshExpiracao);
        _refreshTokenRepositoryMock.Verify(r => r.RevogarAsync(refreshAtual, It.IsAny<CancellationToken>()), Times.Once);
        _refreshTokenRepositoryMock.Verify(r => r.CriarAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_DeveLancarExcecao_QuandoTokenRevogadoOuExpirado()
    {
        var usuario = CriarUsuario();
        var dto = new RefreshTokenRequestDto("refresh-token-revogado");
        var refreshRevogado = new RefreshToken(usuario.Id, "HASH_REVOGADO", DateTime.UtcNow.AddDays(1));
        refreshRevogado.Revogar();

        _refreshTokenRepositoryMock
            .Setup(r => r.ObterPorTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(refreshRevogado);

        var act = () => _service.RefreshAsync(dto);

        await act.Should().ThrowAsync<CredenciaisInvalidasException>();
        _refreshTokenRepositoryMock.Verify(r => r.RevogarAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogoutAsync_DeveRevogarRefreshToken_QuandoAtivo()
    {
        var usuario = CriarUsuario();
        var refresh = new RefreshToken(usuario.Id, "HASH_LOGOUT", DateTime.UtcNow.AddDays(1));

        _refreshTokenRepositoryMock
            .Setup(r => r.ObterPorTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(refresh);

        await _service.LogoutAsync("refresh-token");

        _refreshTokenRepositoryMock.Verify(r => r.RevogarAsync(refresh, It.IsAny<CancellationToken>()), Times.Once);
    }
}
