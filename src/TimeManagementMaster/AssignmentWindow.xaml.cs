using System.Windows;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public partial class AssignmentWindow : Window
{
    private readonly TimeManagementDatabase _db;
    private readonly long? _fixedSiteId;
    private readonly string _hintKey;

    public AssignmentWindow(
        TimeManagementDatabase db,
        string titleKey,
        string hintKey,
        long? fixedSiteId,
        IReadOnlyCollection<long>? preselected,
        DateOnly? initialDate = null,
        IReadOnlyCollection<DateOnly>? initialDates = null)
    {
        InitializeComponent();
        _db = db;
        _hintKey = hintKey;
        _fixedSiteId = fixedSiteId;
        SetBinding(TitleProperty, LanguageService.Instance.Binding(titleKey));
        LanguageService.Instance.LanguageChanged += OnLanguage;
        Closed += (_, _) => LanguageService.Instance.LanguageChanged -= OnLanguage;
        People.LoadPeople(_db.SearchEmployees(null, includeArchived: false), preselected);
        Dates.SetDates(initialDates is { Count: > 0 } ? initialDates : [initialDate ?? Ui.Today]);
        ApplyLanguage();
        var sites = _db.SearchSites(null, includeArchived: false);
        if (fixedSiteId is null)
        {
            SiteBox.ItemsSource = sites;
            if (sites.Count > 0)
                SiteBox.SelectedIndex = 0;
        }
    }

    private void OnLanguage(object? sender, EventArgs e) => Dispatcher.Invoke(ApplyLanguage);

    private void ApplyLanguage()
    {
        HintText.Text = L.T(_hintKey);
        if (_fixedSiteId is not long siteId)
            return;
        var site = _db.GetSite(siteId);
        SiteBox.Visibility = Visibility.Collapsed;
        NewSiteButton.Visibility = Visibility.Collapsed;
        FixedSiteText.Visibility = Visibility.Visible;
        FixedSiteText.Text = site.IsArchived
            ? L.F("ArchivedBlocked", site.Name)
            : site.Name;
        SaveButton.IsEnabled = !site.IsArchived;
    }

    private void AddSite_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SiteEditorWindow(_db) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SavedSite is null)
            return;
        var sites = _db.SearchSites(null, includeArchived: false);
        SiteBox.ItemsSource = sites;
        SiteBox.SelectedItem = sites.First(site => site.Id == dialog.SavedSite.Id);
    }

    private void AddEmployee_Click(object sender, RoutedEventArgs e)
    {
        var selected = People.SelectedEmployees().Select(person => person.Id).ToHashSet();
        var existing = _db.SearchEmployees(null, false).Select(person => person.Id).ToHashSet();
        if (!EmployeesWindow.AddEmployee(this, _db))
            return;
        var people = _db.SearchEmployees(null, false);
        selected.UnionWith(people.Where(person => !existing.Contains(person.Id)).Select(person => person.Id));
        People.LoadPeople(people, selected);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var people = People.SelectedEmployees();
        if (people.Count == 0)
        {
            Ui.Info(this, L.T("PickEmployees"));
            return;
        }

        long siteId;
        if (_fixedSiteId is long fixedId)
        {
            siteId = fixedId;
        }
        else if (SiteBox.SelectedItem is Site site)
        {
            siteId = site.Id;
        }
        else
        {
            Ui.Info(this, L.T("PickSiteFirst"));
            return;
        }

        if (!Dates.TryGetDates(out var dates, out var error))
        {
            Ui.Info(this, error ?? L.T("CheckDates"));
            return;
        }

        if (!WorkTimeEditor.TryGetTime(out var time, out var timeError))
        {
            Ui.Info(this, timeError ?? L.T("TimeFormatHint"));
            return;
        }
        var ops = people.SelectMany(person => dates.Select(date => new AssignmentOp(person.Id, siteId, date, time))).ToList();
        if (!SaveFlow.TryApplyAssignments(this, _db, ops))
            return;
        DialogResult = true;
    }
}
