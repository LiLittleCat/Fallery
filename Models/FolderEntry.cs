namespace Fallery.Models;

public enum FolderTimeField { Modified, Created }

/// <summary>文件树节点和面包屑共用。TreeView / BreadcrumbBar 默认用 ToString() 显示。</summary>
public sealed record FolderEntry(string Path, string Name)
{
    public long? ModifiedTicks { get; init; }
    public long? CreatedTicks { get; init; }
    public bool ShowModifiedTime { get; init; }
    public FolderTimeField TimeField { get; init; }
    public string TimeLabel => TimeField == FolderTimeField.Created ? "创建时间" : "修改时间";
    public string ModifiedText => !ShowModifiedTime || string.IsNullOrEmpty(Path) ? ""
        : (TimeField == FolderTimeField.Created ? CreatedTicks : ModifiedTicks) is { } ticks
            ? new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm") : TimeLabel + "未知";
    public override string ToString() => Name;
}

public sealed record FolderChild(string Path, bool HasChildren)
{
    public long? ModifiedTicks { get; init; }
    public long? CreatedTicks { get; init; }
}
