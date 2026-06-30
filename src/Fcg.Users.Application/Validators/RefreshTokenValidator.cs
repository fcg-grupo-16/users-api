using Fcg.Users.Application.DTOs.Request;
using FluentValidation;

namespace Fcg.Users.Application.Validators;

public sealed class RefreshTokenValidator : AbstractValidator<RefreshTokenRequestDto>
{
    public RefreshTokenValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("O refresh token é obrigatório.");
    }
}
