using System.Net;
using System.Text.Json;
using Fcg.Users.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Fcg.Users.Api.Middlewares;

public sealed class GlobalExceptionHandlerMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlerMiddleware> logger,
    IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title) = exception switch
        {
            EntidadeNaoEncontradaException => (HttpStatusCode.NotFound, "Recurso não encontrado"),
            ValidacaoException => (HttpStatusCode.UnprocessableEntity, "Dados inválidos"),
            CredenciaisInvalidasException => (HttpStatusCode.Unauthorized, "Credenciais inválidas"),
            AcessoNegadoException => (HttpStatusCode.Forbidden, "Acesso negado"),
            ConflitoDeDadosException => (HttpStatusCode.Conflict, "Conflito de dados"),
            _ => (HttpStatusCode.InternalServerError, "Erro interno do servidor")
        };

        var status = (int)statusCode;

        if (status >= 500)
        {
            logger.LogError(exception, "Erro interno ao processar a requisição {Method} {Path}",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            logger.LogWarning(exception, "Erro de negócio ao processar a requisição {Method} {Path}: {Message}",
                context.Request.Method, context.Request.Path, exception.Message);
        }

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status >= 500 && !environment.IsDevelopment()
                ? "Ocorreu um erro inesperado. Tente novamente mais tarde."
                : exception.Message,
            Instance = $"{context.Request.Method} {context.Request.Path}"
        };

        if (exception is ValidacaoException validacaoEx && validacaoEx.Erros.Count > 0)
        {
            problemDetails.Extensions["errors"] = validacaoEx.Erros;
        }

        if (environment.IsDevelopment() && status >= 500)
        {
            problemDetails.Extensions["stackTrace"] = exception.StackTrace;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, JsonOptions));
    }
}
