using System.Text.Json;
using Fallery.Models;

namespace Fallery.Services;

public sealed record AppSettings
{
    public int Width { get; init; } = 1400;
    public int Height { get; init; } = 900;
    public bool Maximized { get; init; }
    public bool VideoWindowDefaultsApplied { get; init; }
    public string? LastFolder { get; init; }
    public int Theme { get; init; }
    public double BackgroundOpacity { get; init; } = 70;
    public double ThumbnailSize { get; init; } = 240;
    public double SidebarWidth { get; init; } = 260;
    public MediaSortField TreeSortField { get; init; } = MediaSortField.Name;
    public bool TreeDescending { get; init; }
    public bool TreeShowModifiedTime { get; init; }
    public FolderTimeField TreeTimeField { get; init; }
    public string[] TreeExcludedFolders { get; init; } = [];
    public MediaSortField SortField { get; init; }
    public bool Descending { get; init; }
    public GalleryDisplayMode DisplayMode { get; init; }
    public bool ShowFileNames { get; init; } = true;
    public bool ShowCards { get; init; } = true;
    public MediaFilter Filter { get; init; }
    public bool ShowSubfolders { get; init; } = true;
    public bool ShowHidden { get; init; }
    public double Volume { get; init; } = 100;
    public bool Muted { get; init; }
    public bool PlaylistOpen { get; init; }
    public bool PlaylistShowThumbnails { get; init; } = true;
    public bool DetailsOpen { get; init; }
    public string[] RecentFolders { get; init; } = [];
    public string[] FavoriteFolders { get; init; } = [];

    public AppSettings Normalize() => this with
    {
        Width = Math.Clamp(Width, 320, 7680),
        Height = Math.Clamp(Height, 240, 4320),
        LastFolder = NormalizeFolder(LastFolder),
        Theme = Theme is >= 0 and <= 2 ? Theme : 0,
        BackgroundOpacity = double.IsFinite(BackgroundOpacity) ? Math.Clamp(BackgroundOpacity, 0, 100) : 70,
        ThumbnailSize = double.IsFinite(ThumbnailSize) ? Math.Clamp(ThumbnailSize, 120, 480) : 240,
        SidebarWidth = double.IsFinite(SidebarWidth) ? Math.Clamp(SidebarWidth, 160, 480) : 260,
        SortField = Enum.IsDefined(SortField) ? SortField : MediaSortField.Name,
        TreeSortField = TreeSortField is MediaSortField.Name or MediaSortField.Modified ? TreeSortField : MediaSortField.Name,
        TreeTimeField = Enum.IsDefined(TreeTimeField) ? TreeTimeField : FolderTimeField.Modified,
        TreeExcludedFolders = (TreeExcludedFolders ?? []).Select(NormalizeFolder).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        DisplayMode = Enum.IsDefined(DisplayMode) ? DisplayMode : GalleryDisplayMode.Masonry,
        Filter = Enum.IsDefined(Filter) ? Filter : MediaFilter.All,
        Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, 100) : 100,
        RecentFolders = (RecentFolders ?? []).Select(NormalizeFolder).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray(),
        FavoriteFolders = (FavoriteFolders ?? []).Select(NormalizeFolder).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
    };

    private static string? NormalizeFolder(string? path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
                ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return null; }
    }
}

public sealed class AppSettingsStore(string path)
{
    private readonly SemaphoreSlim _writeGate = new(1);
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fallery", "settings.json");

    public Task<AppSettings> LoadAsync() => Task.Run(() =>
    {
        try
        {
            return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new()).Normalize();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new AppSettings();
        }
    });

    public async Task<bool> SaveAsync(AppSettings settings)
    {
        await _writeGate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                string? temporary = null;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                    temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllText(temporary, JsonSerializer.Serialize(settings.Normalize()));
                    File.Move(temporary, path, overwrite: true);
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
                finally
                {
                    if (temporary is not null)
                    {
                        try { File.Delete(temporary); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    }
                }
            });
        }
        finally { _writeGate.Release(); }
    }
}
