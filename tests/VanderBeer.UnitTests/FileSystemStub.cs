namespace VanderBeer.App.Storage;

// CustomerStore's source is linked into the platform-neutral tests. They pass an explicit
// temporary database path; this stub only satisfies the unused default constructor.
internal static class FileSystem
{
    public static string AppDataDirectory => Path.GetTempPath();
}
