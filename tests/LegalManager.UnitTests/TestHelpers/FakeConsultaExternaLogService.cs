using LegalManager.Application.Interfaces;

namespace LegalManager.UnitTests.TestHelpers;

/// <summary>
/// No-op stand-in for <see cref="IConsultaExternaLogService"/> in unit tests: just runs
/// <c>execucao</c> and returns its result, without touching the database. Using a real mock
/// here would return default(T) for the unconfigured async call, silently breaking any test
/// that asserts on the value returned by the wrapped Escavador/DataJud call.
/// </summary>
public sealed class FakeConsultaExternaLogService : IConsultaExternaLogService
{
    public static readonly FakeConsultaExternaLogService Instance = new();

    public Task<T> RegistrarAsync<T>(
        string api,
        string tipoConsulta,
        string origem,
        object? parametros,
        Func<Task<T>> execucao,
        Func<T, (int? Total, object? Resumo)>? extrairResumo = null,
        Guid? tenantId = null,
        Guid? usuarioId = null,
        CancellationToken ct = default) => execucao();
}
