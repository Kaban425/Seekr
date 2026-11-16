using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Seekr.App.Services;

public sealed record LockingApplication(int ProcessId, long StartFileTime, string Name, bool CanClose);
public sealed record LockInspection(IReadOnlyList<LockingApplication> Applications, bool Partial);

public static class FileLockService
{
    public static LockInspection Inspect(IReadOnlyList<string> paths, CancellationToken token)
    {
        var files = new List<string>();
        var partial = false;
        var pending = new Stack<string>(paths);
        while (pending.TryPop(out var path))
        {
            token.ThrowIfCancellationRequested();
            if (files.Count >= 5000) { partial = true; break; }
            try
            {
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)
                    && (Directory.Exists(path) ? new DirectoryInfo(path).LinkTarget : new FileInfo(path).LinkTarget) is not null) { partial = true; continue; }
                if (Directory.Exists(path))
                {
                    foreach (var child in Directory.EnumerateFileSystemEntries(path))
                    {
                        token.ThrowIfCancellationRequested();
                        if (pending.Count + files.Count >= 5000) { partial = true; break; }
                        pending.Push(child);
                    }
                }
                else if (File.Exists(path)) files.Add(Path.GetFullPath(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { partial = true; }
        }
        if (files.Count == 0) return new([], partial);
        var error = RmStartSession(out var session, 0, new char[33]);
        if (error != 0) throw new Win32Exception(error);
        try
        {
            error = RmRegisterResources(session, (uint)files.Count, files.ToArray(), 0, 0, 0, 0);
            if (error != 0) throw new Win32Exception(error);
            uint count = 0;
            error = RmGetList(session, out var needed, ref count, null, out _);
            for (var attempt = 0; attempt < 4 && error == 234; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (needed > 4096) throw new IOException("Слишком много процессов удерживают выбранные файлы.");
                var list = new ProcessInfo[needed];
                count = needed;
                error = RmGetList(session, out needed, ref count, list, out _);
                if (error == 0)
                {
                    using var current = Process.GetCurrentProcess();
                    return new(list.Take((int)count).Select(info => new LockingApplication((int)info.Process.Id,
                        ((long)info.Process.High << 32) | info.Process.Low, info.Name,
                        info.Kind is 1 or 2 && info.Session == current.SessionId && info.Process.Id != Environment.ProcessId)).ToArray(), partial);
                }
            }
            if (error != 0) throw new Win32Exception(error);
            return new([], partial);
        }
        finally { _ = RmEndSession(session); }
    }

    public static async Task<string> CloseAsync(LockingApplication app, bool force, CancellationToken token)
    {
        if (!app.CanClose || app.ProcessId == Environment.ProcessId) return "Этот процесс нельзя закрыть из Seekr.";
        try
        {
            using var process = Process.GetProcessById(app.ProcessId);
            using var current = Process.GetCurrentProcess();
            if (process.StartTime.ToFileTimeUtc() != app.StartFileTime || process.SessionId != current.SessionId
                || process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)
                || process.MainWindowHandle == 0 || !IsProcessCritical(process.Handle, out var critical) || critical)
                return "Процесс изменился или защищён. Закрытие отменено.";
            if (force) process.Kill(entireProcessTree: false);
            else if (!process.CloseMainWindow()) return "Приложение не приняло запрос на закрытие.";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return "Приложение ещё работает. Возможно, оно ожидает сохранения документа."; }
            return "Приложение закрыто. Можно повторить удаление.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        { return "Не удалось закрыть приложение: " + ex.Message; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UniqueProcess { public uint Id; public uint Low; public uint High; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessInfo
    {
        public UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Service;
        public int Kind;
        public uint Status;
        public uint Session;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmStartSession(out uint session, int flags, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2, SizeConst = 33)] char[] key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmRegisterResources(uint session, uint count, string[] files, uint appCount, nint apps, uint serviceCount, nint services);
    [DllImport("rstrtmgr.dll")] private static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] ProcessInfo[]? apps, out uint reasons);
    [DllImport("rstrtmgr.dll")] private static extern int RmEndSession(uint session);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsProcessCritical(nint process, [MarshalAs(UnmanagedType.Bool)] out bool critical);
}
