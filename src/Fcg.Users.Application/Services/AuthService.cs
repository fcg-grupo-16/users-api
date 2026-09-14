using System.Security.Cryptography;
using System.Text;
using Fcg.Users.Application.DTOs.Request;
using Fcg.Users.Application.DTOs.Response;
using Fcg.Users.Application.Interfaces;
using Fcg.Users.Domain.Entities;
using Fcg.Users.Domain.Exceptions;
using Fcg.Users.Domain.Repositories;
using Fcg.Users.Domain.ValueObjects;

namespace Fcg.Users.Application.Services;

public sealed class AuthService(
    IUsuarioRepository usuarioRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IRefreshTokenRepository refreshTokenRepository) : IAuthService
{
    public async Task<TokenResponseDto> LoginAsync(LoginRequestDto dto, CancellationToken ct = default)
    {
        // FORMATO INVÁLIDO É CREDENCIAL INVÁLIDA, e a validação acontece AQUI — antes do repositório.
        //
        // O validador do DTO usa `.EmailAddress()` do FluentValidation, que é mais permissivo que o
        // regex do value object: `a@b` (sem TLD) e `x y@fcg.com` (com espaço) passam por ele e
        // reprovam no `Email`. Antes, essa string crua descia até o repositório e o `new Email(...)`
        // lançava DENTRO da expressão LINQ; o EF embrulhava a ValidacaoException num
        // InvalidOperationException, o middleware não reconhecia o tipo externo e devolvia **500**.
        //
        // Devolver CredenciaisInvalidasException mantém a resposta INDISTINGUÍVEL de um e-mail
        // inexistente — é o mesmo cuidado contra enumeração que
        // LoginAsync_MesmaExcecaoParaEmailInexistenteESenhaErrada já protege — e tira o 5xx da taxa
        // de erro do serviço, que é o sinal usado para alarme (issue #28).
        Email email;

        try
        {
            email = new Email(dto.Email);
        }
        catch (ValidacaoException)
        {
            throw new CredenciaisInvalidasException();
        }

        // O construtor de Email já faz Trim + ToLowerInvariant; normalizar de novo aqui seria
        // duplicar a regra em dois lugares.
        var usuario = await usuarioRepository.ObterPorEmailAsync(email.Endereco, ct);

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
