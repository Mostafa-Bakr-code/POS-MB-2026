namespace POS_MB.Cashier.Services;

// A UI-shell concern local to this app, not something POS-MB.Printing needs
// to know about - SaveFileDialog (what WinForms uses) has no Android
// equivalent, so the two platforms need genuinely different hand-off
// mechanisms for the same server-generated .xlsx bytes.
public static class ExportFileHandler
{
    public static async Task SaveAsync(byte[] bytes, string suggestedFileName)
    {
#if WINDOWS
        await SaveOnWindowsAsync(bytes, suggestedFileName);
#else
        await ShareOnAndroidAsync(bytes, suggestedFileName);
#endif
    }

#if WINDOWS
    private static async Task SaveOnWindowsAsync(byte[] bytes, string suggestedFileName)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.FileTypeChoices.Add("Excel Workbook", [".xlsx"]);
        picker.SuggestedFileName = suggestedFileName;

        // FileSavePicker needs a window handle on unpackaged Windows apps -
        // there's no other way to associate the picker with this app's
        // window than reaching through MauiWinUIWindow like this.
        var window = Microsoft.Maui.Controls.Application.Current!.Windows[0];
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window.Handler!.PlatformView);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return; // user cancelled

        await Windows.Storage.FileIO.WriteBytesAsync(file, bytes);
    }
#else
    private static async Task ShareOnAndroidAsync(byte[] bytes, string suggestedFileName)
    {
        // The share sheet needs a real file path, not raw bytes - write it
        // to the cache directory first, same as any other MAUI share flow.
        var path = Path.Combine(FileSystem.Current.CacheDirectory, suggestedFileName);
        await File.WriteAllBytesAsync(path, bytes);

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = suggestedFileName,
            File = new ShareFile(path)
        });
    }
#endif
}
