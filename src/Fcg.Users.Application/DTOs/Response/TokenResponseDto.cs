using Fcg.Users.Domain.Enums;

namespace Fcg.Users.Application.DTOs.Response;

public sealed record TokenResponseDto(
    string Token,
    DateTime Expiracao,
    TipoUsuario Tipo,
    string RefreshToken,
    DateTime RefreshTokenExpiracao);
