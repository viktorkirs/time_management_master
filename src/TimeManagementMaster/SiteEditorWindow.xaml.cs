using System.Windows;
using System.Windows.Controls;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public partial class SiteEditorWindow : Window
{
    private readonly TimeManagementDatabase _db;
    private readonly Site? _site;

    public Site? SavedSite { get; private set; }
    public bool AssignAfterSave => AssignNext.IsChecked == true;

    public SiteEditorWindow(TimeManagementDatabase db, Site? site = null, bool offerAssignment = false)
    {
        InitializeComponent();
        _db = db;
        _site = site;
        SetBinding(TitleProperty, LanguageService.Instance.Binding(site is null ? "NewSite" : "SiteCardTitle"));
        Heading.SetBinding(TextBlock.TextProperty, LanguageService.Instance.Binding(site is null ? "CreateSite" : "SiteCardTitle"));
        Hint.SetBinding(TextBlock.TextProperty, LanguageService.Instance.Binding(site is null ? "CreateSiteHint" : "EditSiteHint"));
        SaveButton.SetBinding(ContentControl.ContentProperty, LanguageService.Instance.Binding(site is null ? "CreateSite" : "Save"));
        AssignNext.Visibility = site is null && offerAssignment ? Visibility.Visible : Visibility.Collapsed;
        NameBox.Text = site?.Name ?? "";
        AddressBox.Text = site?.Address ?? "";
        ContactBox.Text = site?.ContactName ?? "";
        PhoneBox.Text = site?.Phone ?? "";
        NoteBox.Text = site?.Note ?? "";
        Details.IsExpanded = !string.IsNullOrEmpty(site?.ContactName) || !string.IsNullOrEmpty(site?.Phone) || !string.IsNullOrEmpty(site?.Note);
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void Name_Changed(object sender, TextChangedEventArgs e)
    {
        if (ErrorText is not null)
            ErrorText.Visibility = Visibility.Collapsed;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ShowError(L.T("SiteNameRequired"));
            NameBox.Focus();
            return;
        }
        try
        {
            SavedSite = _site is null
                ? _db.CreateSite(NameBox.Text, AddressBox.Text, ContactBox.Text, PhoneBox.Text, NoteBox.Text)
                : _db.UpdateSite(_site.Id, NameBox.Text, AddressBox.Text, ContactBox.Text, PhoneBox.Text, NoteBox.Text);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
