namespace VanderBeer.App.Storage;

internal static class LocalDatabase
{
    public static string Path
    {
        get
        {
            if (Environment.GetEnvironmentVariable("VANDERBEER_LOCAL_DB") is { Length: > 0 } path)
                return path;
            return System.IO.Path.Combine(FileSystem.AppDataDirectory, "vanderbeer-local.db");
        }
    }
}
