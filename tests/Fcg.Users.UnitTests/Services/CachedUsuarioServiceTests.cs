using Fcg.Users.Application.DTOs.Request;
using Fcg.Users.Application.DTOs.Response;
using Fcg.Users.Application.Interfaces;
using Fcg.Users.Application.Services;
using Fcg.Users.Domain.Enums;
using FluentAssertions;
using Moq;

namespace Fcg.Users.UnitTests.Services;

public sealed class CachedUsuarioServiceTests
{
    private readonly Mock<IUsuarioService> _innerMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly CachedUsuarioService _service;

    public CachedUsuarioServiceTests()
    {
        _service = new CachedUsuarioService(_innerMock.Object, _cacheMock.Object);
    }

    [Fact]
    public async Task ObterPorId_QuandoCacheHit_NaoChamaOInner()
    {
        var cached = CriarUsuarioResponse();
        _cacheMock.Setup(c => c.GetAsync<UsuarioResponseDto>("usuario:id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var result = await _service.ObterPorIdAsync("id");

        result.Should().BeSameAs(cached);
        _innerMock.Verify(s => s.ObterPorIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ObterPorId_QuandoCacheMiss_ChamaOInnerEGrava()
    {
        var result = CriarUsuarioResponse();
        _innerMock.Setup(s => s.ObterPorIdAsync("id", It.IsAny<CancellationToken>())).ReturnsAsync(result);

        var response = await _service.ObterPorIdAsync("id");

        response.Should().BeSameAs(result);
        _cacheMock.Verify(c => c.SetAsync("usuario:id", result, It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ObterPorId_QuandoInnerLanca_NaoGravaNoCache()
    {
        _innerMock.Setup(s => s.ObterPorIdAsync("id", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("falha"));

        var act = () => _service.ObterPorIdAsync("id");

        await act.Should().ThrowAsync<InvalidOperationException>();
        _cacheMock.Verify(c => c.SetAsync<UsuarioResponseDto>(It.IsAny<string>(), It.IsAny<UsuarioResponseDto>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Listar_MontaChaveComAGeracaoAtual()
    {
        var result = CriarPagina();
        _cacheMock.Setup(c => c.GetGenerationAsync("usuarios", It.IsAny<CancellationToken>())).ReturnsAsync(7);
        _innerMock.Setup(s => s.ListarAsync(2, 20, It.IsAny<CancellationToken>())).ReturnsAsync(result);

        await _service.ListarAsync(2, 20);

        _cacheMock.Verify(c => c.GetAsync<PaginacaoResponseDto<UsuarioResponseDto>>("usuarios:lista:g7:p2:t20", It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.SetAsync("usuarios:lista:g7:p2:t20", result, It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Atualizar_InvalidaOItemEOGrupo()
    {
        var result = CriarUsuarioResponse();
        _innerMock.Setup(s => s.AtualizarAsync("id", It.IsAny<AtualizarUsuarioRequestDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(result);

        await _service.AtualizarAsync("id", new AtualizarUsuarioRequestDto("Nome", "email@teste.com"));

        _cacheMock.Verify(c => c.RemoveAsync("usuario:id", It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.InvalidateGroupAsync("usuarios", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Criar_InvalidaApenasOGrupo()
    {
        var result = CriarUsuarioResponse();
        _innerMock.Setup(s => s.CriarAsync(It.IsAny<CriarUsuarioRequestDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(result);

        await _service.CriarAsync(new CriarUsuarioRequestDto("Nome", "email@teste.com", "Senha@123"));

        _cacheMock.Verify(c => c.InvalidateGroupAsync("usuarios", It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Remover_InvalidaOItemEOGrupo()
    {
        _innerMock.Setup(s => s.RemoverAsync("id", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _service.RemoverAsync("id");

        _cacheMock.Verify(c => c.RemoveAsync("usuario:id", It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.InvalidateGroupAsync("usuarios", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_QuandoCacheLanca_CaiNoInner()
    {
        var result = CriarUsuarioResponse();
        _cacheMock.Setup(c => c.GetAsync<UsuarioResponseDto>("usuario:id", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis indisponivel"));
        _innerMock.Setup(s => s.ObterPorIdAsync("id", It.IsAny<CancellationToken>())).ReturnsAsync(result);

        var response = await _service.ObterPorIdAsync("id");

        response.Should().BeSameAs(result);
        _innerMock.Verify(s => s.ObterPorIdAsync("id", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static UsuarioResponseDto CriarUsuarioResponse() =>
        new("id", "Nome", "email@teste.com", TipoUsuario.Usuario, DateTime.UtcNow, true);

    private static PaginacaoResponseDto<UsuarioResponseDto> CriarPagina() =>
        new([CriarUsuarioResponse()], 2, 20, 1);
}
