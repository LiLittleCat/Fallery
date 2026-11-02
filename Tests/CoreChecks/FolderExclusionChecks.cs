using System.Text.Json;
using Fallery.Services;

internal static class FolderExclusionChecks
{
    public static async Task<int> RunAsync(string fixture)
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        string[] original = FolderExclusionRules.Names;
        string root = Path.Combine(fixture, "exclusion-checks");
        Directory.CreateDirectory(Path.Combine(root, "node_modules"));
        Directory.CreateDirectory(Path.Combine(root, "custom-cache"));
        File.WriteAllText(Path.Combine(root, "node_modules", "sample.png"), "fixture");
        File.WriteAllText(Path.Combine(root, "custom-cache", "sample.png"), "fixture");
        try
        {
            var oldSettings = JsonSerializer.Deserialize<AppSettings>("{}")!.Normalize();
            Check(oldSettings.ExcludedDirectoryNames.Contains("node_modules") && oldSettings.ExcludedDirectoryNames.Contains(".git"), "Old settings receive default exclusions");
            Check(new AppSettings { ExcludedDirectoryNames = [] }.Normalize().ExcludedDirectoryNames.Length == 0, "Explicit empty rules must stay empty");
            Check(FolderExclusionRules.Normalize(["node_modules", "NODE_MODULES", "../private", "", "bad*name", "path\\child"])
                .SequenceEqual(["node_modules"]), "Rules reject paths and wildcards and deduplicate case-insensitively");
            FolderExclusionRules.SetNames(FolderExclusionRules.Defaults);
            Check(!FolderScanner.ScanEntries(root).Any(item => item.Name == "node_modules"), "Default dependency folder is excluded from media scanning");
            FolderExclusionRules.SetNames([]);
            Check(FolderScanner.ScanEntries(root).Any(item => item.Name == "node_modules"), "Removing a default rule restores scanning");
            FolderExclusionRules.SetNames(["CUSTOM-CACHE"]);
            Check(!FolderScanner.ScanEntries(root).Any(item => item.Name == "custom-cache"), "Custom names match without case sensitivity");
            var store = new AppSettingsStore(Path.Combine(root, "settings.json"));
            Check(await store.SaveAsync(new AppSettings { ExcludedDirectoryNames = FolderExclusionRules.Names }), "Save custom exclusion settings");
            Check((await store.LoadAsync()).ExcludedDirectoryNames.SequenceEqual(["CUSTOM-CACHE"]), "Custom rules survive persistence");
            await store.SaveAsync(new AppSettings { ExcludedDirectoryNames = [] });
            Check((await store.LoadAsync()).ExcludedDirectoryNames.Length == 0, "Disabled defaults stay disabled after restart");
            FolderExclusionRules.SetNames(FolderExclusionRules.Defaults);
            Check(FolderScanner.IsExcludedDirectory(Path.Combine(root, "NODE_MODULES")), "Restoring defaults restores scanner behavior");
        }
        finally
        {
            FolderExclusionRules.SetNames(original);
            Directory.Delete(root, true);
        }
        return checks;
    }
}
