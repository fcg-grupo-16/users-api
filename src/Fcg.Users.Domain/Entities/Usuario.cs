using Fcg.Users.Domain.Enums;
using Fcg.Users.Domain.Exceptions;
using Fcg.Users.Domain.ValueObjects;

namespace Fcg.Users.Domain.Entities;

public sealed class Usuario
{
    public string Id { get; private set; }
    public string Nome { get; private set; }
    public Email Email { get; private set; }
    public string SenhaHash { get; private set; }
    public TipoUsuario Tipo { get; private set; }
    public DateTime DataCriacao { get; private set; }
    public bool Ativo { get; private set; }

    public Usuario(string nome, Email email, string senhaHash, TipoUsuario tipo = TipoUsuario.Usuario)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new ValidacaoException("O nome do usuário é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(senhaHash))
        {
            throw new ValidacaoException("O hash da senha é obrigatório.");
        }

        Id = string.Empty;
        Nome = nome.Trim();
        Email = email ?? throw new ValidacaoException("O e-mail é obrigatório.");
        SenhaHash = senhaHash;
        Tipo = tipo;
        DataCriacao = DateTime.UtcNow;
        Ativo = true;
    }

    private Usuario() // Para deserialização do MongoDB
    {
        Id = string.Empty;
        Nome = string.Empty;
        Email = null!;
        SenhaHash = string.Empty;
    }

    public void AtualizarNome(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new ValidacaoException("O nome do usuário é obrigatório.");
        }

        Nome = nome.Trim();
    }

    public void AtualizarEmail(Email email)
    {
        Email = email ?? throw new ValidacaoException("O e-mail é obrigatório.");
    }

    public void AtualizarSenha(string senhaHash)
    {
        if (string.IsNullOrWhiteSpace(senhaHash))
        {
            throw new ValidacaoException("O hash da senha é obrigatório.");
        }

        SenhaHash = senhaHash;
    }

    public void Desativar() => Ativo = false;

    public void Ativar() => Ativo = true;
}
