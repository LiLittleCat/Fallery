namespace Fallery.Services;

/// <summary>设置中的语言预览；取消不能覆盖另一窗口较新的选择。</summary>
public sealed class LanguagePreview
{
    private readonly string _original = AppText.Language;
    private long _revision = -1;

    public void Select(string language)
    {
        if (AppText.NormalizeLanguage(language) == AppText.Language) return;
        AppText.SetLanguage(language);
        _revision = AppText.Revision;
    }

    public void Finish(bool keep)
    {
        if (!keep && _revision == AppText.Revision) AppText.SetLanguage(_original);
    }
}
