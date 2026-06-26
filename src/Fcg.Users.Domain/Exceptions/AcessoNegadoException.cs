namespace Fcg.Users.Domain.Exceptions;

public class AcessoNegadoException : DomainException
{
    public AcessoNegadoException()
        : base("Acesso negado. Você não tem permissão para realizar esta ação.") { }

    public AcessoNegadoException(string message) : base(message) { }
}
