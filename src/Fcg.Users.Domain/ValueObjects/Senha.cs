using System.Text.RegularExpressions;
using Fcg.Users.Domain.Exceptions;

namespace Fcg.Users.Domain.ValueObjects;

public sealed partial record Senha
{
    public string Valor { get; }

    public Senha(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new ValidacaoException("A senha é obrigatória.");
        }

        var erros = new List<string>();

        if (valor.Length < 8)
        {
            erros.Add("A senha deve ter no mínimo 8 caracteres.");
        }

        if (!LetraMaiusculaRegex().IsMatch(valor))
        {
            erros.Add("A senha deve conter ao menos uma letra maiúscula.");
        }

        if (!LetraMinusculaRegex().IsMatch(valor))
        {
            erros.Add("A senha deve conter ao menos uma letra minúscula.");
        }

        if (!NumeroRegex().IsMatch(valor))
        {
            erros.Add("A senha deve conter ao menos um número.");
        }

        if (!CaractereEspecialRegex().IsMatch(valor))
        {
            erros.Add("A senha deve conter ao menos um caractere especial (!@#$%^&* etc.).");
        }

        if (erros.Count > 0)
        {
            throw new ValidacaoException(
                new Dictionary<string, string[]> { { "Senha", erros.ToArray() } });
        }

        Valor = valor;
    }

    [GeneratedRegex(@"[A-Z]", RegexOptions.Compiled)]
    private static partial Regex LetraMaiusculaRegex();

    [GeneratedRegex(@"[a-z]", RegexOptions.Compiled)]
    private static partial Regex LetraMinusculaRegex();

    [GeneratedRegex(@"[0-9]", RegexOptions.Compiled)]
    private static partial Regex NumeroRegex();

    [GeneratedRegex(@"[!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]", RegexOptions.Compiled)]
    private static partial Regex CaractereEspecialRegex();

    public override string ToString() => "********";
}
