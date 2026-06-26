using Fcg.Users.Application.DTOs.Request;
using Fcg.Users.Application.Validators;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace Fcg.Users.UnitTests.Validators;

public class CriarUsuarioValidatorTests
{
    private readonly CriarUsuarioValidator _validator = new();

    [Fact]
    public void DevePassar_QuandoDadosValidos()
    {
        var dto = new CriarUsuarioRequestDto("Felipe", "felipe@email.com", "Senha@123");

        var result = _validator.TestValidate(dto);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void DeveRetornarErro_QuandoNomeVazio(string? nome)
    {
        var dto = new CriarUsuarioRequestDto(nome!, "email@test.com", "Senha@123");

        var result = _validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Nome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalido")]
    [InlineData(null)]
    public void DeveRetornarErro_QuandoEmailInvalido(string? email)
    {
        var dto = new CriarUsuarioRequestDto("Felipe", email!, "Senha@123");

        var result = _validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("curta")]
    [InlineData("semmaiuscula1@")]
    [InlineData("SEMMINUSCULA1@")]
    [InlineData("SemNumero@abc")]
    [InlineData("SemEspecial1a")]
    public void DeveRetornarErro_QuandoSenhaInvalida(string? senha)
    {
        var dto = new CriarUsuarioRequestDto("Felipe", "email@test.com", senha!);

        var result = _validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Senha);
    }
}
