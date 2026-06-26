namespace Fcg.Users.Domain.Exceptions;

public class ValidacaoException : DomainException
{
    public IReadOnlyDictionary<string, string[]> Erros { get; }

    public ValidacaoException(string message) : base(message)
    {
        Erros = new Dictionary<string, string[]>();
    }

    public ValidacaoException(IDictionary<string, string[]> erros)
        : base("Um ou mais erros de validação ocorreram.")
    {
        Erros = new Dictionary<string, string[]>(erros);
    }
}
