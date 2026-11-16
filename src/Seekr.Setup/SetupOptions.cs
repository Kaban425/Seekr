using System.IO;

namespace Seekr.Setup;

public sealed record SetupOptions(string Directory, bool DesktopShortcut)
{
    public static string ValidatePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(c => char.IsControl(c) || c == '"') || !Path.IsPathFullyQualified(value)) throw new ArgumentException("Выберите полный путь к папке на локальном диске.", nameof(value));
        var path = Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar);
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.Length <= 3 || path.Equals(Path.GetPathRoot(path)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Выберите отдельную папку приложения на локальном диске.", nameof(value));
        return path;
    }
    public string Properties => "REBOOT=ReallySuppress MSIRESTARTMANAGERCONTROL=Disable SEEKR_INSTALL_DIRECTORY=\"" + ValidatePath(Directory) + "\" SEEKR_DESKTOP_CHOICE=" + (DesktopShortcut ? "1" : "0");
}
