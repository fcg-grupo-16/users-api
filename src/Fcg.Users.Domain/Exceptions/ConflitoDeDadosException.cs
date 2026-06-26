namespace Fcg.Users.Domain.Exceptions;

public class ConflitoDeDadosException : DomainException
{
    public ConflitoDeDadosException(string message) : base(message) { }

    public ConflitoDeDadosException(string entidade, string campo, object valor)
        : base($"Já existe um(a) {entidade} com {campo} '{valor}'.") { }
}
