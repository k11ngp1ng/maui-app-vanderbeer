namespace VanderBeer.Contracts.Operations;

public sealed record ServiceUnavailableDto(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Code,
    string TraceId,
    IReadOnlyList<object> Errors,
    int? CurrentVersion,
    IReadOnlyList<object> PriceChanges,
    decimal? CurrentTotal)
{
    public static ServiceUnavailableDto Create(string traceId) => new(
        "about:blank", "Serviço indisponível", 503, "Tente novamente mais tarde.",
        "ServiceUnavailable", traceId, [], null, [], null);
}
