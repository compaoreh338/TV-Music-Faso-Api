namespace TVMusicFaso.Core.Services;

public static class AppPaths
{
    public static string DataDirectory
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TVMusicFaso");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public static string DatabasePath => Path.Combine(DataDirectory, "mediatheque.db");

    public static string BackupDirectory
    {
        get
        {
            var directory = Path.Combine(DataDirectory, "backups");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
