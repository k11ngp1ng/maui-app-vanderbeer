namespace VanderBeer.Application.Operations;

public sealed class DataStoreUnavailableException : Exception
{
    public DataStoreUnavailableException(Exception? innerException = null)
        : base("O banco de dados não está disponível.", innerException)
    {
    }
}
