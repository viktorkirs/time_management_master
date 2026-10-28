using System.Windows;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public partial class SiteCardWindow : Window
{
    private readonly TimeManagementDatabase _db;
    private readonly long _siteId;

    public SiteCardWindow(TimeManagementDatabase db, long siteId, DateOnly? date = null)
    {
        InitializeComponent();
        _db = db;
        _siteId = siteId;
        var today = (date ?? Ui.Today).ToDateTime(TimeOnly.MinValue);
        FromDate.SelectedDate = today;
        ToDate.SelectedDate = today;
        LanguageService.Instance.LanguageChanged += OnLanguage;
        Closed += (_, _) => LanguageService.Instance.LanguageChanged -= OnLanguage;
        Reload();
    }

    private void OnLanguage(object? sender, EventArgs e) => Dispatcher.Invoke(Reload);

    private void Show_Click(object sender, RoutedEventArgs e) => Reload();

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SiteEditorWindow(_db, _db.GetSite(_siteId)) { Owner = this };
        if (dialog.ShowDialog() == true)
            Reload();
    }

    private void Assign_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AssignmentWindow(
            _db,
            "AssignToSite",
            "AssignSiteHint",
            _siteId,
            null,
            FromDate.SelectedDate is DateTime start ? DateOnly.FromDateTime(start) : Ui.Today)
        { Owner = this };
        if (dialog.ShowDialog() == true)
            Reload();
    }

    private void Reload()
    {
        var site = _db.GetSite(_siteId);
        TitleText.Text = site.IsArchived ? L.F("ArchivedName", site.Name) : site.Name;
        if (!string.IsNullOrWhiteSpace(site.Address))
            TitleText.Text += " — " + site.Address;
        ContactText.Text = string.Join(" · ", new[] { site.ContactName, site.Phone, site.Note }.Where(value => !string.IsNullOrWhiteSpace(value)));
        ContactText.Visibility = ContactText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        AssignButton.IsEnabled = !site.IsArchived;
        if (FromDate.SelectedDate is null || ToDate.SelectedDate is null)
            return;
        var from = DateOnly.FromDateTime(FromDate.SelectedDate.Value);
        var to = DateOnly.FromDateTime(ToDate.SelectedDate.Value);
        if (to < from)
        {
            Ui.Info(this, L.T("EndBeforeStart"));
            return;
        }

        var rows = _db.GetSiteAssignments(_siteId, from, to)
            .Select(line => new Row(TextCatalog.FormatDate(line.Date), line.EmployeeName, line.Specialty ?? ""))
            .ToList();
        Grid.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = L.T("NoAssignmentsInRange");
    }

    private sealed record Row(string DateText, string EmployeeName, string Specialty);
}
