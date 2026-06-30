using Fcg.Users.Domain.Entities;

namespace Fcg.Users.Application.Interfaces;

public interface ITokenService
{
    string GerarToken(Usuario usuario);
    string GerarRefreshToken();
    DateTime ObterExpiracao();
    DateTime ObterExpiracaoRefreshToken();
}
