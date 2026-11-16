using Seekr.Elevated;

if (args.Length == 1 && args[0] is "--install" or "--uninstall" or "--check-package")
{
    try
    {
        if (args[0] == "--install") ServiceInstaller.Install();
        else if (args[0] == "--uninstall") ServiceInstaller.Uninstall();
        else ServiceInstaller.ValidateSource(AppContext.BaseDirectory);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException or System.ServiceProcess.TimeoutException)
    {
        if (args[0] is "--install" or "--uninstall")
        {
            var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Seekr", "setup.log");
            try { Directory.CreateDirectory(Path.GetDirectoryName(log)!); File.WriteAllText(log, ex.ToString()); }
            catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException) { Console.Error.WriteLine(writeError.Message); }
        }
        Console.Error.WriteLine(ex.Message);
        Environment.ExitCode = 1;
    }
    return;
}

// SeekrIndex: a small Windows service that reads the NTFS master file table (which needs
// administrator rights) and hands each user a list filtered to what that user may see.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "SeekrIndex");
builder.Services.AddHostedService<PipeServer>();

var host = builder.Build();
host.Run();
