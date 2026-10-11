using System.IO;

namespace LgController.Core;

public static class Log
{
    private static readonly object Gate = new();

    public static void Write(Exception exception) => Write(exception.ToString());

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsStore.DefaultDirectory);
                File.AppendAllText(
                    Path.Combine(SettingsStore.DefaultDirectory, "log.txt"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Logging must never crash the app.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
