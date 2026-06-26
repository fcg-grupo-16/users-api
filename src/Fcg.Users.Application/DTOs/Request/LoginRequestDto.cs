namespace Fcg.Users.Application.DTOs.Request;

public sealed record LoginRequestDto(
    string Email,
    string Senha);
