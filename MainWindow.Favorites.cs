using Fallery.Controls;
using Fallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Fallery;

public sealed partial class MainWindow
{
    private readonly List<string> _favoriteFolders = [];
    private readonly TreeViewNode _favoritesRoot = new() { Content = new FolderEntry("", "收藏"), IsExpanded = true };

    private void InitializeFavorites() => FavoritesTree.RootNodes.Add(_favoritesRoot);

    private void RestoreFavoriteFolders(IEnumerable<string> folders)
    {
        _favoriteFolders.Clear();
        _favoriteFolders.AddRange(folders);
        RebuildFavorites();
        _favoritesRoot.IsExpanded = true;
    }

    private void RebuildFavorites()
    {
        _favoritesRoot.Children.Clear();
        foreach (string path in _favoriteFolders)
        {
            if (IsTreeFolderExcluded(path)) continue;
            string name = System.IO.Path.GetFileName(path);
            _favoritesRoot.Children.Add(new TreeViewNode { Content = new FolderEntry(path, string.IsNullOrEmpty(name) ? path : name) });
        }
        UpdateFavoriteState();
        _ = RefreshTreePresentationAsync();
    }

    private void UpdateFavoriteState()
    {
        string? path = GalleryState.CurrentFolder;
        bool saved = path is not null && _favoriteFolders.Contains(path, StringComparer.OrdinalIgnoreCase);
        FavoriteButton.IsEnabled = path is not null;
        FavoriteIcon.Glyph = FluentIcons.Glyph(saved ? "IconBookmarkFilled" : "IconBookmark");
        FavoriteIcon.FontFamily = (FontFamily)Application.Current.Resources[saved ? "FluentIconFilledFont" : "FluentIconFont"];
        string label = saved ? "取消收藏当前文件夹" : "收藏当前文件夹";
        ToolTipService.SetToolTip(FavoriteButton, label);
        AutomationProperties.SetName(FavoriteButton, label);
        FavoritesTree.SelectedNode = _favoritesRoot.Children.FirstOrDefault(node =>
            node.Content is FolderEntry entry && string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _closing || GalleryState.CurrentFolder is not { } path) return;
        if (_favoriteFolders.Contains(path, StringComparer.OrdinalIgnoreCase)) RemoveFavorite(path);
        else
        {
            _favoriteFolders.Add(path);
            RebuildFavorites();
            _favoritesRoot.IsExpanded = true;
            QueueSettingsSave();
        }
    }

    private void RemoveFavorite(string path)
    {
        if (_favoriteFolders.RemoveAll(folder => string.Equals(folder, path, StringComparison.OrdinalIgnoreCase)) == 0) return;
        RebuildFavorites();
        QueueSettingsSave();
    }

    private async void FavoritesTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is not TreeViewNode { Content: FolderEntry entry } node) return;
        if (node == _favoritesRoot) { node.IsExpanded = !node.IsExpanded; return; }
        await OpenFavoriteAsync(entry.Path);
    }

    private async Task OpenFavoriteAsync(string path)
    {
        await NavigateAsync(path);
        // 失效的路径仍保留收藏，用户可以重连磁盘或手动移除；导航失败时恢复实际目录的选中态。
        if (!_closed) UpdateFavoriteState();
    }

    private MenuFlyout BuildFavoriteMenu(string path)
    {
        var menu = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = "在新窗口中打开", Icon = FluentIcons.Create("IconNewWindow"), Tag = "new-window" };
        open.Click += (_, _) => RequestNewWindow(path);
        menu.Items.Add(open);
        var exclude = new MenuFlyoutItem { Text = "排除此文件夹", Icon = FluentIcons.Create("IconFilter"), Tag = "exclude" };
        exclude.Click += (_, _) => ExcludeTreeFolder(path);
        var remove = new MenuFlyoutItem { Text = "移除收藏", Icon = FluentIcons.Create("IconDelete"), Tag = "remove-favorite", KeyboardAcceleratorTextOverride = "Delete" };
        remove.Click += (_, _) => RemoveFavorite(path);
        menu.Items.Add(exclude);
        menu.Items.Add(remove);
        return menu;
    }

    private void FavoritesTree_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        TreeViewNode? node = null;
        FrameworkElement target = FavoritesTree;
        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is not TreeViewItem container) continue;
            node = FavoritesTree.NodeFromContainer(container);
            target = container;
            break;
        }
        if (node is null && !e.TryGetPosition(FavoritesTree, out _)) node = FavoritesTree.SelectedNode;
        if (node?.Content is not FolderEntry entry || node == _favoritesRoot) return;
        CloseItemMenu();
        var menu = BuildFavoriteMenu(entry.Path);
        _itemMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_itemMenu, menu)) _itemMenu = null; };
        menu.ShowAt(target, new FlyoutShowOptions { Position = e.TryGetPosition(target, out var point) ? point : null });
    }
}
