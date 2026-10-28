using TimeManagementMaster.Core;

namespace TimeManagementMaster.Tests;

internal sealed class LanguageScope : IDisposable
{
    private readonly AppLanguage _prior;

    public LanguageScope(AppLanguage language)
    {
        _prior = TextCatalog.Current;
        TextCatalog.Current = language;
    }

    public void Dispose() => TextCatalog.Current = _prior;
}
