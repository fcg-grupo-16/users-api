namespace Fcg.Users.Domain.Exceptions;

public class CredenciaisInvalidasException : DomainException
{
    public CredenciaisInvalidasException()
        : base("Credenciais inválidas.") { }

    public CredenciaisInvalidasException(string message) : base(message) { }
}
