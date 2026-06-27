using Fcg.Contracts.Events;
using Fcg.Users.Application.DTOs.Request;
using Fcg.Users.Application.DTOs.Response;
using Fcg.Users.Application.Interfaces;
using Fcg.Users.Domain.Entities;
using Fcg.Users.Domain.Exceptions;
using Fcg.Users.Domain.Repositories;
using Fcg.Users.Domain.ValueObjects;

namespace Fcg.Users.Application.Services;

public sealed class UsuarioService(
    IUsuarioRepository usuarioRepository,
    IPasswordHasher passwordHasher,
    IEventPublisher eventPublisher) : IUsuarioService
{
    public async Task<UsuarioResponseDto> CriarAsync(CriarUsuarioRequestDto dto, CancellationToken ct = default)
    {
        var email = new Email(dto.Email);

        if (await usuarioRepository.EmailExisteAsync(email.Endereco, ct))
        {
            throw new ConflitoDeDadosException("Usuário", "e-mail", email.Endereco);
        }

        var senha = new Senha(dto.Senha);
        var senhaHash = passwordHasher.Hash(senha.Valor);

        var usuario = new Usuario(dto.Nome, email, senhaHash);
        await usuarioRepository.CriarAsync(usuario, ct);

        await eventPublisher.PublishAsync(
            new UserCreatedEvent
            {
                UserId = usuario.Id,
                Nome = usuario.Nome,
                Email = usuario.Email.Endereco
            },
            ct);

        return MapToDto(usuario);
    }

    public async Task<UsuarioResponseDto> ObterPorIdAsync(string id, CancellationToken ct = default)
    {
        var usuario = await usuarioRepository.ObterPorIdAsync(id, ct)
            ?? throw new EntidadeNaoEncontradaException("Usuário", id);

        return MapToDto(usuario);
    }

    public async Task<PaginacaoResponseDto<UsuarioResponseDto>> ListarAsync(int pagina, int tamanhoPagina, CancellationToken ct = default)
    {
        var usuarios = await usuarioRepository.ObterTodosAsync(pagina, tamanhoPagina, ct);
        var total = await usuarioRepository.ContarAsync(ct);
        var itens = usuarios.Select(MapToDto).ToList();

        return new PaginacaoResponseDto<UsuarioResponseDto>(itens, pagina, tamanhoPagina, total);
    }

    public async Task<UsuarioResponseDto> AtualizarAsync(string id, AtualizarUsuarioRequestDto dto, CancellationToken ct = default)
    {
        var usuario = await usuarioRepository.ObterPorIdAsync(id, ct)
            ?? throw new EntidadeNaoEncontradaException("Usuário", id);

        var novoEmail = new Email(dto.Email);

        if (novoEmail.Endereco != usuario.Email.Endereco &&
            await usuarioRepository.EmailExisteAsync(novoEmail.Endereco, ct))
        {
            throw new ConflitoDeDadosException("Usuário", "e-mail", novoEmail.Endereco);
        }

        usuario.AtualizarNome(dto.Nome);
        usuario.AtualizarEmail(novoEmail);

        await usuarioRepository.AtualizarAsync(usuario, ct);

        return MapToDto(usuario);
    }

    public async Task RemoverAsync(string id, CancellationToken ct = default)
    {
        var usuario = await usuarioRepository.ObterPorIdAsync(id, ct)
            ?? throw new EntidadeNaoEncontradaException("Usuário", id);

        usuario.Desativar();
        await usuarioRepository.AtualizarAsync(usuario, ct);
    }

    private static UsuarioResponseDto MapToDto(Usuario usuario) =>
        new(
            usuario.Id,
            usuario.Nome,
            usuario.Email.Endereco,
            usuario.Tipo,
            usuario.DataCriacao,
            usuario.Ativo);
}
