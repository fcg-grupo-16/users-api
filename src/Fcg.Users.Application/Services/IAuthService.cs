using Fcg.Users.Application.DTOs.Request;
using Fcg.Users.Application.DTOs.Response;

namespace Fcg.Users.Application.Services;

public interface IAuthService
{
    Task<TokenResponseDto> LoginAsync(LoginRequestDto dto, CancellationToken ct = default);
    Task<TokenResponseDto> RefreshAsync(RefreshTokenRequestDto dto, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
}
