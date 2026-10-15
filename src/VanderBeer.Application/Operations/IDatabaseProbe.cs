namespace VanderBeer.Application.Operations;

public interface IDatabaseProbe
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
}
