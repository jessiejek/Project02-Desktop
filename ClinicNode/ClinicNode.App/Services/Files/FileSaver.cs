// JJQR - AI (Nov 21, 2026) - Mac Catalyst UIDocumentPicker export, Windows FileSavePicker; fallback to Documents/ClinicNode.
using ClinicNode.Core.Admin;

#if MACCATALYST
using Foundation;
using UIKit;
#endif

namespace ClinicNode.App.Services.Files;

public sealed class FileSaver : IFileSaver
{
    public async Task<string?> SaveAsync(string suggestedFileName, string contents, string contentType = "text/plain", CancellationToken ct = default)
    {
        var name = string.IsNullOrWhiteSpace(suggestedFileName) ? "download.txt" : suggestedFileName.Trim();
        try
        {
#if MACCATALYST
            return await TryMacExportAsync(name, contents);
#elif WINDOWS
            return await TryWindowsSaveAsync(name, contents, contentType);
#else
            return AdminLocalExport.WriteUtf8(name, contents);
#endif
        }
        catch
        {
            return AdminLocalExport.WriteUtf8(name, contents);
        }
    }

#if MACCATALYST
    static async Task<string?> TryMacExportAsync(string suggestedFileName, string contents)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ClinicNode-export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var tempPath = Path.Combine(dir, suggestedFileName);
        await File.WriteAllTextAsync(tempPath, contents);

        var tcs = new TaskCompletionSource<string?>();
        ExportPickerDelegate? del = null;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var url = NSUrl.FromFilename(tempPath);
            var picker = new UIDocumentPickerViewController(new[] { url }, asCopy: true);
            del = new ExportPickerDelegate(tcs);
            picker.Delegate = del;
            var vc = Platform.GetCurrentUIViewController();
            if (vc is null)
            {
                tcs.TrySetException(new InvalidOperationException("No view controller is available to present the save picker."));
                return;
            }
            vc.PresentViewController(picker, true, null);
        });
        try
        {
            return await tcs.Task;
        }
        finally
        {
            GC.KeepAlive(del);
        }
    }

    sealed class ExportPickerDelegate : UIDocumentPickerDelegate
    {
        readonly TaskCompletionSource<string?> _tcs;

        public ExportPickerDelegate(TaskCompletionSource<string?> tcs) => _tcs = tcs;

        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl[] urls)
        {
            _tcs.TrySetResult(urls.FirstOrDefault()?.Path);
            controller.DismissViewController(true, null);
        }

        public override void WasCancelled(UIDocumentPickerViewController controller)
        {
            _tcs.TrySetResult(null);
            controller.DismissViewController(true, null);
        }
    }
#endif

#if WINDOWS
    static async Task<string?> TryWindowsSaveAsync(string suggestedFileName, string contents, string contentType)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        var hwnd = (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as MauiWinUIWindow)?.WindowHandle ?? 0;
        if (hwnd != 0)
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName);
        var ext = Path.GetExtension(suggestedFileName);
        if (string.IsNullOrEmpty(ext)) ext = ".txt";
        var label = contentType.Contains("csv", StringComparison.OrdinalIgnoreCase) ? "CSV"
            : contentType.Contains("html", StringComparison.OrdinalIgnoreCase) ? "HTML"
            : "File";
        picker.FileTypeChoices.Add(label, new List<string> { ext });
        picker.DefaultFileExtension = ext;
        var file = await picker.PickSaveFileAsync();
        if (file is null) return null;
        await Windows.Storage.FileIO.WriteTextAsync(file, contents);
        return file.Path;
    }
#endif
}
// JJQR - END
