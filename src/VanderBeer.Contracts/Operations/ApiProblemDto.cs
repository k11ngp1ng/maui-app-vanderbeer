namespace VanderBeer.Contracts.Operations;

public sealed record FieldErrorDto(string Field, string Message);

public sealed record ApiProblemDto(string Type, string Title, int Status, string Detail,
    string Code, string TraceId, IReadOnlyList<FieldErrorDto> Errors,
    int? CurrentVersion, IReadOnlyList<object> PriceChanges, decimal? CurrentTotal)
{
    public static ApiProblemDto Create(int status, string code, string detail, string traceId,
        IReadOnlyList<FieldErrorDto>? errors = null, int? currentVersion = null) =>
        new("about:blank", status switch { 400 => "Dados inválidos", 404 => "Não encontrado",
            409 => "Conflito", 503 => "Serviço indisponível", _ => "Erro interno" },
            status, detail, code, traceId, errors ?? [], currentVersion, [], null);
}
