using Fcg.Contracts.Events;
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

public class UsuarioServiceTests
{
    private readonly Mock<IUsuarioRepository> _repositoryMock = new();
    private readonly Mock<IPasswordHasher> _hasherMock = new();
    private readonly Mock<IEventPublisher> _eventPublisherMock = new();
    private readonly UsuarioService _service;

    public UsuarioServiceTests()
    {
        _service = new UsuarioService(_repositoryMock.Object, _hasherMock.Object, _eventPublisherMock.Object);
    }

    [Fact]
    public async Task CriarAsync_DeveRetornarUsuario_QuandoDadosValidos()
    {
        var dto = new CriarUsuarioRequestDto("Felipe", "felipe@email.com", "Senha@123");
        _repositoryMock.Setup(r => r.EmailExisteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _hasherMock.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed_password");

        var result = await _service.CriarAsync(dto);

        result.Nome.Should().Be("Felipe");
        result.Email.Should().Be("felipe@email.com");
        result.Tipo.Should().Be(TipoUsuario.Usuario);
        _repositoryMock.Verify(r => r.AdicionarSemSalvarAsync(It.IsAny<Usuario>(), It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.SalvarAlteracoesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CriarAsync_DevePublicarUserCreatedEvent_QuandoUsuarioCriado()
    {
        var dto = new CriarUsuarioRequestDto("Felipe", "felipe@email.com", "Senha@123");
        _repositoryMock.Setup(r => r.EmailExisteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _hasherMock.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed_password");

        await _service.CriarAsync(dto);

        _eventPublisherMock.Verify(p => p.PublishAsync(
            It.Is<UserCreatedEvent>(e => e.Nome == "Felipe" && e.Email == "felipe@email.com"),
            It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.SalvarAlteracoesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CriarAsync_DeveExecutarFluxoAddPublishSave_EmOrdem()
    {
        var dto = new CriarUsuarioRequestDto("Felipe", "felipe@email.com", "Senha@123");
        _repositoryMock.Setup(r => r.EmailExisteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _hasherMock.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed_password");

        var sequence = new MockSequence();
        _repositoryMock.InSequence(sequence)
            .Setup(r => r.AdicionarSemSalvarAsync(It.IsAny<Usuario>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _eventPublisherMock.InSequence(sequence)
            .Setup(p => p.PublishAsync(It.IsAny<UserCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repositoryMock.InSequence(sequence)
            .Setup(r => r.SalvarAlteracoesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _service.CriarAsync(dto);

        _repositoryMock.Verify(r => r.AdicionarSemSalvarAsync(It.IsAny<Usuario>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisherMock.Verify(p => p.PublishAsync(It.IsAny<UserCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.SalvarAlteracoesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CriarAsync_NaoDevePublicarEvento_QuandoEmailJaExiste()
    {
        var dto = new CriarUsuarioRequestDto("Felipe", "existente@email.com", "Senha@123");
        _repositoryMock.Setup(r => r.EmailExisteAsync("existente@email.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => _service.CriarAsync(dto);

        await act.Should().ThrowAsync<ConflitoDeDadosException>();
        _eventPublisherMock.Verify(p => p.PublishAsync(
            It.IsAny<UserCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.SalvarAlteracoesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CriarAsync_DeveLancarExcecao_QuandoEmailJaExiste()
    {
        var dto = new CriarUsuarioRequestDto("Felipe", "existente@email.com", "Senha@123");
        _repositoryMock.Setup(r => r.EmailExisteAsync("existente@email.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => _service.CriarAsync(dto);

        await act.Should().ThrowAsync<ConflitoDeDadosException>();
    }

    [Fact]
    public async Task ObterPorIdAsync_DeveLancarExcecao_QuandoNaoEncontrado()
    {
        _repositoryMock.Setup(r => r.ObterPorIdAsync("id-inexistente", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Usuario?)null);

        var act = () => _service.ObterPorIdAsync("id-inexistente");

        await act.Should().ThrowAsync<EntidadeNaoEncontradaException>();
    }

    [Fact]
    public async Task RemoverAsync_DeveDesativarUsuario()
    {
        var usuario = new Usuario("Felipe", new Email("felipe@email.com"), "hash");
        _repositoryMock.Setup(r => r.ObterPorIdAsync("id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);

        await _service.RemoverAsync("id");

        usuario.Ativo.Should().BeFalse();
        _repositoryMock.Verify(r => r.AtualizarAsync(usuario, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AtualizarAsync_DeveLancarExcecao_QuandoEmailJaExisteEmOutroUsuario()
    {
        var usuario = new Usuario("Felipe", new Email("felipe@email.com"), "hash");
        _repositoryMock.Setup(r => r.ObterPorIdAsync("id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);
        _repositoryMock.Setup(r => r.EmailExisteAsync("outro@email.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var dto = new AtualizarUsuarioRequestDto("Felipe", "outro@email.com");
        var act = () => _service.AtualizarAsync("id", dto);

        await act.Should().ThrowAsync<ConflitoDeDadosException>();
    }

    [Fact]
    public async Task ObterPorIdAsync_DeveRetornarUsuario_QuandoEncontrado()
    {
        var usuario = new Usuario("Felipe", new Email("felipe@email.com"), "hash");
        _repositoryMock.Setup(r => r.ObterPorIdAsync("id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);

        var result = await _service.ObterPorIdAsync("id");

        result.Nome.Should().Be("Felipe");
        result.Email.Should().Be("felipe@email.com");
    }

    [Fact]
    public async Task ObterPorIdAsync_DeveLancarExcecao_QuandoUsuarioInativo()
    {
        var usuario = new Usuario("Felipe", new Email("felipe@email.com"), "hash");
        usuario.Desativar();

        _repositoryMock.Setup(r => r.ObterPorIdAsync("id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);

        var act = () => _service.ObterPorIdAsync("id");

        await act.Should().ThrowAsync<EntidadeNaoEncontradaException>();
    }

    [Fact]
    public async Task ListarAsync_DeveRetornarPaginacao()
    {
        var usuarios = new List<Usuario>
        {
            new("User 1", new Email("u1@email.com"), "hash"),
            new("User 2", new Email("u2@email.com"), "hash")
        };
        _repositoryMock.Setup(r => r.ObterTodosAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuarios);
        _repositoryMock.Setup(r => r.ContarAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(57);

        var result = await _service.ListarAsync(1, 10);

        result.Itens.Should().HaveCount(2);
        result.Pagina.Should().Be(1);
        result.TamanhoPagina.Should().Be(10);
        result.Total.Should().Be(57);
    }

    [Fact]
    public async Task AtualizarAsync_DeveRetornarUsuarioAtualizado_QuandoDadosValidos()
    {
        var usuario = new Usuario("Felipe", new Email("felipe@email.com"), "hash");
        _repositoryMock.Setup(r => r.ObterPorIdAsync("id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(usuario);
        _repositoryMock.Setup(r => r.EmailExisteAsync("novo@email.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var dto = new AtualizarUsuarioRequestDto("Novo Nome", "novo@email.com");
        var result = await _service.AtualizarAsync("id", dto);

        result.Nome.Should().Be("Novo Nome");
        result.Email.Should().Be("novo@email.com");
        _repositoryMock.Verify(r => r.AtualizarAsync(usuario, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AtualizarAsync_DeveLancarExcecao_QuandoUsuarioNaoEncontrado()
    {
        _repositoryMock.Setup(r => r.ObterPorIdAsync("id", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Usuario?)null);

        var dto = new AtualizarUsuarioRequestDto("Nome", "email@test.com");
        var act = () => _service.AtualizarAsync("id", dto);

        await act.Should().ThrowAsync<EntidadeNaoEncontradaException>();
    }

    [Fact]
    public async Task RemoverAsync_DeveLancarExcecao_QuandoUsuarioNaoEncontrado()
    {
        _repositoryMock.Setup(r => r.ObterPorIdAsync("id", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Usuario?)null);

        var act = () => _service.RemoverAsync("id");

        await act.Should().ThrowAsync<EntidadeNaoEncontradaException>();
    }
}
