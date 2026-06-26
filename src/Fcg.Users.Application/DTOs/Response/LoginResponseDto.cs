using Fcg.Users.Domain.Enums;

namespace Fcg.Users.Application.DTOs.Response;

public sealed record LoginResponseDto(
    string Token,
    DateTime Expiracao,
    TipoUsuario Tipo);
