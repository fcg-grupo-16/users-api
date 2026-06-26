using Fcg.Users.Application.DTOs.Request;
using Fcg.Users.Application.Validators;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace Fcg.Users.UnitTests.Validators;

public class LoginValidatorTests
{
    private readonly LoginValidator _validator = new();

    [Fact]
    public void DevePassar_QuandoDadosValidos()
    {
        var dto = new LoginRequestDto("usuario@email.com", "Senha@123");

        var result = _validator.TestValidate(dto);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void DeveRetornarErro_QuandoEmailVazio(string? email)
    {
        var dto = new LoginRequestDto(email!, "Senha@123");

        var result = _validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void DeveRetornarErro_QuandoEmailFormatoInvalido()
    {
        var dto = new LoginRequestDto("invalido", "Senha@123");

        var result = _validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void DeveRetornarErro_QuandoSenhaVazia(string? senha)
    {
        var dto = new LoginRequestDto("email@test.com", senha!);

        var result = _validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Senha);
    }
}
