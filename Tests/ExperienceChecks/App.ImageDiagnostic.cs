using System.Reflection;
using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static async Task RunImageDiagnosticAsync(MainWindow window, string path, string report)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var root = (FrameworkElement)window.Content;
        void Log(string stage) => File.AppendAllText(report, "\n" + stage);
        Log("IMAGE probe");
        var size = await MediaProbe.TryGetImageSizeAsync(path);
        Log($"IMAGE size: {size}");
        Log("IMAGE details start");
        var details = await MediaDetailsReader.ReadAsync(path, MediaKind.Image, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(15));
        if (size is not { } dimensions || details.Width != dimensions.Width || details.Height != dimensions.Height)
            throw new InvalidOperationException("Sample details lost image dimensions");
        Log($"IMAGE details: {details.Resolution}");
        window.ViewerState.SetPlaylist([new MediaItem(path, MediaKind.Image, FileVersion.Read(path))]);
        for (int round = 0; round < 3; round++)
        {
            Log($"IMAGE open {round}");
            await ((Task)typeof(MainWindow).GetMethod("ShowInViewerAsync", flags)!.Invoke(window, [0, true])!).WaitAsync(TimeSpan.FromSeconds(15));
            Log($"IMAGE opened: {window.ViewerState.Message}; source={((Image)root.FindName("ViewerImage")).Source?.GetType().Name}");
            root.UpdateLayout(); await Task.Delay(500); root.UpdateLayout();
            if (((Image)root.FindName("ViewerImage")).Source is not BitmapImage bitmap || bitmap.PixelWidth <= 0)
                throw new InvalidOperationException("Sample did not decode: " + window.ViewerState.Message);
            Log($"IMAGE rendered {bitmap.PixelWidth} x {bitmap.PixelHeight}");
            typeof(MainWindow).GetMethod("CloseViewer", flags)!.Invoke(window, null);
            Log("IMAGE closed");
        }
        Log("PASS: sample image open/render/close three rounds");
    }
}
