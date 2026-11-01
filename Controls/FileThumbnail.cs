using System.ComponentModel;
using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Fallery.Controls;

/// <summary>虚拟化列表的小预览；只为已加载的控件取图，复用和卸载时释放旧请求。</summary>
public sealed partial class FileThumbnail : Grid
{
    public static readonly DependencyProperty FileProperty = DependencyProperty.Register(
        nameof(File), typeof(MediaFile), typeof(FileThumbnail),
        new PropertyMetadata(null, (sender, _) => ((FileThumbnail)sender).Reload()));

    public MediaFile? File
    {
        get => (MediaFile?)GetValue(FileProperty);
        set => SetValue(FileProperty, value);
    }

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _status = new()
    {
        Text = "加载中", FontSize = 10, TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center
    };
    private MediaItem? _item;
    private bool _loaded;

    public FileThumbnail()
    {
        Children.Add(_image);
        Children.Add(_status);
        Loaded += (_, _) => { _loaded = true; Reload(); };
        Unloaded += (_, _) => { _loaded = false; Release(); };
    }

    private void Reload()
    {
        Release();
        if (!_loaded || File is not { } file) return;
        _item = new MediaItem(file);
        _item.PropertyChanged += ItemChanged;
        // 与浏览区共用六并发、内存/磁盘缓存，128px 覆盖高 DPI 下的 64 DIP 小图。
        _ = _item.EnsureThumbnailAsync(128);
    }

    private void ItemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!ReferenceEquals(sender, _item)) return;
        _image.Source = _item?.Thumbnail;
        _status.Text = _item?.ThumbnailFailed == true ? "无法预览" : "加载中";
        _status.Visibility = _image.Source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Release()
    {
        _image.Source = null;
        _status.Text = "加载中";
        _status.Visibility = Visibility.Visible;
        if (_item is not null)
        {
            _item.PropertyChanged -= ItemChanged;
            _item.ReleaseThumbnail();
            _item = null;
        }
    }
}
