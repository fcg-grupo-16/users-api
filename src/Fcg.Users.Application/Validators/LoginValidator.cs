using Fcg.Users.Application.DTOs.Request;
using FluentValidation;

namespace Fcg.Users.Application.Validators;

public sealed class LoginValidator : AbstractValidator<LoginRequestDto>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("O campo E-mail é obrigatório.")
            .EmailAddress().WithMessage("O formato do e-mail é inválido.");

        RuleFor(x => x.Senha)
            .NotEmpty().WithMessage("O campo Senha é obrigatório.");
    }
}
