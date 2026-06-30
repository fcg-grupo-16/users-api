using System.Security.Cryptography;
using System.Text;
using Fcg.Users.Application.DTOs.Request;
using Fcg.Users.Application.DTOs.Response;
using Fcg.Users.Application.Interfaces;
using Fcg.Users.Domain.Entities;
using Fcg.Users.Domain.Exceptions;
using Fcg.Users.Domain.Repositories;

namespace Fcg.Users.Application.Services;

public sealed class AuthService(
    IUsuarioRepository usuarioRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IRefreshTokenRepository refreshTokenRepository) : IAuthService
{
    public async Task<TokenResponseDto> LoginAsync(LoginRequestDto dto, CancellationToken ct = default)
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

        return await GerarCredenciaisAsync(usuario, ct);
    }

    public async Task<TokenResponseDto> RefreshAsync(RefreshTokenRequestDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.RefreshToken))
        {
            throw new CredenciaisInvalidasException();
        }

        var refreshTokenHash = CalcularHash(dto.RefreshToken);
        var refreshTokenAtual = await refreshTokenRepository.ObterPorTokenHashAsync(refreshTokenHash, ct);

        if (refreshTokenAtual is null || !refreshTokenAtual.Ativo)
        {
            throw new CredenciaisInvalidasException();
        }

        var usuario = await usuarioRepository.ObterPorIdAsync(refreshTokenAtual.UsuarioId, ct);

        if (usuario is null || !usuario.Ativo)
        {
            throw new CredenciaisInvalidasException();
        }

        refreshTokenAtual.Revogar();
        await refreshTokenRepository.RevogarAsync(refreshTokenAtual, ct);

        return await GerarCredenciaisAsync(usuario, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var refreshTokenHash = CalcularHash(refreshToken);
        var refreshTokenAtual = await refreshTokenRepository.ObterPorTokenHashAsync(refreshTokenHash, ct);

        if (refreshTokenAtual is null || !refreshTokenAtual.Ativo)
        {
            return;
        }

        refreshTokenAtual.Revogar();
        await refreshTokenRepository.RevogarAsync(refreshTokenAtual, ct);
    }

    private async Task<TokenResponseDto> GerarCredenciaisAsync(Usuario usuario, CancellationToken ct)
    {
        var accessToken = tokenService.GerarToken(usuario);
        var accessTokenExpiracao = tokenService.ObterExpiracao();
        var refreshToken = tokenService.GerarRefreshToken();
        var refreshTokenExpiracao = tokenService.ObterExpiracaoRefreshToken();

        var refreshTokenEntity = new RefreshToken(
            usuario.Id,
            CalcularHash(refreshToken),
            refreshTokenExpiracao);

        await refreshTokenRepository.CriarAsync(refreshTokenEntity, ct);

        return new TokenResponseDto(
            accessToken,
            accessTokenExpiracao,
            usuario.Tipo,
            refreshToken,
            refreshTokenExpiracao);
    }

    private static string CalcularHash(string valor)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(valor));
        return Convert.ToHexString(bytes);
    }
}
