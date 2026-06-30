using Fcg.Users.Api.Extensions;
using Fcg.Users.Application.DTOs.Request;
using Fcg.Users.Application.DTOs.Response;
using Fcg.Users.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fcg.Users.Api.Controllers;

/// <summary>
/// Controller responsável pela autenticação de usuários.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(
    IAuthService authService,
    IValidator<LoginRequestDto> loginValidator,
    IValidator<RefreshTokenRequestDto> refreshTokenValidator) : ControllerBase
{
    /// <summary>
    /// Autenticar usuário e obter token JWT.
    /// </summary>
    /// <param name="dto">Credenciais de login.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Token JWT com data de expiração.</returns>
    /// <response code="200">Login realizado com sucesso.</response>
    /// <response code="401">Credenciais inválidas.</response>
    /// <response code="422">Dados de validação inválidos.</response>
    [HttpPost("login")]
    [ProducesResponseType(typeof(TokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto, CancellationToken ct)
    {
        await loginValidator.ValidarAsync(dto, ct);

        var resultado = await authService.LoginAsync(dto, ct);

        return Ok(resultado);
    }

    /// <summary>
    /// Renovar o par de tokens usando um refresh token válido.
    /// </summary>
    /// <param name="dto">Refresh token atual.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Novo par de access token e refresh token.</returns>
    /// <response code="200">Refresh realizado com sucesso.</response>
    /// <response code="401">Refresh token inválido, expirado ou revogado.</response>
    /// <response code="422">Dados de validação inválidos.</response>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(TokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto dto, CancellationToken ct)
    {
        await refreshTokenValidator.ValidarAsync(dto, ct);

        var resultado = await authService.RefreshAsync(dto, ct);

        return Ok(resultado);
    }

    /// <summary>
    /// Revogar um refresh token da sessão autenticada.
    /// </summary>
    /// <param name="dto">Refresh token a ser revogado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Sem conteúdo.</returns>
    /// <response code="204">Logout realizado com sucesso.</response>
    /// <response code="401">Token de acesso inválido.</response>
    /// <response code="422">Dados de validação inválidos.</response>
    [HttpPost("logout")]
    [Authorize(Policy = "UsuarioAutenticado")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequestDto dto, CancellationToken ct)
    {
        await refreshTokenValidator.ValidarAsync(dto, ct);

        await authService.LogoutAsync(dto.RefreshToken, ct);

        return NoContent();
    }
}
