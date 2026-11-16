using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Seekr.Setup;

public sealed record InstallProgress(double Percent, bool Indeterminate, string Status);
public sealed record InstallResult(uint Code, string? Error)
{
    public bool Succeeded => Code is 0 or 3010;
    public bool Cancelled => Code == 1602;
}

public static class MsiEngine
{
    // MSI UI/log configuration is process-global. Only one installation is allowed per process.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static async Task<InstallResult> InstallAsync(string package, SetupOptions options, string log,
        IProgress<InstallProgress> progress, CancellationToken token)
    {
        var properties = options.Properties;
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(() => Install(package, properties, log, progress, token), CancellationToken.None).ConfigureAwait(false); }
        finally { Gate.Release(); }
    }

    private static InstallResult Install(string package, string properties, string log, IProgress<InstallProgress> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var model = new MsiProgress();
        string? error = null;
        var status = "Подготовка установки…";
        var lastUpdate = Stopwatch.StartNew();
        var cancelled = false;
        Native.Handler callback = (_, type, record) =>
        {
            try
            {
                var kind = type & 0xff000000;
                if (kind is 0x05000000 or 0x19000000)
                { error = "Файлы заняты. Закройте Seekr и повторите установку."; return 2; }
                if (kind is 0x00000000 or 0x01000000) error = Format(record);
                if (!cancelled && token.IsCancellationRequested && kind is 0x0a000000 or 0x09000000)
                { cancelled = true; return 2; }
                var changed = false;
                if (kind == 0x08000000) { status = Read(record, 2); changed = true; }
                if (kind == 0x0a000000) model.Update(Native.MsiRecordGetInteger(record, 1), Native.MsiRecordGetInteger(record, 2), Native.MsiRecordGetInteger(record, 3));
                if (kind == 0x09000000) model.ActionData();
                if (changed || lastUpdate.ElapsedMilliseconds >= 120)
                { progress.Report(new(model.Percent, !model.HasTotal, cancelled ? "Отмена и восстановление…" : status)); lastUpdate.Restart(); }
                return 1;
            }
#pragma warning disable CA1031 // No managed exception may cross the native MSI callback boundary.
            catch { return -1; }
#pragma warning restore CA1031
        };
        var oldLevel = Native.MsiSetInternalUI(2, IntPtr.Zero); // INSTALLUILEVEL_NONE
        IntPtr previous = IntPtr.Zero;
        try
        {
            var code = Native.MsiEnableLog(0x1fff, log, 0);
            if (code != 0) return new(code, "Не удалось создать журнал установки.");
            code = Native.MsiSetExternalUIRecord(callback, 0x02000fff, IntPtr.Zero, out previous);
            if (code != 0) return new(code, "Не удалось подключить интерфейс установки.");
            code = Native.MsiInstallProduct(package, properties);
            GC.KeepAlive(callback);
            return new(code, error);
        }
        finally
        {
            _ = Native.RestoreExternalUI(previous, 0, IntPtr.Zero, out _);
            _ = Native.MsiSetInternalUI(oldLevel, IntPtr.Zero);
            _ = Native.MsiEnableLog(0, null, 0);
        }
    }

    private static string Read(uint record, uint field)
    {
        var text = new char[8192]; uint length = (uint)text.Length;
        var code = Native.MsiRecordGetString(record, field, text, ref length);
        return code == 0 ? new string(text, 0, (int)length) : "Установка…";
    }
    private static string Format(uint record)
    {
        var text = new char[8192]; uint length = (uint)text.Length;
        var code = Native.MsiFormatRecord(0, record, text, ref length);
        return code == 0 ? new string(text, 0, (int)length) : "Установка не завершена. Подробности находятся в журнале.";
    }
    private static class Native
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] internal delegate int Handler(IntPtr context, uint type, uint record);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] internal static extern uint MsiInstallProduct(string package, string properties);
        [DllImport("msi.dll")] internal static extern uint MsiSetInternalUI(uint level, IntPtr window);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] internal static extern uint MsiEnableLog(uint mode, string? path, uint attributes);
        [DllImport("msi.dll")] internal static extern uint MsiSetExternalUIRecord(Handler handler, uint filter, IntPtr context, out IntPtr previous);
        [DllImport("msi.dll", EntryPoint = "MsiSetExternalUIRecord")] internal static extern uint RestoreExternalUI(IntPtr handler, uint filter, IntPtr context, out IntPtr previous);
        [DllImport("msi.dll")] internal static extern int MsiRecordGetInteger(uint record, uint field);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] internal static extern uint MsiRecordGetString(uint record, uint field, [Out] char[] text, ref uint length);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] internal static extern uint MsiFormatRecord(uint install, uint record, [Out] char[] text, ref uint length);
    }
}
