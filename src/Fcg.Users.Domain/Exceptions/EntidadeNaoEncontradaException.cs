namespace Fcg.Users.Domain.Exceptions;

public class EntidadeNaoEncontradaException : DomainException
{
    public EntidadeNaoEncontradaException(string entidade, object id)
        : base($"{entidade} com identificador '{id}' não foi encontrado(a).") { }

    public EntidadeNaoEncontradaException(string message) : base(message) { }
}
