using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;

namespace Seekr.Elevated;

public static class ServiceInstaller
{
    public static void Uninstall()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException("Удаление службы требует прав администратора.");
        var target = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Seekr", "Service"));
        var runningFrom = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
        if (runningFrom.Equals(target, StringComparison.OrdinalIgnoreCase) || runningFrom.Equals(target + ".previous", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Запустите удаление службы из комплекта приложения, а не из её установленной папки.");
        PayloadDeployment.Uninstall(target, () =>
        {
            var services = ServiceController.GetServices();
            bool exists;
            try { exists = services.Any(service => service.ServiceName == "SeekrIndex"); }
            finally { foreach (var service in services) service.Dispose(); }
            if (!exists) return;
            using var controller = new ServiceController("SeekrIndex");
            if (controller.Status != ServiceControllerStatus.Stopped) { controller.Stop(); controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)); }
            RunSc("delete", "SeekrIndex");
        });
    }

    public static void ValidateSource(string source)
    {
        foreach (var name in new[] { "Seekr.Elevated.exe", "Seekr.Elevated.dll", "Seekr.Elevated.deps.json", "Seekr.Elevated.runtimeconfig.json" })
            if (!File.Exists(Path.Combine(source, name))) throw new IOException("Неполный комплект службы: " + name);
    }

    public static void Install()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException("Установка службы требует прав администратора.");
        var source = Path.GetFullPath(AppContext.BaseDirectory);
        ValidateSource(source);
        var target = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Seekr", "Service"));
        if (source.TrimEnd('\\').Equals(target, StringComparison.OrdinalIgnoreCase)) throw new IOException("Запустите установку из комплекта приложения, а не из папки установленной службы.");
        using var controller = new ServiceController("SeekrIndex");
        var existing = ServiceController.GetServices();
        bool exists;
        try { exists = existing.Any(s => s.ServiceName == "SeekrIndex"); }
        finally { foreach (var service in existing) service.Dispose(); }
        var created = false;
        var wasRunning = exists && controller.Status != ServiceControllerStatus.Stopped;
        void Stop()
        {
            controller.Refresh();
            if (!ServiceProcessExists() || controller.Status == ServiceControllerStatus.Stopped) return;
            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }
        bool ServiceProcessExists() => exists || created;
        var binary = "\"" + Path.Combine(target, "Seekr.Elevated.exe") + "\"";
        PayloadDeployment.Install(source, target, Stop, () =>
        {
            RunSc(exists ? "config" : "create", "SeekrIndex", "binPath=", binary, "start=", "auto", "DisplayName=", "Seekr Index");
            created = true;
            RunSc("failure", "SeekrIndex", "reset=", "86400", "actions=", "restart/5000/restart/5000/restart/60000");
            RunSc("sidtype", "SeekrIndex", "unrestricted");
            RunSc("privs", "SeekrIndex", "SeChangeNotifyPrivilege/SeImpersonatePrivilege/SeBackupPrivilege/SeManageVolumePrivilege");
            controller.Refresh();
            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
        }, () =>
        {
            if (wasRunning) { controller.Refresh(); controller.Start(); controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30)); }
            else if (!exists && created) RunSc("delete", "SeekrIndex");
        });
    }

    private static void RunSc(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Не удалось запустить управление службой.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) throw new IOException("Превышено время настройки службы.");
        if (process.ExitCode != 0) throw new Win32Exception(process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }
}

