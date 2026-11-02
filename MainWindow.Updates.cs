using Fallery.Controls;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery;

public sealed partial class MainWindow
{
    private StackPanel BuildUpdateSection()
    {
        var service = VelopackUpdates.Shared;
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(LiveText.Set(new TextBlock { Text = AppText.Get("VersionAndUpdates"), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] }, "Text", localized => localized.Text = AppText.Get("VersionAndUpdates")));
        panel.Children.Add(new TextBlock { Text = $"{AppIdentity.DisplayName} {AppIdentity.Version}", IsTextSelectionEnabled = true });
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 400, FontSize = 12 };
        var notes = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 400, MaxLines = 8, IsTextSelectionEnabled = true };
        var progress = new ProgressBar { Minimum = 0, Maximum = 100 };
        var action = new Button();
        var cancel = LiveText.Set(new Button { Content = AppText.Get("CancelDownload") }, "Content", localized => localized.Content = AppText.Get("CancelDownload"));
        var confirmation = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        confirmation.Children.Add(LiveText.Set(new TextBlock { Text = AppText.Get("SettingsAndPlaybackPositionsWillBeSavedAllFallery"), TextWrapping = TextWrapping.Wrap, MaxWidth = 400 }, "Text", localized => localized.Text = AppText.Get("SettingsAndPlaybackPositionsWillBeSavedAllFallery")));
        var restart = LiveText.Set(new Button { Content = AppText.Get("ConfirmRestartAndUpdate") }, "Content", localized => localized.Content = AppText.Get("ConfirmRestartAndUpdate"));
        confirmation.Children.Add(restart);
        void Refresh()
        {
            if (_closed) return;
            var value = service.Status;
            LiveText.Set(status, "Text", localized => localized.Text = service.Status.DisplayMessage);
            LiveText.SetOnce(notes, "Text", localized => localized.Text = value.Release?.Notes ?? "");
            notes.Visibility = string.IsNullOrWhiteSpace(notes.Text) ? Visibility.Collapsed : Visibility.Visible;
            progress.Visibility = value.State is AppUpdateState.Checking or AppUpdateState.Downloading ? Visibility.Visible : Visibility.Collapsed;
            progress.IsIndeterminate = value.State == AppUpdateState.Checking;
            progress.Value = value.Progress;
            LiveText.Set(action, "Content", localized => localized.Content = value.State switch { AppUpdateState.Available => AppText.Get("DownloadUpdate"), AppUpdateState.Ready => AppText.Get("RestartAndUpdate"), _ => AppText.Get("CheckForUpdates") });
            action.IsEnabled = value.State is AppUpdateState.Idle or AppUpdateState.Available or AppUpdateState.Ready or AppUpdateState.Latest or AppUpdateState.Failed;
            cancel.Visibility = value.State is AppUpdateState.Checking or AppUpdateState.Downloading ? Visibility.Visible : Visibility.Collapsed;
            LiveText.Set(cancel, "Content", localized => localized.Content = value.State == AppUpdateState.Checking ? AppText.Get("CancelCheck") : AppText.Get("CancelDownload"));
            restart.IsEnabled = value.State == AppUpdateState.Ready;
            if (value.State != AppUpdateState.Ready) confirmation.Visibility = Visibility.Collapsed;
        }
        void Changed() => DispatcherQueue.TryEnqueue(Refresh);
        panel.Loaded += (_, _) => { service.Changed += Changed; Refresh(); };
        panel.Unloaded += (_, _) => service.Changed -= Changed;
        action.Click += async (_, _) =>
        {
            if (service.Status.State == AppUpdateState.Ready) confirmation.Visibility = Visibility.Visible;
            else if (service.Status.State == AppUpdateState.Available) await service.DownloadAsync();
            else await service.CheckAsync();
        };
        cancel.Click += (_, _) => service.Cancel();
        restart.Click += async (_, _) =>
        {
            confirmation.Visibility = Visibility.Collapsed;
            if (App.Current is { } app) await app.ApplyUpdateAsync(this);
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(action); buttons.Children.Add(cancel);
        panel.Children.Add(status); panel.Children.Add(notes); panel.Children.Add(progress); panel.Children.Add(buttons); panel.Children.Add(confirmation);
        panel.Children.Add(LiveText.Set(new HyperlinkButton { Content = AppText.Get("ReleasePage"), NavigateUri = new Uri("https://github.com/LiLittleCat/Fallery/releases") }, "Content", localized => localized.Content = AppText.Get("ReleasePage")));
        panel.Children.Add(LiveText.Set(new TextBlock { Text = AppText.Format("DataFolder", AppIdentity.DataDirectory), TextWrapping = TextWrapping.Wrap, MaxWidth = 400, FontSize = 12, IsTextSelectionEnabled = true }, "Text", localized => localized.Text = AppText.Format("DataFolder", AppIdentity.DataDirectory)));
        Refresh();
        return panel;
    }

    internal bool CanPrepareUpdate(bool requestingWindow) => !_closed && !_closing && !_pasting && !_deleting && !_pickingFolder
        && _searchDialog is null && (requestingWindow || _settingsDialog is null);

    internal void BeginUpdateShutdown()
    {
        _closing = true;
        Root.IsHitTestVisible = false;
        StopHoverPreview();
        CloseItemMenu();
        CloseAddressMenu();
        _settingsTimer.Stop();
        GalleryState.Cancel();
        CloseViewer();
    }

    internal async Task SaveBeforeUpdateAsync()
    {
        if (!await _resumeSaveTask) throw new IOException(AppText.Get("CouldNotSavePlaybackPositionsCheckDataFolderPermissions"));
        if (_settingsReady && !await _settingsStore.SaveAsync(CaptureSettings()))
            throw new IOException(AppText.Get("CouldNotSaveSettingsCheckDataFolderPermissionsAnd"));
    }

    internal void CancelUpdateShutdown()
    {
        if (_closed) return;
        _closing = false;
        Root.IsHitTestVisible = true;
    }

    internal void CloseForUpdate()
    {
        _allowClose = true;
        Close();
    }
}
