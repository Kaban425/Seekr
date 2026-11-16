using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace Seekr.Setup;

public static class SetupPackage
{
    public static string Version => typeof(SetupPackage).Assembly.GetName().Version?.ToString(3) ?? "1.0.2";
    public static async Task<string> ExtractAsync(CancellationToken token)
    {
        var assembly = typeof(SetupPackage).Assembly;
        var expected = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "PayloadSha256")?.Value;
        if (expected is null || expected.Length != 64) throw new InvalidDataException("Установщик собран без пакета приложения. Скачайте готовый Setup.exe со страницы релиза.");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Seekr", "Setup", "Packages", expected);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Seekr.msi");
        if (File.Exists(path) && await Valid(path, expected, token).ConfigureAwait(false)) return path;
        var temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var source = assembly.GetManifestResourceStream("Seekr.Payload.msi") ?? throw new InvalidDataException("Пакет приложения отсутствует."))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await source.CopyToAsync(output, token).ConfigureAwait(false);
            if (!await Valid(temporary, expected, token).ConfigureAwait(false)) throw new InvalidDataException("Проверка целостности пакета не пройдена. Скачайте установщик заново.");
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static async Task<bool> Valid(string path, string expected, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
