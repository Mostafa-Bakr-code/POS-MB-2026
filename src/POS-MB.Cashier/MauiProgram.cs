using Microsoft.Extensions.Logging;
using POS_MB.Printing;

namespace POS_MB.Cashier;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		// Environment.SpecialFolder.LocalApplicationData (PrinterSettings'
		// own default) doesn't reliably resolve to a writable path on
		// Android - FileSystem.Current.AppDataDirectory is MAUI's
		// cross-platform equivalent, so PrinterSettings.Load()/Save() work
		// the same way here as they do in the WinForms app.
		PrinterSettings.BaseDirectoryProvider = () => FileSystem.Current.AppDataDirectory;

		return builder.Build();
	}
}
