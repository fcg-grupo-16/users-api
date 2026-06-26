using Fcg.Users.Domain.Entities;

namespace Fcg.Users.Application.Interfaces;

public interface ITokenService
{
    string GerarToken(Usuario usuario);
    DateTime ObterExpiracao();
}
