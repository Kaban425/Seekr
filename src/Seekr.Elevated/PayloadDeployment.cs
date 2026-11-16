namespace Seekr.Elevated;

/// <summary>Prepares a complete payload before stopping the service and restores the previous payload on failure.</summary>
public static class PayloadDeployment
{
    public static void Uninstall(string target, Action stopAndUnregister)
    {
        ArgumentNullException.ThrowIfNull(stopAndUnregister);
        target = Path.GetFullPath(target);
        var roots = new[] { target, target + ".previous" };
        foreach (var root in roots)
        {
            EnsureSafePath(root);
            if (Directory.Exists(root)) _ = Files(root).ToArray();
        }
        stopAndUnregister();
        foreach (var root in roots) if (Directory.Exists(root)) RemovePayload(root);
    }

    public static void Install(string source, string target, Action stop, Action start, Action restartPrevious)
    {
        source = Path.GetFullPath(source);
        target = Path.GetFullPath(target);
        ServiceInstaller.ValidateSource(source);
        EnsureSafePath(target);
        var staging = target + ".staging-" + Guid.NewGuid().ToString("N");
        var backup = target + ".previous";
        var stopped = false;
        var published = false;
        var replaced = false;
        try
        {
            foreach (var file in Files(source))
            {
                var destination = Path.Combine(staging, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: false);
            }
            ServiceInstaller.ValidateSource(staging);
            stop();
            stopped = true;
            EnsureSafePath(backup);
            if (!Directory.Exists(target) && Directory.Exists(backup)) Directory.Move(backup, target);
            if (Directory.Exists(backup)) RemovePayload(backup);
            if (Directory.Exists(target)) { Directory.Move(target, backup); replaced = true; }
            Directory.Move(staging, target);
            published = true;
            start();
            // Retain one previous complete version for recovery if a later launch fails.
        }
        catch
        {
            if (published || replaced)
            {
                stop();
                if (published && Directory.Exists(target)) Directory.Move(target, staging);
                if (replaced) Directory.Move(backup, target);
            }
            if (stopped) restartPrevious();
            throw;
        }
        finally { if (Directory.Exists(staging)) RemovePayload(staging); }
    }

    private static IEnumerable<string> Files(string root)
    {
        EnsureSafePath(root);
        foreach (var child in Directory.EnumerateFileSystemEntries(root))
        {
            EnsureSafePath(child);
            if (Directory.Exists(child)) { foreach (var file in Files(child)) yield return file; }
            else yield return child;
        }
    }

    private static void RemovePayload(string root)
    {
        // Validate the entire tree before deleting anything; reject links rather than following them.
        var files = Files(root).ToArray();
        foreach (var file in files) File.Delete(file);
        RemoveEmptyDirectories(root);
    }

    private static void RemoveEmptyDirectories(string root)
    {
        EnsureSafePath(root);
        foreach (var directory in Directory.EnumerateDirectories(root)) RemoveEmptyDirectories(directory);
        Directory.Delete(root, recursive: false);
    }

    public static void EnsureSafePath(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)
                    && (Directory.Exists(current) ? new DirectoryInfo(current).LinkTarget : new FileInfo(current).LinkTarget) is not null)
                    throw new IOException("Путь установки содержит ссылку: " + current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
