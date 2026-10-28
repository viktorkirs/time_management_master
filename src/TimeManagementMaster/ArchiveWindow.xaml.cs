using System.Windows;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public partial class ArchiveWindow : Window
{
    private readonly Func<ArchivePreview> _load;

    public ArchiveWindow(string titleKey, string introKey, Func<ArchivePreview> load)
    {
        InitializeComponent();
        _load = load;
        SetBinding(TitleProperty, LanguageService.Instance.Binding(titleKey));
        IntroText.SetBinding(System.Windows.Controls.TextBlock.TextProperty, LanguageService.Instance.Binding(introKey));
        LanguageService.Instance.LanguageChanged += OnLanguage;
        Closed += (_, _) => LanguageService.Instance.LanguageChanged -= OnLanguage;
        Rebuild();
    }

    private void OnLanguage(object? sender, EventArgs e) => Dispatcher.Invoke(Rebuild);

    private void Rebuild()
    {
        Lines.Items.Clear();
        var preview = _load();
        if (preview.Lines.Count == 0)
            Lines.Items.Add(L.T("NoFutureRecords"));
        else
            foreach (var line in preview.Lines)
                Lines.Items.Add(line);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
