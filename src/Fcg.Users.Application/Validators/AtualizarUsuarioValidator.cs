using Fcg.Users.Application.DTOs.Request;
using FluentValidation;

namespace Fcg.Users.Application.Validators;

public sealed class AtualizarUsuarioValidator : AbstractValidator<AtualizarUsuarioRequestDto>
{
    public AtualizarUsuarioValidator()
    {
        RuleFor(x => x.Nome)
            .NotEmpty().WithMessage("O campo Nome é obrigatório.")
            .MaximumLength(150).WithMessage("O campo Nome deve ter no máximo 150 caracteres.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("O campo E-mail é obrigatório.")
            .EmailAddress().WithMessage("O formato do e-mail é inválido.");
    }
}
