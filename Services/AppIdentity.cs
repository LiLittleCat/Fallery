using System.Reflection;
using System.Runtime.InteropServices;

namespace Fallery.Services;

public static class AppIdentity
{
#if FALLERY_STABLE
    public const string Channel = "stable";
    public const string Name = "Fallery";
#elif FALLERY_PREVIEW
    public const string Channel = "preview";
    public const string Name = "Fallery.Preview";
#else
    public const string Channel = "dev";
    public const string Name = "Fallery.Dev";
#endif
    public const string PackageId = Name + ".App";
    public const string InstanceKey = Name + ".Main";
    public static string DisplayName => Name.Replace('.', ' ');
    public static bool IsDevelopment => Channel == "dev";
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);
    public static string MotionDirectory => Path.Combine(Path.GetTempPath(), Name + "-Motion");
    public static string UpdateChannel => $"{Channel}-win-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
    public static string Version => typeof(AppIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.1.0";
    public static string UpdateSource => typeof(AppIdentity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(value => value.Key == "FalleryUpdateSource")?.Value ?? "";
}
