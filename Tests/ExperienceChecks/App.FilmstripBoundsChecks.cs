using System.Reflection;
using Fallery.Controls;
using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static async Task<int> RunFilmstripBoundsChecksAsync(MainWindow window)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var root = (FrameworkElement)window.Content;
        var gallery = (ItemsRepeater)root.FindName("Gallery");
        var scroller = (ScrollViewer)root.FindName("GalleryScroller");
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 700));
        await (Task)Call("SetDisplayModeAsync", GalleryDisplayMode.Filmstrip)!;
        bool originalNames = window.GalleryState.ShowFileNames, originalCards = window.GalleryState.ShowCards;
        try
        {
            foreach (bool names in new[] { true, false })
            foreach (bool cards in new[] { true, false })
            {
                window.GalleryState.ShowFileNames = names;
                window.GalleryState.ShowCards = cards;
                Call("ApplyDisplayOptions");
                root.UpdateLayout(); await Task.Delay(100); root.UpdateLayout();
                var presenter = Descendants(scroller).OfType<ScrollContentPresenter>().First();
                var tiles = window.GalleryState.Items.Select((_, index) => gallery.TryGetElement(index)).OfType<MediaTile>().ToArray();
                Check(tiles.Length > 0 && scroller.ScrollableWidth > 0, "横排边界检查应包含真实卡片及横向溢出");
                foreach (var tile in tiles)
                {
                    var bounds = tile.TransformToVisual(presenter).TransformBounds(new Rect(0, 0, tile.ActualWidth, tile.ActualHeight));
                    Check(bounds.Top >= -0.5 && bounds.Bottom <= presenter.ActualHeight + 0.5,
                        $"横排卡片不能被视口裁剪：names={names}, cards={cards}, bottom={bounds.Bottom}, viewport={presenter.ActualHeight}");
                }
                Check(scroller.ActualHeight >= gallery.ActualHeight + scroller.Padding.Top + scroller.Padding.Bottom,
                    "横排缩略图栏应容纳整张卡片和滚动条留白");
            }
        }
        finally
        {
            window.GalleryState.ShowFileNames = originalNames;
            window.GalleryState.ShowCards = originalCards;
            await (Task)Call("SetDisplayModeAsync", GalleryDisplayMode.Masonry)!;
        }
        Check(double.IsNaN(gallery.Height) && scroller.Padding == new Thickness(12), "退出横排应恢复原视图高度和留白");
        return checks;
    }
}
