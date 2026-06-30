using Fcg.Users.Domain.Exceptions;

namespace Fcg.Users.Domain.Entities;

public sealed class RefreshToken
{
    public string Id { get; private set; }
    public string UsuarioId { get; private set; }
    public string TokenHash { get; private set; }
    public DateTime ExpiraEm { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime? RevogadoEm { get; private set; }

    public bool Ativo => RevogadoEm is null && DateTime.UtcNow < ExpiraEm;

    public RefreshToken(string usuarioId, string tokenHash, DateTime expiraEm)
    {
        if (string.IsNullOrWhiteSpace(usuarioId))
        {
            throw new ValidacaoException("O identificador do usuário é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ValidacaoException("O hash do refresh token é obrigatório.");
        }

        if (expiraEm <= DateTime.UtcNow)
        {
            throw new ValidacaoException("A expiração do refresh token deve estar no futuro.");
        }

        Id = string.Empty;
        UsuarioId = usuarioId;
        TokenHash = tokenHash;
        ExpiraEm = expiraEm;
        CriadoEm = DateTime.UtcNow;
    }

    private RefreshToken() // Para deserialização do MongoDB
    {
        Id = string.Empty;
        UsuarioId = string.Empty;
        TokenHash = string.Empty;
    }

    public void Revogar()
    {
        if (RevogadoEm is not null)
        {
            return;
        }

        RevogadoEm = DateTime.UtcNow;
    }
}
