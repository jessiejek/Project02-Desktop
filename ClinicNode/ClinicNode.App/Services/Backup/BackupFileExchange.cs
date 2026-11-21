//JJQR - AI (Nov 21, 2026) - hands a finished backup file to the user and picks one for restore.
// Mac Catalyst: UIDocumentPickerViewController in export mode (works inside the App Sandbox with user-selected files; NSSavePanel is not
// available to Catalyst). Windows: FolderPicker, then copy. Restore uses MAUI FilePicker (UIDocumentPicker / FileOpenPicker).
namespace ClinicNode.App.Services.Backup;

public interface IBackupFileExchange
{
    /// <summary>Lets the user choose where <paramref name="tempFile"/> goes. Returns the saved location, or null when cancelled.</summary>
    Task<string?> ExportAsync(string tempFile, string suggestedName, CancellationToken ct = default);
    /// <summary>Picks a .cnbackup file; returns a readable local path (copied into the cache on platforms that hand out streams), or null.</summary>
    Task<string?> PickBackupAsync(CancellationToken ct = default);
}

public sealed class BackupFileExchange : IBackupFileExchange
{
    public async Task<string?> ExportAsync(string tempFile, string suggestedName, CancellationToken ct = default)
    {
#if MACCATALYST
        // the picker exports the file under its own name, so give the temp copy the suggested one
        var named = Path.Combine(Path.GetDirectoryName(tempFile)!, suggestedName);
        if (!string.Equals(named, tempFile, StringComparison.Ordinal)) File.Move(tempFile, named, overwrite: true);
        try
        {
            return await MainThread.InvokeOnMainThreadAsync(() => MacExportAsync(named));
        }
        finally { if (File.Exists(named)) File.Delete(named); }
#elif WINDOWS
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var picker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
            var window = (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window)
                ?? throw new InvalidOperationException("No window to anchor the folder picker.");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return null;
            var target = Path.Combine(folder.Path, suggestedName);
            File.Copy(tempFile, target, overwrite: false);
            return target;
        });
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException("Backup export is implemented for Mac Catalyst and Windows.");
#endif
    }

    public async Task<string?> PickBackupAsync(CancellationToken ct = default)
    {
        var type = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.MacCatalyst] = ["public.data"],
            [DevicePlatform.WinUI] = [ClinicNode.Core.NodeSettings.BackupFormat.FileExtension],
        });
        var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a ClinicNode backup", FileTypes = type });
        if (picked is null) return null;
        var local = Path.Combine(FileSystem.CacheDirectory, "restore-" + Guid.NewGuid().ToString("N") + ClinicNode.Core.NodeSettings.BackupFormat.FileExtension);
        await using (var src = await picked.OpenReadAsync())
        await using (var dst = File.Create(local))
            await src.CopyToAsync(dst, ct);
        return local;
    }

#if MACCATALYST
    static Task<string?> MacExportAsync(string file)
    {
        var tcs = new TaskCompletionSource<string?>();
        var picker = new UIKit.UIDocumentPickerViewController(new[] { Foundation.NSUrl.FromFilename(file) }, asCopy: true);
        picker.DidPickDocumentAtUrls += (_, e) => tcs.TrySetResult(e.Urls.FirstOrDefault()?.Path);
        picker.WasCancelled += (_, _) => tcs.TrySetResult(null);
        var top = TopController() ?? throw new InvalidOperationException("No window to present the export picker from.");
        top.PresentViewController(picker, true, null);
        return tcs.Task;
    }

    static UIKit.UIViewController? TopController()
    {
        var window = UIKit.UIApplication.SharedApplication.ConnectedScenes.OfType<UIKit.UIWindowScene>()
            .SelectMany(s => s.Windows).FirstOrDefault(w => w.IsKeyWindow);
        var vc = window?.RootViewController;
        while (vc?.PresentedViewController is { } p) vc = p;
        return vc;
    }
#endif
}
//JJQR - END
