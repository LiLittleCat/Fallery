using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Fallery.Controls;

/// <summary>代码里创建的图标同样走 Themes/Icons.xaml，避免回落到随系统变化的 Segoe 图标字体。</summary>
public static class FluentIcons
{
    public static string Glyph(string key) => (string)Application.Current.Resources[key];

    public static FontIcon Create(string key, double size = 16, bool filled = false) => new()
    {
        Glyph = Glyph(key),
        FontFamily = (FontFamily)Application.Current.Resources[filled ? "FluentIconFilledFont" : "FluentIconFont"],
        FontSize = size,
    };
}

/// <summary>文件树节点图标种类：磁盘、图片库和普通文件夹分别显示，与资源管理器的习惯一致。</summary>
public sealed partial class FolderIconConverter : IValueConverter
{
    private static readonly string Pictures = Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));

    public static string KindOf(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        if (string.Equals(trimmed, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path) ?? ""), StringComparison.OrdinalIgnoreCase)) return "Drive";
        return string.Equals(trimmed, Pictures, StringComparison.OrdinalIgnoreCase) ? "Pictures" : "Folder";
    }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string? path = value is Models.FolderEntry entry ? entry.Path : value as string;
        return path is not null && KindOf(path) == (string)parameter ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class NonEmptyTextVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
