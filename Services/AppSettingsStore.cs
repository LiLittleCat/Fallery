using System.Text.Json;
using Fallery.Models;

namespace Fallery.Services;

public sealed record AppSettings
{
    public int Width { get; init; } = 1400;
    public int Height { get; init; } = 900;
    public bool Maximized { get; init; }
    public string? LastFolder { get; init; }
    public int Theme { get; init; }
    public double ThumbnailSize { get; init; } = 240;
    public double SidebarWidth { get; init; } = 260;
    public MediaSortField SortField { get; init; }
    public bool Descending { get; init; }
    public GalleryDisplayMode DisplayMode { get; init; }
    public bool ShowFileNames { get; init; } = true;
    public MediaFilter Filter { get; init; }
    public bool ShowSubfolders { get; init; } = true;

    public AppSettings Normalize() => this with
    {
        Width = Math.Clamp(Width, 800, 7680),
        Height = Math.Clamp(Height, 500, 4320),
        LastFolder = NormalizeFolder(LastFolder),
        Theme = Theme is >= 0 and <= 2 ? Theme : 0,
        ThumbnailSize = double.IsFinite(ThumbnailSize) ? Math.Clamp(ThumbnailSize, 120, 480) : 240,
        SidebarWidth = double.IsFinite(SidebarWidth) ? Math.Clamp(SidebarWidth, 160, 480) : 260,
        SortField = Enum.IsDefined(SortField) ? SortField : MediaSortField.Name,
        DisplayMode = Enum.IsDefined(DisplayMode) ? DisplayMode : GalleryDisplayMode.Masonry,
        Filter = Enum.IsDefined(Filter) ? Filter : MediaFilter.All,
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
