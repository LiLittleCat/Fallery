using Fallery.Controls;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery;

public sealed partial class MainWindow
{
    private (StackPanel Content, Func<Task> Apply) BuildFolderExclusionsSection()
    {
        var originalNames = FolderExclusionRules.Names;
        var originalPaths = _treeExcludedFolders.ToArray();
        var paths = originalPaths.ToList();
        var availableNames = FolderExclusionRules.Defaults.Concat(originalNames)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var panel = new StackPanel { Spacing = 12 };
        TextBlock Text(string key) => LiveText.Set(new TextBlock { TextWrapping = TextWrapping.Wrap },
            "Text", control => control.Text = AppText.Get(key));
        panel.Children.Add(Text("ExclusionSaveHint"));
        panel.Children.Add(Text("ExcludedDirectoryNames"));
        panel.Children.Add(Text("ExcludedDirectoryNamesHint"));
        var names = new ListView
        {
            Name = "SettingsExcludedNames", ItemsSource = availableNames.ToArray(),
            SelectionMode = ListViewSelectionMode.Multiple, Height = 180,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        foreach (string name in availableNames.Where(name => originalNames.Contains(name, StringComparer.OrdinalIgnoreCase)))
            names.SelectedItems.Add(name);
        panel.Children.Add(names);
        var addRow = new Grid { ColumnSpacing = 8 };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var input = LiveText.Set(new TextBox { Name = "SettingsExcludedNameInput", MaxLength = 255 },
            "PlaceholderText", control => control.PlaceholderText = AppText.Get("ExcludedNamePlaceholder"));
        var add = LiveText.Set(new Button { Name = "SettingsAddExcludedName" }, "Content", control => control.Content = AppText.Get("AddExclusionName"));
        Grid.SetColumn(add, 1);
        addRow.Children.Add(input);
        addRow.Children.Add(add);
        panel.Children.Add(addRow);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        panel.Children.Add(error);
        add.Click += (_, _) =>
        {
            string name = input.Text.Trim();
            if (!FolderExclusionRules.IsValidName(name))
            {
                error.Visibility = Visibility.Visible;
                LiveText.Set(error, "Text", control => control.Text = AppText.Get("InvalidExcludedName"));
                return;
            }
            var selected = names.SelectedItems.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            selected.Add(name);
            if (!availableNames.Contains(name, StringComparer.OrdinalIgnoreCase)) availableNames.Add(name);
            availableNames.Sort(StringComparer.OrdinalIgnoreCase);
            names.ItemsSource = availableNames.ToArray();
            foreach (string item in availableNames.Where(selected.Contains)) names.SelectedItems.Add(item);
            input.Text = "";
            error.Visibility = Visibility.Collapsed;
            LiveText.SetOnce(error, "Text", control => control.Text = "");
        };
        var defaults = LiveText.Set(new Button { Name = "SettingsDefaultExclusions" }, "Content",
            control => control.Content = AppText.Get("RestoreDefaultExclusions"));
        defaults.Click += (_, _) =>
        {
            names.SelectedItems.Clear();
            foreach (string name in FolderExclusionRules.Defaults) names.SelectedItems.Add(name);
        };
        panel.Children.Add(defaults);
        panel.Children.Add(Text("TreeExcludedPaths"));
        panel.Children.Add(Text("TreeExcludedPathsHint"));
        var pathList = new ListView { Name = "SettingsExcludedPaths", MaxHeight = 140, SelectionMode = ListViewSelectionMode.Single };
        var selectedPath = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        var empty = Text("NoExcludedFolders");
        var restore = LiveText.Set(new Button { Name = "SettingsRestoreExcludedPath", IsEnabled = false },
            "Content", control => control.Content = AppText.Get("RestoreExcludedFolder"));
        void RefreshPaths()
        {
            pathList.ItemsSource = paths.ToArray();
            empty.Visibility = paths.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (paths.Count > 0) pathList.SelectedIndex = 0;
        }
        pathList.SelectionChanged += (_, _) =>
        {
            restore.IsEnabled = pathList.SelectedItem is string;
            selectedPath.Text = pathList.SelectedItem as string ?? "";
        };
        restore.Click += (_, _) =>
        {
            if (pathList.SelectedItem is string path) paths.Remove(path);
            RefreshPaths();
        };
        RefreshPaths();
        panel.Children.Add(empty);
        panel.Children.Add(pathList);
        panel.Children.Add(selectedPath);
        panel.Children.Add(restore);
        return (panel, async () =>
        {
            string[] selected = FolderExclusionRules.Normalize(names.SelectedItems.Cast<string>());
            bool namesEdited = !originalNames.SequenceEqual(selected, StringComparer.OrdinalIgnoreCase);
            bool pathsEdited = !originalPaths.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase);
            if (namesEdited || pathsEdited)
                await ApplyFolderExclusionsAsync(namesEdited ? selected : null, pathsEdited ? paths.ToArray() : null);
        });
    }

    private async Task ApplyFolderExclusionsAsync(string[]? names, string[]? paths)
    {
        bool namesChanged = names is not null && FolderExclusionRules.SetNames(names);
        if (paths is not null)
        {
            _treeExcludedFolders.Clear();
            _treeExcludedFolders.AddRange(paths);
        }
        QueueSettingsSave();
        if (namesChanged && App.Current is { } app) await app.RefreshFolderExclusionsAsync();
        else await RefreshFolderExclusionsAsync();
    }

    internal async Task RefreshFolderExclusionsAsync()
    {
        if (_closed || _closing) return;
        UpdateTreeToolbar();
        QueueSettingsSave();
        GalleryState.ClearCachedDirectories();
        ClearExcludedTreeSelection();
        RebuildFavorites();
        await ResetFolderTreeAsync();
        await (_rootsTask ??= LoadRootsAsync());
        await LoadRootsAsync();
        await ReloadBrowseOptionsAsync();
    }
}
