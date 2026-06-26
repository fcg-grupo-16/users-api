using System.Security.Claims;
using Fcg.Users.Api.Extensions;
using Fcg.Users.Application.DTOs.Request;
using Fcg.Users.Application.DTOs.Response;
using Fcg.Users.Application.Services;
using Fcg.Users.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fcg.Users.Api.Controllers;

/// <summary>
/// Controller responsável pelo gerenciamento de usuários.
/// </summary>
[ApiController]
[Route("api/v1/usuarios")]
[Produces("application/json")]
public sealed class UsuariosController(
    IUsuarioService usuarioService,
    IValidator<CriarUsuarioRequestDto> criarValidator,
    IValidator<AtualizarUsuarioRequestDto> atualizarValidator) : ControllerBase
{
    /// <summary>
    /// Registrar novo usuário.
    /// </summary>
    /// <param name="dto">Dados do novo usuário.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Usuário criado.</returns>
    /// <response code="201">Usuário criado com sucesso.</response>
    /// <response code="409">E-mail já cadastrado.</response>
    /// <response code="422">Dados de validação inválidos.</response>
    [HttpPost]
    [ProducesResponseType(typeof(UsuarioResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Criar([FromBody] CriarUsuarioRequestDto dto, CancellationToken ct)
    {
        await criarValidator.ValidarAsync(dto, ct);

        var resultado = await usuarioService.CriarAsync(dto, ct);

        return CreatedAtAction(nameof(ObterPorId), new { id = resultado.Id }, resultado);
    }

    /// <summary>
    /// Listar usuários com paginação.
    /// </summary>
    /// <param name="pagina">Número da página (padrão: 1).</param>
    /// <param name="tamanhoPagina">Itens por página (padrão: 10).</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Lista paginada de usuários.</returns>
    /// <response code="200">Lista de usuários retornada com sucesso.</response>
    [HttpGet]
    [Authorize(Policy = "ApenasAdmin")]
    [ProducesResponseType(typeof(PaginacaoResponseDto<UsuarioResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanhoPagina = 10,
        CancellationToken ct = default)
    {
        var resultado = await usuarioService.ListarAsync(pagina, tamanhoPagina, ct);

        return Ok(resultado);
    }

    /// <summary>
    /// Obter usuário por ID.
    /// </summary>
    /// <param name="id">Identificador do usuário.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Dados do usuário.</returns>
    /// <response code="200">Usuário retornado com sucesso.</response>
    /// <response code="403">Acesso negado.</response>
    /// <response code="404">Usuário não encontrado.</response>
    [HttpGet("{id}")]
    [Authorize(Policy = "UsuarioAutenticado")]
    [ProducesResponseType(typeof(UsuarioResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterPorId(string id, CancellationToken ct)
    {
        ValidarAcessoAoRecurso(id);

        var resultado = await usuarioService.ObterPorIdAsync(id, ct);

        return Ok(resultado);
    }

    /// <summary>
    /// Atualizar dados do usuário.
    /// </summary>
    /// <param name="id">Identificador do usuário.</param>
    /// <param name="dto">Dados atualizados.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Usuário atualizado.</returns>
    /// <response code="200">Usuário atualizado com sucesso.</response>
    /// <response code="403">Acesso negado.</response>
    /// <response code="404">Usuário não encontrado.</response>
    /// <response code="409">E-mail já cadastrado.</response>
    /// <response code="422">Dados de validação inválidos.</response>
    [HttpPut("{id}")]
    [Authorize(Policy = "UsuarioAutenticado")]
    [ProducesResponseType(typeof(UsuarioResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Atualizar(string id, [FromBody] AtualizarUsuarioRequestDto dto, CancellationToken ct)
    {
        ValidarAcessoAoRecurso(id);

        await atualizarValidator.ValidarAsync(dto, ct);

        var resultado = await usuarioService.AtualizarAsync(id, dto, ct);

        return Ok(resultado);
    }

    /// <summary>
    /// Desativar usuário (soft delete).
    /// </summary>
    /// <param name="id">Identificador do usuário.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <response code="204">Usuário desativado com sucesso.</response>
    /// <response code="404">Usuário não encontrado.</response>
    [HttpDelete("{id}")]
    [Authorize(Policy = "ApenasAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remover(string id, CancellationToken ct)
    {
        await usuarioService.RemoverAsync(id, ct);

        return NoContent();
    }

    private void ValidarAcessoAoRecurso(string recursoUsuarioId)
    {
        var usuarioIdLogado = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdmin = User.IsInRole("Administrador");

        if (!isAdmin && usuarioIdLogado != recursoUsuarioId)
        {
            throw new AcessoNegadoException();
        }
    }
}
