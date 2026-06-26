using Fcg.Users.Application.DTOs.Request;
using Fcg.Users.Application.Validators;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace Fcg.Users.UnitTests.Validators;

public class AtualizarUsuarioValidatorTests
{
    private readonly AtualizarUsuarioValidator _validator = new();

    [Fact]
    public void DevePassar_QuandoDadosValidos()
    {
        var dto = new AtualizarUsuarioRequestDto("Nome Válido", "email@valido.com");

        var result = _validator.TestValidate(dto);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void DeveRetornarErro_QuandoNomeVazio(string? nome)
    {
        var dto = new AtualizarUsuarioRequestDto(nome!, "email@test.com");

        var result = _validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Nome);
    }

    [Fact]
    public void DeveRetornarErro_QuandoNomeExcedeMaximo()
    {
        var dto = new AtualizarUsuarioRequestDto(new string('A', 151), "email@test.com");

        var result = _validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Nome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalido")]
    [InlineData(null)]
    public void DeveRetornarErro_QuandoEmailInvalido(string? email)
    {
        var dto = new AtualizarUsuarioRequestDto("Nome", email!);

        var result = _validator.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }
}
