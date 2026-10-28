using System.Windows;
using System.Windows.Controls;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public partial class CalendarEntryWindow : Window
{
    private readonly TimeManagementDatabase _db;
    private bool _loading = true;

    public CalendarEntryWindow(TimeManagementDatabase db, long? employeeId, DateOnly date,
        AbsenceKind? absenceKind = null, bool forceWork = false)
    {
        InitializeComponent();
        _db = db;
        StartDate.SelectedDate = EndDate.SelectedDate = date.ToDateTime(TimeOnly.MinValue);
        WeekdaysBox.IsChecked = !CalendarDates.IsWeekend(date);
        var people = db.SearchEmployees(null, false).ToList();
        if (employeeId is long id && people.All(person => person.Id != id)) people.Add(db.GetEmployee(id));
        EmployeeBox.ItemsSource = people;
        EmployeeBox.SelectedItem = people.FirstOrDefault(person => person.Id == employeeId) ?? people.FirstOrDefault();
        SiteBox.ItemsSource = db.SearchSites(null, false);
        KindBox.ItemsSource = new[]
        {
            new EntryChoice("WorkDay", null), new EntryChoice("Vacation", AbsenceKind.Vacation),
            new EntryChoice("Sick", AbsenceKind.Sick), new EntryChoice("Unpaid", AbsenceKind.Unpaid), new EntryChoice("Other", AbsenceKind.Other)
        };
        var cell = CurrentCell();
        var kind = absenceKind ?? (forceWork ? null : CellAbsence(cell?.Kind));
        KindBox.SelectedItem = KindBox.Items.OfType<EntryChoice>().First(choice => choice.Kind == kind);
        if (cell?.SiteId is long siteId)
            SiteBox.SelectedItem = SiteBox.Items.OfType<Site>().FirstOrDefault(site => site.Id == siteId);
        if (SiteBox.SelectedItem is null && SiteBox.Items.Count > 0) SiteBox.SelectedIndex = 0;
        if (cell?.Kind == CellKind.Assignment) TimeEditor.SetTime(cell.Time);
        if (cell?.AbsenceId is long absenceId) NoteBox.Text = db.ListAbsences(employeeId).FirstOrDefault(leave => leave.Id == absenceId)?.Note ?? "";
        _loading = false;
        UpdateUi();
        ShowCurrent();
    }

    private static AbsenceKind? CellAbsence(CellKind? kind) => kind switch
    {
        CellKind.Vacation => AbsenceKind.Vacation, CellKind.Sick => AbsenceKind.Sick,
        CellKind.Unpaid => AbsenceKind.Unpaid, CellKind.Other => AbsenceKind.Other, _ => null
    };

    private DayCell? CurrentCell() => EmployeeBox.SelectedItem is Employee person && StartDate.SelectedDate is DateTime date
        ? _db.GetEmployeeDay(person.Id, DateOnly.FromDateTime(date)) : null;

    private void EmployeeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var cell = CurrentCell();
        if (cell?.Kind == CellKind.Assignment) TimeEditor.SetTime(cell.Time);
        if (cell?.SiteId is long siteId)
            SiteBox.SelectedItem = SiteBox.Items.OfType<Site>().FirstOrDefault(site => site.Id == siteId);
        ShowCurrent(); UpdateUi();
    }
    private void DateChanged(object sender, SelectionChangedEventArgs e) { if (!_loading) ShowCurrent(); }
    private void KindChanged(object sender, SelectionChangedEventArgs e) { if (!_loading) UpdateUi(); }
    private void OpenChanged(object sender, RoutedEventArgs e) { if (!_loading) UpdateUi(); }

    private void ShowCurrent()
    {
        var cell = CurrentCell();
        CurrentText.Text = L.F("CurrentEntry", cell?.Kind is null or CellKind.Empty ? L.T("Unassigned") : cell.Text
            + (cell.Time is WorkTime time ? " · " + time.RangeText + " · " + time.HoursText : ""));
    }

    private void UpdateUi()
    {
        if (KindBox.SelectedItem is not EntryChoice choice) return;
        WorkPanel.Visibility = choice.Kind is null ? Visibility.Visible : Visibility.Collapsed;
        Height = choice.Kind is null ? 800 : 640;
        OpenBox.Visibility = choice.Kind == AbsenceKind.Sick ? Visibility.Visible : Visibility.Collapsed;
        if (choice.Kind != AbsenceKind.Sick) OpenBox.IsChecked = false;
        EndDate.IsEnabled = choice.Kind != AbsenceKind.Sick || OpenBox.IsChecked != true;
        NotePanel.Visibility = choice.Kind == AbsenceKind.Other ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = EmployeeBox.SelectedItem is Employee { IsArchived: false };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (EmployeeBox.SelectedItem is not Employee person || KindBox.SelectedItem is not EntryChoice choice) { Error("PickPersonAndKind"); return; }
        if (StartDate.SelectedDate is not DateTime start) { Error("PickStart"); return; }
        var from = DateOnly.FromDateTime(start);
        DateOnly? to = null;
        if (choice.Kind != AbsenceKind.Sick || OpenBox.IsChecked != true)
        {
            if (EndDate.SelectedDate is not DateTime end) { Error("PickEnd"); return; }
            to = DateOnly.FromDateTime(end);
            if (to < from) { Error("EndBeforeStart"); return; }
        }
        if (choice.Kind is AbsenceKind kind)
        {
            if (kind == AbsenceKind.Other && string.IsNullOrWhiteSpace(NoteBox.Text)) { Error("OtherNoteRequired"); return; }
            var same = _db.ListAbsences(person.Id).FirstOrDefault(leave => leave.Kind == kind && leave.StartDate == from && leave.EndDate == to);
            if (!SaveFlow.TryApplyAbsence(this, _db, new AbsenceDraft(same?.Id, person.Id, kind, from, to, NoteBox.Text))) return;
        }
        else
        {
            if (SiteBox.SelectedItem is not Site site) { Error("PickSiteFirst"); return; }
            if (!TimeEditor.TryGetTime(out var time, out var error)) { ShowError(error!); return; }
            var dates = CalendarDates.Expand(from, to!.Value, from != to && WeekdaysBox.IsChecked == true);
            if (dates.Count == 0) { Error("NoDaysInRange"); return; }
            if (!SaveFlow.TryApplyAssignments(this, _db, dates.Select(date => new AssignmentOp(person.Id, site.Id, date, time)).ToList())) return;
        }
        DialogResult = true;
    }

    private void Register_Click(object sender, RoutedEventArgs e)
    {
        if (EmployeeBox.SelectedItem is not Employee person) return;
        new AbsenceWindow(_db, person.Id, StartDate.SelectedDate is DateTime date ? DateOnly.FromDateTime(date) : Ui.Today) { Owner = this }.ShowDialog();
        ShowCurrent();
    }
    private void Error(string key) => ShowError(L.T(key));
    private void ShowError(string text) { ErrorText.Text = text; ErrorText.Visibility = Visibility.Visible; }
    private sealed record EntryChoice(string Key, AbsenceKind? Kind) { public string Title => L.T(Key); }
}
