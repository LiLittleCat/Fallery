namespace Fallery.Models;

/// <summary>文件树节点和面包屑共用。TreeView / BreadcrumbBar 默认用 ToString() 显示。</summary>
public sealed record FolderEntry(string Path, string Name)
{
    public long? ModifiedTicks { get; init; }
    public bool ShowModifiedTime { get; init; }
    public string ModifiedText => !ShowModifiedTime || string.IsNullOrEmpty(Path) ? ""
        : ModifiedTicks is { } ticks ? new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "修改时间未知";
    public override string ToString() => Name;
}

public sealed record FolderChild(string Path, bool HasChildren)
{
    public long? ModifiedTicks { get; init; }
}
