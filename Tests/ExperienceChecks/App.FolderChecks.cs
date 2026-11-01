using System.Reflection;
using Fallery.Controls;
using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static async Task RunFolderDiagnosticAsync(MainWindow window, string folder, string report)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var root = (FrameworkElement)window.Content;
        var scroller = (ScrollViewer)root.FindName("GalleryScroller");
        void Log(string text) => File.AppendAllText(report, $"\n{DateTime.Now:HH:mm:ss} {text}");
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../.."));
        typeof(MediaCache).GetProperty("Disk", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null,
            new ThumbnailDiskCache(Path.Combine(workspace, "work", "validation", "folder-diagnostics", Guid.NewGuid().ToString("N"))));
        window.GalleryState.DisplayMode = GalleryDisplayMode.Justified;
        window.GalleryState.SortField = MediaSortField.Name;
        window.GalleryState.Descending = false;
        ((Slider)root.FindName("ThumbSize")).Value = 480;
        Call("ApplyDisplayOptions");
        Log("real folder navigation start (muted, no input)");
        await ((Task)Call("NavigateAsync", folder, false, false)!).WaitAsync(TimeSpan.FromSeconds(60));
        Log($"navigation completed; items={window.GalleryState.Items.Count}");
        await Task.Delay(8000);
        Log("first viewport settled with initially empty application thumbnail cache");
        for (int step = 0; step <= 12; step++)
        {
            root.UpdateLayout();
            scroller.ChangeView(null, scroller.ScrollableHeight * step / 12, null, true);
            await Task.Delay(700);
            Log($"scroll {step}/12; memory={Environment.WorkingSet / 1024 / 1024} MiB");
        }
        var sample = window.GalleryState.MediaItems.FirstOrDefault(item => item.Name == "VID_20260612_150657.mp4");
        if (sample is not null)
        {
            Log("reported video thumbnail start");
            await sample.EnsureThumbnailAsync(480).WaitAsync(TimeSpan.FromSeconds(25));
            Log($"reported video thumbnail completed; failed={sample.ThumbnailFailed}");
            Call("BringItemIntoView", sample);
            await Task.Delay(500);
            var tiles = (Dictionary<UIElement, MediaItem>)typeof(MainWindow).GetField("_realizedItems", flags)!.GetValue(window)!;
            var tile = tiles.FirstOrDefault(pair => ReferenceEquals(pair.Value, sample)).Key as MediaTile;
            if (tile is not null)
            {
                Log("reported video muted hover start");
                Call("GalleryTile_PreviewHoverChanged", tile, true);
                await Task.Delay(2500);
                Call("StopHoverPreview");
                Log("reported video muted hover stopped");
            }
        }
        Log("PASS: real folder navigation, bounded scrolling and reported video; no activation or user input");
    }
}
