using System.IO;
using System.Text;

namespace OwaWidget.App.Services;

public static class Log
{
    private const long MaxBytes = 1024 * 1024;

    private static readonly object Gate = new();

    public static string Path => System.IO.Path.Combine(AppStorage.Directory, "widget.log");

    public static void Info(string message)
    {
        Write("INFO ", message);
    }

    public static void Error(string context, Exception exception)
    {
        Write("ERROR", $"{context}: {exception.GetType().Name}: {exception.Message}");
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppStorage.Directory);

                var file = new FileInfo(Path);
                if (file.Exists && file.Length > MaxBytes)
                {
                    var tail = File.ReadAllLines(Path).TakeLast(500).ToArray();
                    File.WriteAllLines(Path, tail);
                }

                File.AppendAllText(
                    Path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch (Exception)
        {
        }
    }
}