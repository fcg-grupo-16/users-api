using Fcg.Users.Application.DTOs.Request;
using Fcg.Users.Application.DTOs.Response;

namespace Fcg.Users.Application.Services;

public interface IAuthService
{
    Task<LoginResponseDto> LoginAsync(LoginRequestDto dto, CancellationToken ct = default);
}
