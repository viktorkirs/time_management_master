using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public sealed class LanguageService : INotifyPropertyChanged
{
    public static LanguageService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public string this[string key] => TextCatalog.Get(key);

    public Binding Binding(string key) => new($"[{key}]")
    {
        Source = this,
        Mode = BindingMode.OneWay
    };

    public void Apply(AppLanguage language, bool persist)
    {
        var changed = TextCatalog.Current != language;
        TextCatalog.SetApplicationLanguage(language);
        TextCatalog.Current = language;
        if (persist)
            LanguagePreference.Save(language);

        var culture = TextCatalog.Culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        if (Application.Current is not null)
        {
            var xml = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
            foreach (Window window in Application.Current.Windows)
                window.Language = xml;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        if (changed)
            LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LanguageService.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}

internal static class Palette
{
    public static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}

internal static class L
{
    public static string T(string key) => TextCatalog.Get(key);

    public static string F(string key, params object?[] args) => TextCatalog.Format(key, args);
}
