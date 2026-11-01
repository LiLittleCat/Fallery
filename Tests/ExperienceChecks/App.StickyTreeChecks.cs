using System.Collections;
using System.Reflection;
using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static async Task<int> RunStickyTreeChecksAsync(MainWindow window)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var root = (FrameworkElement)window.Content;
        var tree = (TreeView)root.FindName("FolderTree");
        var sticky = (ItemsControl)root.FindName("FolderTreeStickyItems");
        TreeViewNode[] Pinned() => (sticky.ItemsSource as IEnumerable)?.Cast<object>()
            .Select(row => (TreeViewNode)row.GetType().GetProperty("Node")!.GetValue(row)!).ToArray() ?? [];
        async Task Settle()
        {
            root.UpdateLayout(); await Task.Delay(150); root.UpdateLayout(); Call("UpdateStickyTree");
        }
        TreeViewNode Node(string name) => new() { Content = new FolderEntry(@"C:\sticky-test\" + name, name) };
        var branch = Node("吸顶父目录");
        var nested = Node("嵌套目录");
        branch.Children.Add(nested);
        for (int i = 0; i < 300; i++) nested.Children.Add(Node($"子目录{i:000}"));
        var other = Node("另一分支");
        for (int i = 0; i < 80; i++) other.Children.Add(Node($"另一子目录{i:000}"));
        var previous = tree.SelectedNode;
        string? browseFolder = window.GalleryState.CurrentFolder;
        tree.RootNodes.Add(branch); tree.RootNodes.Add(other);
        branch.IsExpanded = nested.IsExpanded = other.IsExpanded = true;
        try
        {
            await Settle();
            var list = (ListView)typeof(MainWindow).GetField("_stickyTreeList", flags)!.GetValue(window)!;
            list.ScrollIntoView(nested.Children[140], ScrollIntoViewAlignment.Leading);
            await Settle();
            Check(Pinned().SequenceEqual(new[] { branch, nested }), "滚动到深层内容时应固定真实父级路径");
            Check(((Border)root.FindName("FolderTreeStickyHeader")).Visibility == Visibility.Visible
                && Descendants(sticky).OfType<TextBlock>().Any(text => text.Text == "嵌套目录"), "吸顶栏必须显示父目录名称");
            int containers = ((Panel)list.ItemsPanelRoot).Children.Count;
            for (int i = 0; i < 10; i++) Call("UpdateStickyTree");
            Check(((Panel)list.ItemsPanelRoot).Children.Count == containers && nested.Children.Count == 300,
                "吸顶更新不能实现视口外节点或展开额外目录");
            nested.Content = ((FolderEntry)nested.Content) with { Name = "更新后的父目录" };
            await Settle();
            Check(Descendants(sticky).OfType<TextBlock>().Any(text => text.Text == "更新后的父目录"), "父节点名称更新应同步到吸顶栏");
            list.ScrollIntoView(other.Children[40], ScrollIntoViewAlignment.Leading);
            await Settle();
            Check(Pinned().SequenceEqual(new[] { other }), "滚到另一分支后不能残留上一分支吸顶项");
            list.ScrollIntoView(nested.Children[100], ScrollIntoViewAlignment.Leading);
            await Settle();
            Call("CollapseStickyTreeNode", nested);
            await Settle();
            Check(!nested.IsExpanded && branch.IsExpanded && tree.SelectedNode == nested, "点击吸顶收起应只折叠对应分支并聚焦父行");
            Check(window.GalleryState.CurrentFolder == browseFolder, "收起吸顶目录不能导航右侧浏览区");
            Check(!Pinned().Contains(nested) && tree.ContainerFromNode(nested) is not null, "折叠后应移除旧吸顶项并显示真实父行");
            list.ScrollIntoView(branch, ScrollIntoViewAlignment.Leading);
            await Settle();
            Check(!Pinned().Contains(branch), "父目录本身已回到顶部时不能重复显示吸顶副本");
            tree.RootNodes.Remove(branch); tree.RootNodes.Remove(other);
            await Settle();
            Check(!Pinned().Contains(branch) && !Pinned().Contains(nested) && !Pinned().Contains(other), "节点移除后吸顶不能保留失效分支");
        }
        finally
        {
            tree.RootNodes.Remove(branch); tree.RootNodes.Remove(other);
            tree.SelectedNode = previous;
            if (previous is not null) Call("UpdateStickyTree");
        }
        return checks;
    }
}
