using System.Runtime.InteropServices;

namespace Seekr.App.Utils;

/// <summary>Recycle-only shell operation. Never requests Windows confirmation or permanent fallback.</summary>
internal static class RecycleOnly
{
    public static RecycleBinHelper.DeletionResult Delete(string path)
    {
        IShellItem? shellItem = null; IFileOperation? operation = null;
        try
        {
            var iid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ref iid, out shellItem));
            operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), true)!)!;
            operation.SetOperationFlags(0x00080000 | 0x00100000 | 0x0004 | 0x0010 | 0x0400 | 0x2000);
            operation.DeleteItem(shellItem, 0); operation.PerformOperations(); operation.GetAnyOperationsAborted(out var aborted);
            return aborted || System.IO.File.Exists(path) || System.IO.Directory.Exists(path) ? new(false, "Не удалось поместить в корзину. При необходимости выберите «Удалить безвозвратно» отдельно.") : new(true, null);
        }
        catch (COMException ex) { return new(false, $"Не удалось поместить в корзину (0x{ex.HResult:X}). Файл не удаляется безвозвратно автоматически."); }
        finally
        {
            if (shellItem is not null && Marshal.IsComObject(shellItem)) Marshal.FinalReleaseComObject(shellItem);
            if (operation is not null && Marshal.IsComObject(operation)) Marshal.FinalReleaseComObject(operation);
        }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, out IShellItem item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem { }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(nint sink, out uint cookie); void Unadvise(uint cookie);
        void SetOperationFlags(uint flags); void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetProgressDialog(nint dialog); void SetProperties(nint properties); void SetOwnerWindow(nint window);
        void ApplyPropertiesToItem(nint item); void ApplyPropertiesToItems(nint items);
        void RenameItem(nint item, nint name, nint sink); void RenameItems(nint items, nint name);
        void MoveItem(nint item, nint destination, nint name, nint sink); void MoveItems(nint items, nint destination);
        void CopyItem(nint item, nint destination, nint name, nint sink); void CopyItems(nint items, nint destination);
        void DeleteItem(IShellItem item, nint sink); void DeleteItems(nint items);
        void NewItem(nint destination, uint attributes, nint name, nint template, nint sink);
        void PerformOperations(); void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}

