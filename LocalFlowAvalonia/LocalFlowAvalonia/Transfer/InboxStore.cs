using System.Text.RegularExpressions;

namespace LocalFlowAvalonia.Transfer;

internal sealed class InboxStore
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public InboxStore(string inboxPath)
    {
        Path = System.IO.Path.GetFullPath(inboxPath);
    }

    public string Path { get; }

    public void EnsureCreated() => Directory.CreateDirectory(Path);

    public void DeleteLeftoverPartials()
    {
        if (!Directory.Exists(Path))
            return;

        foreach (var file in Directory.EnumerateFiles(Path, ".partial-*"))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // leftover temps are best-effort
            }
        }
    }

    public string CreatePartialPath(Guid transferId) =>
        System.IO.Path.Combine(Path, $".partial-{transferId:N}");

    public bool TrySanitizeFileName(string raw, out string safeName, out string error)
    {
        safeName = "";
        error = "";

        var name = System.IO.Path.GetFileName(raw.Replace('\\', '/').Trim());
        name = name.TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            error = "недопустимое имя файла";
            return false;
        }

        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        name = Regex.Replace(name, @"_+", "_");

        var stem = System.IO.Path.GetFileNameWithoutExtension(name);
        if (ReservedNames.Contains(stem))
            name = "_" + name;

        if (name.StartsWith(".partial-", StringComparison.OrdinalIgnoreCase))
            name = "_" + name;

        if (name.Length > 200)
        {
            var ext = System.IO.Path.GetExtension(name);
            var maxStem = Math.Max(1, 200 - ext.Length);
            name = name[..maxStem] + ext;
        }

        safeName = name;
        return true;
    }

    public bool HasEnoughSpace(long fileSize)
    {
        try
        {
            var root = System.IO.Path.GetPathRoot(Path);
            if (string.IsNullOrEmpty(root))
                return true;

            var drive = new DriveInfo(root);
            const long margin = 1_000_000;
            return drive.AvailableFreeSpace >= fileSize + margin;
        }
        catch
        {
            return true;
        }
    }

    public string Commit(string partialPath, string safeFileName)
    {
        var destination = UniquePath(safeFileName);
        File.Move(partialPath, destination);
        return destination;
    }

    public void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignored
        }
    }

    private string UniquePath(string safeFileName)
    {
        var destination = System.IO.Path.Combine(Path, safeFileName);
        if (!File.Exists(destination))
            return destination;

        var stem = System.IO.Path.GetFileNameWithoutExtension(safeFileName);
        var ext = System.IO.Path.GetExtension(safeFileName);
        for (var i = 1; i < 10_000; i++)
        {
            var candidate = System.IO.Path.Combine(Path, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        throw new IOException("Слишком много совпадений имён в папке входящих.");
    }
}
