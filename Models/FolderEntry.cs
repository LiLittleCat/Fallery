namespace Fallery.Models;

/// <summary>文件树节点和面包屑共用。TreeView / BreadcrumbBar 默认用 ToString() 显示。</summary>
public sealed record FolderEntry(string Path, string Name)
{
    public override string ToString() => Name;
}
