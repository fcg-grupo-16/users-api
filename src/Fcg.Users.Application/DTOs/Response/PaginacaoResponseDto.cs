namespace Fcg.Users.Application.DTOs.Response;

public sealed record PaginacaoResponseDto<T>(
    IEnumerable<T> Itens,
    int Pagina,
    int TamanhoPagina,
    long Total);
