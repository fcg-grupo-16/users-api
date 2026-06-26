using Fcg.Users.Application.DTOs.Request;
using Fcg.Users.Application.DTOs.Response;

namespace Fcg.Users.Application.Services;

public interface IUsuarioService
{
    Task<UsuarioResponseDto> CriarAsync(CriarUsuarioRequestDto dto, CancellationToken ct = default);
    Task<UsuarioResponseDto> ObterPorIdAsync(string id, CancellationToken ct = default);
    Task<PaginacaoResponseDto<UsuarioResponseDto>> ListarAsync(int pagina, int tamanhoPagina, CancellationToken ct = default);
    Task<UsuarioResponseDto> AtualizarAsync(string id, AtualizarUsuarioRequestDto dto, CancellationToken ct = default);
    Task RemoverAsync(string id, CancellationToken ct = default);
}
