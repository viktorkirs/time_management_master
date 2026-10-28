namespace TimeManagementMaster.Core;

public static class LanguagePreference
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TimeManagementMaster",
        "ui-language.txt");

    public static AppLanguage Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            if (!File.Exists(file))
                return TextCatalog.DefaultLanguage;
            return TextCatalog.FromCode(File.ReadAllText(file)) ?? TextCatalog.DefaultLanguage;
        }
        catch (IOException)
        {
            return TextCatalog.DefaultLanguage;
        }
        catch (UnauthorizedAccessException)
        {
            return TextCatalog.DefaultLanguage;
        }
    }

    public static void Save(AppLanguage language, string? path = null)
    {
        var file = path ?? DefaultPath;
        var folder = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, TextCatalog.Code(language));
        File.Move(temporary, file, overwrite: true);
    }
}
