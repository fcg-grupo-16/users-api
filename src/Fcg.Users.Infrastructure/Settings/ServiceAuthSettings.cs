namespace Fcg.Users.Infrastructure.Settings;

/// <summary>
/// Credencial de autenticação SERVIÇO-A-SERVIÇO, separada da dos usuários.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que uma chave própria, e não a <c>JwtSettings:SecretKey</c>.</b> A chave dos usuários é
/// compartilhada entre <c>users-api</c>, <c>catalog-api</c> e a credencial do Kong (paridade
/// tríplice). Quem a possui pode assinar QUALQUER token, inclusive com a role
/// <c>Administrador</c> — dá-la à <c>notifications-function</c> para que ela leia um e-mail
/// converteria um componente serverless em portador de credencial administrativa da plataforma.
/// </para>
/// <para>
/// Com chave separada, o pior caso de vazamento do segredo da Function é o acesso aos endpoints de
/// serviço — hoje, apenas a consulta de contato, que devolve um único campo. O token de serviço
/// também NÃO é aceito pelo <c>catalog-api</c>, que valida com a chave dos usuários.
/// </para>
/// </remarks>
public sealed class ServiceAuthSettings
{
    public const string SectionName = "ServiceAuth";

    /// <summary>Nome do esquema de autenticação registrado para tokens de serviço.</summary>
    public const string Esquema = "Servico";

    /// <summary>
    /// Role exigida nos tokens de serviço.
    /// </summary>
    /// <remarks>
    /// Propositalmente FORA do enum <c>TipoUsuario</c> (que só produz <c>Usuario</c> e
    /// <c>Administrador</c>): nenhum login consegue emitir um token com esta role, mesmo com a chave
    /// dos usuários em mãos.
    /// </remarks>
    public const string Role = "Servico";

    public required string SecretKey { get; init; }
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
}
