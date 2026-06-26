using Fcg.Users.Application.DTOs.Request;
using FluentValidation;

namespace Fcg.Users.Application.Validators;

public sealed class CriarUsuarioValidator : AbstractValidator<CriarUsuarioRequestDto>
{
    public CriarUsuarioValidator()
    {
        RuleFor(x => x.Nome)
            .NotEmpty().WithMessage("O campo Nome é obrigatório.")
            .MaximumLength(150).WithMessage("O campo Nome deve ter no máximo 150 caracteres.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("O campo E-mail é obrigatório.")
            .EmailAddress().WithMessage("O formato do e-mail é inválido.");

        RuleFor(x => x.Senha)
            .NotEmpty().WithMessage("O campo Senha é obrigatório.")
            .MinimumLength(8).WithMessage("A senha deve ter no mínimo 8 caracteres.")
            .Matches("[A-Z]").WithMessage("A senha deve conter ao menos uma letra maiúscula.")
            .Matches("[a-z]").WithMessage("A senha deve conter ao menos uma letra minúscula.")
            .Matches("[0-9]").WithMessage("A senha deve conter ao menos um número.")
            .Matches(@"[!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]").WithMessage("A senha deve conter ao menos um caractere especial.");
    }
}
