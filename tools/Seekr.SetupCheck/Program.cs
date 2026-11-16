using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Seekr.Setup;

if (args.Length != 2) { Console.WriteLine("Usage: SetupCheck current.msi previous.msi"); return 2; }
var package = Path.GetFullPath(args[0]); var previous = Path.GetFullPath(args[1]);
using var manifest=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(package)!,"release-manifest.json")));
var expectedVersion=manifest.RootElement.GetProperty("version").GetString()+"."+manifest.RootElement.GetProperty("build").GetInt32();
var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Seekr.lnk");
if (Installed() || File.Exists(desktop)) throw new InvalidOperationException("Existing installation/shortcut must not be modified.");
var root = Path.Combine(Path.GetTempPath(), "Seekr-setup-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var target = Path.Combine(root, "Новая папка"); var old = Path.Combine(root, "Предыдущая версия");
var currentInstalled = false;
try
{
    using var cancellation = new CancellationTokenSource();
    var cancelled = await MsiEngine.InstallAsync(package, new(target, true), Path.Combine(root,"cancel.log"), new ImmediateProgress(_ => cancellation.Cancel()), cancellation.Token);
    currentInstalled = Installed();
    if (cancelled.Code != 1602 || Installed() || File.Exists(Path.Combine(target,"Seekr.App.exe"))) throw new InvalidOperationException("Cancellation failed: " + cancelled.Code);
    Console.WriteLine("PASS native cancellation: 1602, no installed product or executable");
    RunMsi("/i", previous, "INSTALLFOLDER=\"" + old + "\" ADD_DESKTOP_SHORTCUT=1");
    currentInstalled = true;
    if (!File.Exists(desktop)) throw new InvalidOperationException("Previous shortcut missing.");
    Console.WriteLine("PASS installed previous version as upgrade fixture");
    var result = await MsiEngine.InstallAsync(package, new(target, false), Path.Combine(root,"upgrade.log"), new ImmediateProgress(_ => { }), CancellationToken.None);
    if (!result.Succeeded) throw new InvalidOperationException("Upgrade failed: " + result.Code + " " + result.Error);
    using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Seekr"))
    {
        if (!string.Equals((key?.GetValue("InstallDirectory") as string)?.TrimEnd('\\'),target,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Installer ignored selected upgrade directory.");
        if(key?.GetValue("DesktopShortcutPreference") as string != "0" || File.Exists(desktop)) throw new InvalidOperationException("Installer ignored desktop opt-out.");
    }
    if (!File.Exists(Path.Combine(target,"Seekr.App.exe")) || File.Exists(Path.Combine(old,"Seekr.App.exe"))) throw new InvalidOperationException("Upgrade files are in the wrong directory.");
    if(FileVersionInfo.GetVersionInfo(Path.Combine(target,"Seekr.App.exe")).FileVersion != expectedVersion) throw new InvalidOperationException("Wrong installed build.");
    Console.WriteLine("PASS native upgrade: new folder, version "+expectedVersion+", desktop opt-out overrides prior preference");
    var downgrade = await MsiEngine.InstallAsync(previous, new(target,false),Path.Combine(root,"downgrade.log"),new ImmediateProgress(_=>{}),CancellationToken.None);
    if(downgrade.Succeeded) throw new InvalidOperationException("Downgrade unexpectedly succeeded.");
    Console.WriteLine("PASS native downgrade blocked without hidden dialog");
    var document=Path.Combine(target,"test-document.txt"); File.WriteAllText(document,"Owned test fixture, must survive uninstall");
    RunMsi("/x",package,""); currentInstalled=false;
    if(Installed() || File.Exists(desktop) || File.Exists(Path.Combine(target,"Seekr.App.exe")) || !File.Exists(document)) throw new InvalidOperationException("Uninstall failed or removed document.");
    Console.WriteLine("PASS final uninstall, shortcut cleanup, user document preserved");
    Console.WriteLine("Logs: " + root);
    return 0;
}
finally
{
    if(currentInstalled) RunMsi("/x",File.Exists(Path.Combine(target,"Seekr.App.exe"))?package:previous,"");
}

static bool Installed()
{
    foreach(var hive in new[]{Registry.CurrentUser, Registry.LocalMachine})
    using(var uninstall=hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
    {
        if(uninstall is null) continue;
        foreach(var name in uninstall.GetSubKeyNames())
        using(var key=uninstall.OpenSubKey(name)) if(key?.GetValue("DisplayName") as string == "Seekr") return true;
    }
    return false;
}
static void RunMsi(string action,string package,string properties)
{
    using var process=Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"msiexec.exe"),action+" \""+package+"\" /qn /norestart "+properties){UseShellExecute=false,CreateNoWindow=true})!;
    process.WaitForExit(); if(process.ExitCode is not (0 or 3010)) throw new InvalidOperationException("MSI failed: "+process.ExitCode);
}
sealed class ImmediateProgress(Action<InstallProgress> callback):IProgress<InstallProgress>
{public void Report(InstallProgress value)=>callback(value);}
