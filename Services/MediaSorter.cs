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
        bool folderByName = folder && field is MediaSortField.Size or MediaSortField.Taken;
        if (field == MediaSortField.Name || folderByName)
            order = FolderScanner.CompareNames(left.Name, right.Name);
        else
        {
            long? a = Value(left, field);
            long? b = Value(right, field);
            // 缺少元数据的文件始终放在这一组的末尾，不猜测大小，也不递归计算目录大小。
            if (a.HasValue != b.HasValue) return a.HasValue ? -1 : 1;
            order = Nullable.Compare(a, b);
        }
        if (order != 0)
            return descending && !folderByName ? -Math.Sign(order) : Math.Sign(order);
        order = FolderScanner.CompareNames(left.Name, right.Name);
        return order != 0 ? order : StringComparer.Ordinal.Compare(left.Path, right.Path);
    }

    private static long? Value(MediaFile file, MediaSortField field) => field switch
    {
        MediaSortField.Size => file.Version?.Length,
        MediaSortField.Created => file.CreatedTicks,
        MediaSortField.Taken => file.TakenTicks,
        _ => file.ModifiedTicks,
    };
}
