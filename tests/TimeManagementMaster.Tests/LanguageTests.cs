using TimeManagementMaster.Core;

namespace TimeManagementMaster.Tests;

public class LanguageTests
{
    [Fact]
    public void DefaultLanguage_IsGerman()
    {
        Assert.Equal(AppLanguage.De, TextCatalog.DefaultLanguage);
        Assert.Equal("de", TextCatalog.DefaultCode);
        Assert.Equal("de", TextCatalog.Code(TextCatalog.DefaultLanguage));
    }

    [Fact]
    public void MissingChoice_LoadsGerman()
    {
        var path = Path.Combine(Path.GetTempPath(), "tmm-lang-" + Guid.NewGuid().ToString("N") + ".txt");
        Assert.False(File.Exists(path));
        Assert.Equal(AppLanguage.De, LanguagePreference.Load(path));
    }

    [Fact]
    public void SavedChoice_WinsOverDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), "tmm-lang-" + Guid.NewGuid().ToString("N") + ".txt");
        LanguagePreference.Save(AppLanguage.Ro, path);
        try
        {
            Assert.Equal(AppLanguage.Ro, LanguagePreference.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EveryKey_HasAllFourLanguages()
    {
        Assert.NotEmpty(TextCatalog.Keys);
        foreach (var key in TextCatalog.Keys)
        {
            var values = new List<string>();
            foreach (var language in TextCatalog.Languages)
            {
                var value = TextCatalog.Get(key, language);
                Assert.False(string.IsNullOrWhiteSpace(value), key + "/" + TextCatalog.Code(language));
                values.Add(value);
            }

            Assert.Equal(4, values.Count);
        }
    }

    [Fact]
    public void ImportantLabels_DifferAcrossLanguages()
    {
        foreach (var key in new[] { "Today", "Sick", "Unassigned", "Sites" })
        {
            var values = TextCatalog.Languages.Select(language => TextCatalog.Get(key, language)).ToList();
            Assert.Equal(4, values.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void RussianCatalog_KeepsAgreedCsvAndSickWording()
    {
        Assert.Equal(
            "дата;фамилия;имя;специальность;категория;объект;пояснение;адрес",
            TextCatalog.Get("CsvHeader", AppLanguage.Ru));
        Assert.Contains("больнич", TextCatalog.Get("OpenSickBlocks", AppLanguage.Ru), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Больничный (продолжается)", TextCatalog.Get("SickOngoing", AppLanguage.Ru));
        Assert.Equal("Другое: {0}", TextCatalog.Get("OtherNote", AppLanguage.Ru));
        Assert.Equal("Krankmeldung", TextCatalog.Get("Sick", AppLanguage.De));
        Assert.Equal("Heute", TextCatalog.Get("Today", AppLanguage.De));
    }
}
