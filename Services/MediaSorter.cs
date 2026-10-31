using Fallery.Models;

namespace Fallery.Services;

public static class MediaSorter
{
    public static List<T> Sort<T>(IEnumerable<T> items, Func<T, MediaFile> metadata, MediaSortField field, bool descending)
    {
        var result = items.ToList();
        result.Sort((left, right) => Compare(metadata(left), metadata(right), field, descending));
        return result;
    }

    private static int Compare(MediaFile left, MediaFile right, MediaSortField field, bool descending)
    {
        bool folder = left.Kind == MediaKind.Folder;
        if (folder != (right.Kind == MediaKind.Folder)) return folder ? -1 : 1;
        int order;
        if (field == MediaSortField.Name || (field == MediaSortField.Size && folder))
            order = FolderScanner.CompareNames(left.Name, right.Name);
        else
        {
            long? a = field == MediaSortField.Size ? left.Version?.Length : left.ModifiedTicks;
            long? b = field == MediaSortField.Size ? right.Version?.Length : right.ModifiedTicks;
            // 缺少元数据的文件始终放在这一组的末尾，不猜测大小，也不递归计算目录大小。
            if (a.HasValue != b.HasValue) return a.HasValue ? -1 : 1;
            order = Nullable.Compare(a, b);
        }
        if (order != 0)
            return descending && !(field == MediaSortField.Size && folder) ? -Math.Sign(order) : Math.Sign(order);
        order = FolderScanner.CompareNames(left.Name, right.Name);
        return order != 0 ? order : StringComparer.Ordinal.Compare(left.Path, right.Path);
    }
}
