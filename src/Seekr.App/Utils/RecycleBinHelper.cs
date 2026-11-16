using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace Seekr.App.Utils;

public static class RecycleBinHelper
{
    public sealed record DeletionResult(bool Success, string? Error);
    public static async Task<bool> ExecuteAsync(string path, bool permanently)
        => (await ExecuteWithResultAsync(path, permanently)).Success;

    public static Task<DeletionResult> ExecuteWithResultAsync(string path, bool permanently)
    {
        var completion = new TaskCompletionSource<DeletionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                var flags = FileOperationFlags.NoConnectedElements | FileOperationFlags.NoConfirmation | FileOperationFlags.NoErrorUi | FileOperationFlags.Silent;
                if (!permanently) flags |= FileOperationFlags.AllowUndo | FileOperationFlags.WantNukeWarning;
                completion.SetResult(ExecuteDetailed(flags, path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { completion.SetResult(new(false, ex.Message)); }
        }) { IsBackground = true, Name = "Seekr file operation" };
        worker.SetApartmentState(ApartmentState.STA); worker.Start();
        return completion.Task;
    }

    private const uint FileOperationSilent = 0x0004;
    private const uint FileOperationNoConfirmation = 0x0010;
    private const uint FileOperationNoErrorUi = 0x0400;
    private const uint FileOperationAllowUndo = 0x0040;

    public static bool MoveToRecycleBin(string path)
    {
        // ALLOWUNDO is best-effort. Ask through the shell if a permanent delete would be needed.
        return Execute(FileOperationFlags.AllowUndo | FileOperationFlags.WantNukeWarning
            | FileOperationFlags.NoConnectedElements | FileOperationFlags.NoConfirmation
            | FileOperationFlags.NoErrorUi | FileOperationFlags.Silent, path);
    }

    public static bool DeletePermanently(string path)
    {
        return Execute(FileOperationFlags.NoConnectedElements | FileOperationFlags.NoConfirmation | FileOperationFlags.NoErrorUi | FileOperationFlags.Silent, path);
    }

    private static bool Execute(FileOperationFlags flags, string path) => ExecuteDetailed(flags, path).Success;
    private static DeletionResult ExecuteDetailed(FileOperationFlags flags, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
            || path.IndexOfAny(['\0', '*', '?']) >= 0
            || string.Equals(Path.GetPathRoot(path)?.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return new(false, "Путь недоступен, содержит недопустимые символы или является корнем диска.");
        }

        if (flags.HasFlag(FileOperationFlags.AllowUndo)) return RecycleOnly.Delete(Path.GetFullPath(path));

        var fileOp = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + '\0' + '\0',
            fFlags = (ushort)flags,
        };

        var result = SHFileOperation(ref fileOp);
        if (fileOp.fAnyOperationsAborted) return new(false, "Операция отменена Windows или пользователем.");
        if (result == 0) return new(true, null);
        var reason = result switch
        {
            5 or 0x78 => "Нет доступа к файлу. Проверьте права и защиту папки.",
            32 or 33 => "Файл занят другим приложением. Нажмите «Кто использует?».",
            0x74 => "Путь назначения или источник находится в корне диска.",
            _ => new System.ComponentModel.Win32Exception(result).Message,
        };
        return new(false, $"{reason} (код Windows: 0x{result:X}).");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEOPSTRUCT
    {
        public nint hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public nint hNameMappings;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT fileOp);

    private const uint FO_DELETE = 0x0003;

    [Flags]
    private enum FileOperationFlags : ushort
    {
        Silent = 0x0004,
        AllowUndo = 0x0040,
        NoConfirmation = 0x0010,
        NoErrorUi = 0x0400,
        NoConnectedElements = 0x2000,
        WantNukeWarning = 0x4000,
    }
}
