using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text.Json;
using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private async Task CaptureReadmeScreenshotsAsync(string workspace, string report)
    {
        string demo = Path.Combine(workspace, "work", "readme-demo");
        string samples = Path.Combine(demo, "Sample Library");
        string output = Path.Combine(workspace, "docs", "screenshots");
        if (Directory.GetFiles(samples, "*.png").Length != 15)
            throw new InvalidOperationException("Run tools/Generate-ReadmeSamples.py first.");
        var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(demo, "manifest.json")))!;
        if (manifest.Count != 24 || (File.GetAttributes(samples) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Unexpected sample library.");
        var folders = Directory.GetDirectories(samples);
        if (!folders.Select(Path.GetFileName).Order().SequenceEqual(new[] { "Coastlines", "Color studies", "Mountain sketches" }))
            throw new InvalidOperationException("Unexpected sample folders; refusing capture.");
        var files = Directory.GetFiles(samples).ToList();
        foreach (string folder in folders)
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0 || Directory.GetDirectories(folder).Length != 0)
                throw new InvalidOperationException("Demo folders must not contain links or nested folders.");
            files.AddRange(Directory.GetFiles(folder));
        }
        if (files.Count != manifest.Count) throw new InvalidOperationException("Unexpected sample files; refusing capture.");
        foreach (string file in files)
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0
                || !manifest.TryGetValue(Path.GetRelativePath(samples, file).Replace('\\', '/'), out var expected)
                || !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Sample file differs from generated artwork; refusing capture.");
        }
        Directory.CreateDirectory(output);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        // This branch runs before normal test setup: no user settings, roots, search or resume history are loaded.
        typeof(MediaCache).GetProperty("Disk", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null,
            new ThumbnailDiskCache(Path.Combine(demo, "cache")));
        var store = new AppSettingsStore(Path.Combine(demo, "settings.json"));
        await store.SaveAsync(new AppSettings
        {
            Width = 1280, Height = 840, Theme = 2, Language = "en-US", BackgroundOpacity = 100,
            ThumbnailSize = 230, SidebarWidth = 216, DisplayMode = GalleryDisplayMode.Masonry,
            Muted = true, Volume = 0, VideoWindowDefaultsApplied = true,
        });
        AppText.SetLanguage("en-US");
        var window = new MainWindow(store, monitorFolderChanges: false);
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        async Task Invoke(string name, params object[] arguments) => await ((Task)typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, arguments)!).WaitAsync(TimeSpan.FromSeconds(30));
        var root = (Grid)window.Content;
        try
        {
            Set("_initialized", true);
            Set("_rootsTask", Task.CompletedTask);
            var bindings = typeof(MainWindow).GetField("Bindings", flags)!.GetValue(window)!;
            bindings.GetType().GetMethod("Initialize")!.Invoke(bindings, null);
            await Invoke("RestoreSettingsAsync");
            window.SystemBackdrop = null;
            root.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 29, 33, 42));
            ((TitleBar)root.FindName("AppTitleBar")).Title = "Fallery";
            ((Slider)root.FindName("ThumbSize")).IsThumbToolTipEnabled = false;
            var tree = (TreeView)root.FindName("FolderTree");
            tree.RootNodes.Clear();
            var sampleNode = new TreeViewNode { Content = new FolderEntry(samples, "Sample Library"), IsExpanded = true };
            foreach (string folder in Directory.GetDirectories(samples).Order())
                sampleNode.Children.Add(new TreeViewNode { Content = new FolderEntry(folder, Path.GetFileName(folder)) });
            tree.RootNodes.Add(sampleNode);
            await Invoke("NavigateAsync", samples, false, false);
            // The fixture exposes only its own friendly root, never workspace/drive/user ancestors.
            typeof(Fallery.ViewModels.GalleryViewModel).GetProperty("Breadcrumbs")!.SetValue(window.GalleryState,
                new List<FolderEntry> { new(samples, "Sample Library") });
            typeof(MainWindow).GetMethod("RefreshAddressBar", flags)!.Invoke(window, null);
            await Capture("masonry-dark.png");

            await Invoke("SetDisplayModeAsync", GalleryDisplayMode.Filmstrip);
            await Invoke("ShowInViewerAsync", 4, true);
            await Capture("filmstrip-dark.png");

            await Invoke("SetDisplayModeAsync", GalleryDisplayMode.Justified);
            typeof(MainWindow).GetMethod("ApplyTheme", flags)!.Invoke(window, [1]);
            root.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 243, 245, 247));
            AppText.SetLanguage("zh-CN");
            await Capture("justified-light-zh.png");
            File.AppendAllText(report, "\nPASS: three README captures from generated art and isolated settings; no desktop capture");

            async Task Capture(string name)
            {
                root.Measure(new Windows.Foundation.Size(1280, 840));
                root.Arrange(new Windows.Foundation.Rect(0, 0, 1280, 840));
                root.UpdateLayout();
                // Real production thumbnail decoding/virtualization runs before the XAML snapshot.
                foreach (var item in window.GalleryState.Items.Take(20))
                    await item.EnsureThumbnailAsync(320);
                await Task.Delay(800);
                root.UpdateLayout();
                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(root, 1280, 840);
                var pixels = await bitmap.GetPixelsAsync();
                if (bitmap.PixelWidth != 1280 || bitmap.PixelHeight != 840) throw new InvalidOperationException("Unexpected capture dimensions");
                using var stream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, 1280, 840, 96, 96, pixels.ToArray());
                await encoder.FlushAsync();
                stream.Seek(0);
                using var file = File.Create(Path.Combine(output, name));
                await stream.AsStreamForRead().CopyToAsync(file);
                File.AppendAllText(report, "\nCAPTURED " + name);
            }
        }
        finally
        {
            Set("_allowClose", true);
            window.Close();
        }
    }
}
