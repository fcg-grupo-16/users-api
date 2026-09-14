using Serilog.Core;
using Serilog.Events;

namespace Fcg.Users.IntegrationTests.Infrastructure;

/// <summary>
/// Sink do Serilog que guarda os eventos emitidos, para os testes asseverarem o que foi LOGADO —
/// e não só o que foi respondido.
/// </summary>
/// <remarks>
/// Escrito à mão, sem pacote novo: o <c>Serilog.AspNetCore</c> já chega ao projeto de teste por
/// transitividade do <c>ProjectReference</c> para a API. O Serilog o adota porque o
/// <c>Program.cs</c> configura <c>.ReadFrom.Services(services)</c> — qualquer
/// <see cref="ILogEventSink"/> registrado na DI entra no pipeline. Mesmo espírito do
/// <c>LoggerComEscopos</c> da notifications-function.
/// </remarks>
public sealed class CapturaDeLog : ILogEventSink
{
    private readonly List<LogEvent> _eventos = [];
    private readonly Lock _trava = new();

    public void Emit(LogEvent logEvent)
    {
        lock (_trava)
        {
            _eventos.Add(logEvent);
        }
    }

    public IReadOnlyList<LogEvent> Eventos
    {
        get { lock (_trava) { return [.. _eventos]; } }
    }

    public void Limpar()
    {
        lock (_trava) { _eventos.Clear(); }
    }

    /// <summary>Eventos de request-logging (os que carregam <c>StatusCode</c>).</summary>
    public IEnumerable<LogEvent> DeRequisicao(string caminho) =>
        Eventos.Where(e =>
            e.Properties.TryGetValue("RequestPath", out var p)
            && p.ToString().Trim('"').Equals(caminho, StringComparison.OrdinalIgnoreCase));

    public static int? StatusDe(LogEvent e) =>
        e.Properties.TryGetValue("StatusCode", out var v) && int.TryParse(v.ToString().Trim('"'), out var n)
            ? n
            : null;
}
