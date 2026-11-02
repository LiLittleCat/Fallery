using Fallery.Controls;
using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Fallery;

public sealed partial class MainWindow
{
    private FlyoutBase? _addressFlyout;
    private CancellationTokenSource? _addressMenuCts;
    private int? _addressRequest;

    private void RefreshAddressBar()
    {
        NewFolderButton.IsEnabled = GalleryState.CurrentFolder is not null;
        UpdateFavoriteState();
        CloseAddressMenu();
        EndAddressEdit(cancelRequest: false);
        AddressSegments.Children.Clear();
        var style = (Style)Root.Resources["IconButtonStyle"];
        foreach (var entry in GalleryState.Breadcrumbs)
        {
            var name = new Button
            {
                Content = new TextBlock { Text = entry.Name, FontSize = 13 },
                Tag = entry, Style = style, Padding = new Thickness(5, 2, 5, 2), MinWidth = 0, MinHeight = 28,
            };
            LiveText.SetOnce(name, "ToolTipService.ToolTip", localized => ToolTipService.SetToolTip(localized, entry.Path));
            LiveText.Set(name, "AutomationProperties.Name", localized => AutomationProperties.SetName(localized, AppText.Format("GoTo", entry.Path)));
            name.Click += AddressSegment_Click;
            AddressSegments.Children.Add(name);
            var children = new Button
            {
                Content = Controls.FluentIcons.Create("IconChevronRight", 10), Tag = entry,
                Style = style, Padding = new Thickness(3, 2, 3, 2), MinWidth = 18, MinHeight = 28,
            };
            LiveText.Set(children, "ToolTipService.ToolTip", localized => ToolTipService.SetToolTip(localized, AppText.Format("SubfoldersOf", entry.Name)));
            LiveText.Set(children, "AutomationProperties.Name", localized => AutomationProperties.SetName(localized, AppText.Format("ExpandSubfoldersOf", entry.Name)));
            children.Click += AddressChildren_Click;
            AddressSegments.Children.Add(children);
        }
        if (GalleryState.CurrentFolder is null)
            AddressSegments.Children.Add(LiveText.Set(new TextBlock { Text = AppText.Get("EnterAPathOrOpenAFolder"), FontSize = 13, Margin = new Thickness(4, 5, 4, 0) }, "Text", localized => localized.Text = AppText.Get("EnterAPathOrOpenAFolder")));
        LiveText.SetOnce(AddressFrame, "ToolTipService.ToolTip", localized => ToolTipService.SetToolTip(localized, GalleryState.CurrentFolder));
        DispatcherQueue.TryEnqueue(ScrollAddressToEnd);
    }

    private void ScrollAddressToEnd()
    {
        if (!_closed && AddressScroller.IsLoaded && double.IsFinite(AddressScroller.ScrollableWidth) && AddressScroller.ScrollableWidth > 0)
            AddressScroller.ChangeView(AddressScroller.ScrollableWidth, null, null, true);
    }

    private void AddressScroller_SizeChanged(object sender, SizeChangedEventArgs e) => DispatcherQueue.TryEnqueue(ScrollAddressToEnd);

    private async void AddressSegment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FolderEntry entry }) await NavigateAsync(entry.Path);
    }

    private void AddressFrame_Tapped(object sender, TappedRoutedEventArgs e)
    {
        for (var element = e.OriginalSource as DependencyObject; element is not null && element != AddressFrame;
             element = VisualTreeHelper.GetParent(element))
            if (element is ButtonBase or TextBox) return;
        BeginAddressEdit();
        e.Handled = true;
    }

    private void BeginAddressEdit()
    {
        if (_closing || _closed || (ViewerState.IsOpen && !IsFilmstrip)) return;
        CloseAddressMenu();
        if (AddressEditor.Visibility != Visibility.Visible) LiveText.SetOnce(AddressEditor, "Text", localized => localized.Text = GalleryState.CurrentFolder ?? "");
        LiveText.Set(AddressEditor, "ToolTipService.ToolTip", localized => ToolTipService.SetToolTip(localized, AppText.Get("EnterAFolderPathEnterToGoEscTo")));
        AddressScroller.Visibility = AddressRefreshButton.Visibility = Visibility.Collapsed;
        AddressEditor.Visibility = AddressGoButton.Visibility = Visibility.Visible;
        AddressEditor.Focus(FocusState.Programmatic);
        AddressEditor.SelectAll();
    }

    private void EndAddressEdit(bool cancelRequest)
    {
        if (cancelRequest && _addressRequest == _navigationVersion)
        {
            GalleryState.Cancel();
            GalleryState.Status = AppText.Get("NavigationCanceled");
        }
        _addressRequest = null;
        LiveText.SetOnce(AddressEditor, "Text", localized => localized.Text = GalleryState.CurrentFolder ?? "");
        AddressEditor.Visibility = AddressGoButton.Visibility = Visibility.Collapsed;
        AddressScroller.Visibility = AddressRefreshButton.Visibility = Visibility.Visible;
    }

    private async void AddressEditor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await SubmitAddressAsync();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            EndAddressEdit(cancelRequest: true);
            GalleryScroller.Focus(FocusState.Programmatic);
        }
    }

    private void AddressEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        // 等焦点完成转移，点击“转到”时不能先清掉尚未提交的输入。
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || _closing || _addressRequest is not null || AddressEditor.Visibility != Visibility.Visible) return;
            var focused = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
            for (var element = focused; element is not null; element = VisualTreeHelper.GetParent(element))
                if (element == AddressEditor || element == AddressGoButton) return;
            EndAddressEdit(cancelRequest: false);
        });
    }

    private async void AddressGo_Click(object sender, RoutedEventArgs e) => await SubmitAddressAsync();

    private async Task SubmitAddressAsync()
    {
        if (_closing || _closed) return;
        string target;
        try { target = FolderAddress.Resolve(AddressEditor.Text, GalleryState.CurrentFolder); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            GalleryState.Status = AppText.Format("CouldNotOpenAddress", ex.Message);
            LiveText.SetOnce(AddressEditor, "ToolTipService.ToolTip", localized => ToolTipService.SetToolTip(localized, GalleryState.Status));
            return;
        }
        if (!GalleryState.IsScanning && string.Equals(target, GalleryState.CurrentFolder, StringComparison.OrdinalIgnoreCase))
        {
            EndAddressEdit(cancelRequest: false);
            return;
        }
        int request = _navigationVersion + 1;
        _addressRequest = request;
        await NavigateAsync(target);
        if (_closed || _closing || _addressRequest != request || _navigationVersion != request) return;
        // 导航成功会在提交目录时退出编辑；失败时保留输入，便于直接修正。
        _addressRequest = null;
        LiveText.SetOnce(AddressEditor, "ToolTipService.ToolTip", localized => ToolTipService.SetToolTip(localized, GalleryState.Status));
        AddressEditor.Focus(FocusState.Programmatic);
    }

    private async void AddressChildren_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) await ShowAddressChildrenAsync(button);
    }

    private async Task ShowAddressChildrenAsync(Button button)
    {
        if (_closed || _closing) return;
        CloseAddressMenu();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _addressMenuCts = cts;
        var status = LiveText.Set(new TextBlock { Text = AppText.Get("ReadingSubfolders"), Margin = new Thickness(8) }, "Text", localized => localized.Text = AppText.Get("ReadingSubfolders"));
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true, MaxHeight = 420, Visibility = Visibility.Collapsed,
            ItemTemplate = (DataTemplate)Root.Resources["AddressMenuItemTemplate"],
            ItemContainerStyle = (Style)Root.Resources["AddressMenuItemStyle"],
        };
        LiveText.Set(list, "AutomationProperties.Name", localized => AutomationProperties.SetName(localized, AppText.Get("Subfolders")));
        var content = new Grid { Width = 260 };
        content.Children.Add(status);
        content.Children.Add(list);
        var flyout = new Flyout
        {
            Content = content,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            ShouldConstrainToRootBounds = true,
            FlyoutPresenterStyle = (Style)Root.Resources["AddressMenuPresenterStyle"]
        };
        _addressFlyout = flyout;
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(_addressMenuCts, cts)) cts.Cancel();
            if (ReferenceEquals(_addressFlyout, flyout)) _addressFlyout = null;
        };
        list.ItemClick += async (_, args) =>
        {
            if (args.ClickedItem is not FolderEntry entry) return;
            flyout.Hide();
            await NavigateAsync(entry.Path);
        };
        flyout.ShowAt(button);
        try
        {
            string? parent = (button.Tag as FolderEntry)?.Path;
            bool showHidden = GalleryState.ShowHidden;
            var children = await Task.Run(() => FolderAddress.GetChildren(parent, cts.Token, showHidden), cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            LiveText.SetOnce(list, "ItemsSource", localized => localized.ItemsSource = children);
            list.Visibility = children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            status.Visibility = children.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            LiveText.Set(status, "Text", localized => localized.Text = AppText.Get("NoAccessibleSubfolders"));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!cts.IsCancellationRequested) LiveText.Set(status, "Text", localized => localized.Text = AppText.Get("CouldNotReadThisFolderCheckPermissionsOrThe"));
        }
        finally { if (ReferenceEquals(_addressMenuCts, cts)) _addressMenuCts = null; }
    }

    private void AddressHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _closing) return;
        CloseAddressMenu();
        EndAddressEdit(cancelRequest: false);
        var menu = new MenuFlyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            ShouldConstrainToRootBounds = true,
            MenuFlyoutPresenterStyle = (Style)Root.Resources["NavigationMenuPresenterStyle"]
        };
        foreach (var folder in GalleryState.RecentFolders)
        {
            string name = System.IO.Path.GetFileName(folder) is { Length: > 0 } leaf ? leaf : folder;
            var item = new MenuFlyoutItem
            {
                Text = string.Equals(name, folder, StringComparison.Ordinal) ? name : $"{name}  —  {folder}",
                Icon = Controls.FluentIcons.Create("IconFolderFilled", filled: true), Tag = folder,
            };
            if (string.Equals(folder, GalleryState.CurrentFolder, StringComparison.OrdinalIgnoreCase)) item.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            LiveText.SetOnce(item, "ToolTipService.ToolTip", localized => ToolTipService.SetToolTip(localized, folder));
            item.Click += async (_, _) => await NavigateAsync(folder);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(LiveText.Set(new MenuFlyoutItem { Text = AppText.Get("NoRecentLocations"), IsEnabled = false }, "Text", localized => localized.Text = AppText.Get("NoRecentLocations")));
        else
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            var clear = LiveText.Set(new MenuFlyoutItem { Text = AppText.Get("ClearHistory"), Icon = Controls.FluentIcons.Create("IconDelete") }, "Text", localized => localized.Text = AppText.Get("ClearHistory"));
            clear.Click += (_, _) =>
            {
                GalleryState.ClearRecentFolders();
                QueueSettingsSave();
            };
            menu.Items.Add(clear);
        }
        _addressFlyout = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_addressFlyout, menu)) _addressFlyout = null; };
        menu.ShowAt(AddressFrame);
    }

    private void CloseAddressMenu()
    {
        _addressMenuCts?.Cancel();
        _addressFlyout?.Hide();
        _addressFlyout = null;
    }
}
