using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Xaml.Controls;

namespace Fallery;

public sealed partial class MainWindow
{
    private async void OnLanguageChanged(string previousLanguage)
    {
        if (_closed || _closing) return;
        Root.Language = AppText.Language;
        if (_settingsDialog is not null) _settingsDialog.Language = AppText.Language;
        GalleryState.RefreshLanguage(previousLanguage);
        ViewerState.Message = AppText.TranslateStatus(ViewerState.Message, previousLanguage);
        UpdateBrowseMenus();
        UpdateTreeToolbar();
        UpdateFavoriteState();
        UpdateGalleryVisibility();
        UpdateZoomIndicators();
        UpdateMuteIcon();
        UpdateRateLabel();
        RefreshAddressBar();
        // 只替换已加载节点的展示数据，不重新扫描、不改变展开和选择状态。
        _favoritesRoot.Content = new FolderEntry("", AppText.Get("Favorites"));
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        foreach (TreeViewNode node in LoadedFolderNodes())
        {
            if (node.Content is not FolderEntry entry) continue;
            string name = entry.Name;
            if (FolderTree.RootNodes.Contains(node))
            {
                if (string.Equals(entry.Path, desktop, StringComparison.OrdinalIgnoreCase)) name = AppText.Get("Desktop");
                else if (string.Equals(entry.Path, pictures, StringComparison.OrdinalIgnoreCase)) name = AppText.Get("Photos");
            }
            node.Content = entry with { Name = name };
        }
        SetStickyTreeRows([]);
        UpdateStickyTree();
        QueueSettingsSave();
        if (ViewerState.CurrentItem is { } item) await LoadViewerDetailsAsync(item);
    }
}
