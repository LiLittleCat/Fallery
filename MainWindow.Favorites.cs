using Fallery.Services;
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
    private readonly TreeViewNode _favoritesRoot = new() { Content = new FolderEntry("", AppText.Get("Favorites")), IsExpanded = true };

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
        string label = saved ? AppText.Get("RemoveCurrentFolderFromFavorites") : AppText.Get("AddCurrentFolderToFavorites");
        LiveText.SetOnce(FavoriteButton, "ToolTipService.ToolTip", localized => ToolTipService.SetToolTip(localized, label));
        LiveText.SetOnce(FavoriteButton, "AutomationProperties.Name", localized => AutomationProperties.SetName(localized, label));
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
        var open = LiveText.Set(new MenuFlyoutItem { Text = AppText.Get("OpenInNewWindow"), Icon = FluentIcons.Create("IconNewWindow"), Tag = "new-window" }, "Text", localized => localized.Text = AppText.Get("OpenInNewWindow"));
        open.Click += (_, _) => RequestNewWindow(path);
        menu.Items.Add(open);
        var exclude = LiveText.Set(new MenuFlyoutItem { Text = AppText.Get("ExcludeThisFolder"), Icon = FluentIcons.Create("IconFilter"), Tag = "exclude" }, "Text", localized => localized.Text = AppText.Get("ExcludeThisFolder"));
        exclude.Click += (_, _) => ExcludeTreeFolder(path);
        var remove = LiveText.Set(new MenuFlyoutItem { Text = AppText.Get("RemoveFromFavorites"), Icon = FluentIcons.Create("IconDelete"), Tag = "remove-favorite", KeyboardAcceleratorTextOverride = "Delete" }, "Text", localized => localized.Text = AppText.Get("RemoveFromFavorites"));
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
