using Fallery.Controls;
using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Fallery.TileChecks;

public sealed partial class GalleryHarness : UserControl
{
    private List<MediaItem> _items = [];
    private int _prepared;
    private int _cleared;

    public GalleryHarness()
    {
        InitializeComponent();
        Gallery.ElementPrepared += (_, args) =>
        {
            ((MediaTile)args.Element).Bind(_items[args.Index]);
            _prepared++;
        };
        Gallery.ElementClearing += (_, args) =>
        {
            ((MediaTile)args.Element).Bind(null);
            _cleared++;
        };
    }

    public async Task RunAsync(Action<bool, string> check)
    {
        // 隐藏测试窗口仅承载 XAML 布局，不激活、不发送用户输入。
        var window = new Window { Content = this };
        try
        {
            for (int round = 0; round < 4; round++)
            {
                for (int i = 0; i < _items.Count; i++)
                    if (Gallery.TryGetElement(i) is MediaTile oldTile) oldTile.Bind(null);
                Gallery.ItemsSource = null;
                _items = Enumerable.Range(0, 100).Select(i => new MediaItem($@"E:\harness\{round}\{i}.mp4", MediaKind.Video)).ToList();
                Gallery.ItemsSource = _items;
                var firstTile = (MediaTile)Gallery.GetOrCreateElement(0);
                check(ReferenceEquals(firstTile.Item, _items[0]), $"替换目录后复用了已清空的旧卡片: round={round}");
                for (int pass = 0; pass < 12; pass++)
                {
                    foreach (var item in _items) item.AspectRatio = pass % 2 == 0 ? 0.6 : 1.8;
                    Masonry.Refresh();
                    double width = pass % 3 == 0 ? 800 : 1100;
                    Measure(new Size(width, 600));
                    Arrange(new Rect(0, 0, width, 600));
                    UpdateLayout();
                    Scroller.ChangeView(null, pass % 2 == 0 ? 0 : 3000, null, true);
                    await Task.Delay(30);
                    UpdateLayout();
                    for (int i = 0; i < _items.Count; i++)
                    {
                        if (Gallery.TryGetElement(i) is not MediaTile tile) continue;
                        check(ReferenceEquals(tile.Item, _items[i]), $"列表回收后绑定丢失: round={round}, pass={pass}, index={i}");
                        check(((Border)tile.FindName("VideoBadge")).Visibility == Visibility.Visible, "已实现的视频卡片角标丢失");
                    }
                }
            }
            check(_prepared > 0 && _cleared > 0, $"真实列表必须经过准备和清理: prepared={_prepared}, cleared={_cleared}");
        }
        finally { window.Close(); }
    }
}
