namespace Fcg.Users.Application.DTOs.Response;

/// <summary>
/// Contato de um usuário, para consumo SERVIÇO-A-SERVIÇO.
/// </summary>
/// <remarks>
/// Deliberadamente com um único campo. O <see cref="UsuarioResponseDto"/> traria nome, tipo, data de
/// criação e status — dado que quem só precisa despachar um e-mail não tem por que receber. Menor
/// superfície de PII exposta ao componente que consulta.
/// </remarks>
public sealed record ContatoUsuarioResponseDto(string Email);
