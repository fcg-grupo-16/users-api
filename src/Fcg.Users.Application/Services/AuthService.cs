using Fcg.Users.Application.DTOs.Request;
using Fcg.Users.Application.DTOs.Response;
using Fcg.Users.Application.Interfaces;
using Fcg.Users.Domain.Exceptions;
using Fcg.Users.Domain.Repositories;

namespace Fcg.Users.Application.Services;

public sealed class AuthService(
    IUsuarioRepository usuarioRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService) : IAuthService
{
    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto dto, CancellationToken ct = default)
    {
        var usuario = await usuarioRepository.ObterPorEmailAsync(dto.Email.Trim().ToLowerInvariant(), ct);

        if (usuario is null || !passwordHasher.Verify(dto.Senha, usuario.SenhaHash))
        {
            throw new CredenciaisInvalidasException();
        }

        if (!usuario.Ativo)
        {
            throw new CredenciaisInvalidasException("Conta desativada. Entre em contato com o suporte.");
        }

        var token = tokenService.GerarToken(usuario);
        var expiracao = tokenService.ObterExpiracao();

        return new LoginResponseDto(token, expiracao, usuario.Tipo);
    }
}
