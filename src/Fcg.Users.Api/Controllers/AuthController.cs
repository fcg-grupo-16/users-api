using Fcg.Users.Api.Extensions;
using Fcg.Users.Application.DTOs.Request;
using Fcg.Users.Application.DTOs.Response;
using Fcg.Users.Application.Services;
using FluentValidation;
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
    IValidator<LoginRequestDto> loginValidator) : ControllerBase
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
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto, CancellationToken ct)
    {
        await loginValidator.ValidarAsync(dto, ct);

        var resultado = await authService.LoginAsync(dto, ct);

        return Ok(resultado);
    }
}
