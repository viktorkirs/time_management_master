using System.Windows;
using System.Windows.Controls;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public partial class AbsenceWindow : Window
{
    private readonly TimeManagementDatabase _db;
    public AbsenceWindow(TimeManagementDatabase db, long? employeeId, DateOnly? date = null)
    {
        InitializeComponent();
        _db = db;
        LanguageService.Instance.LanguageChanged += OnLanguage;
        Closed += (_, _) => LanguageService.Instance.LanguageChanged -= OnLanguage;
        FillKinds(AbsenceKind.Sick);
        var today = (date ?? Ui.Today).ToDateTime(TimeOnly.MinValue);
        StartDate.SelectedDate = today;
        EndDate.SelectedDate = today;
        EmployeeBox.ItemsSource = _db.SearchEmployees(null, includeArchived: false);
        if (employeeId is long id)
            EmployeeBox.SelectedItem = EmployeeBox.Items.OfType<Employee>().FirstOrDefault(person => person.Id == id);
        else if (EmployeeBox.Items.Count > 0)
            EmployeeBox.SelectedIndex = 0;
        UpdateKindUi();
        Reload();
    }

    private void OnLanguage(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
        var kind = (KindBox.SelectedItem as KindItem)?.Kind ?? AbsenceKind.Sick;
        FillKinds(kind);
        Reload();
    });

    private void FillKinds(AbsenceKind selected)
    {
        var items = new[]
        {
            new KindItem(AbsenceKind.Sick, L.T("Sick")),
            new KindItem(AbsenceKind.Vacation, L.T("Vacation")),
            new KindItem(AbsenceKind.Unpaid, L.T("Unpaid")),
            new KindItem(AbsenceKind.Other, L.T("Other"))
        };
        KindBox.ItemsSource = items;
        KindBox.SelectedItem = items.First(item => item.Kind == selected);
    }

    private void EmployeeChanged(object sender, SelectionChangedEventArgs e) => Reload();

    private void KindChanged(object sender, SelectionChangedEventArgs e) => UpdateKindUi();

    private void OpenChanged(object sender, RoutedEventArgs e) => UpdateKindUi();

    private void UpdateKindUi()
    {
        if (OpenBox is null || KindBox.SelectedItem is not KindItem kind)
            return;
        var sick = kind.Kind == AbsenceKind.Sick;
        OpenBox.Visibility = sick ? Visibility.Visible : Visibility.Collapsed;
        if (!sick)
            OpenBox.IsChecked = false;
        EndDate.IsEnabled = OpenBox.IsChecked != true;
    }

    private void Reload()
    {
        if (EmployeeBox.SelectedItem is not Employee employee)
        {
            AbsenceGrid.ItemsSource = null;
            return;
        }

        AbsenceGrid.ItemsSource = _db.ListAbsences(employee.Id)
            .OrderByDescending(row => row.StartDate)
            .Select(row => new AbsenceLine(row))
            .ToList();
        CloseSickButton.IsEnabled = Selected()?.Row.IsOpen == true;
    }

    private void AbsenceSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        CloseSickButton.IsEnabled = Selected()?.Row.IsOpen == true;

    private AbsenceLine? Selected() => AbsenceGrid.SelectedItem as AbsenceLine;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (EmployeeBox.SelectedItem is not Employee employee || KindBox.SelectedItem is not KindItem kind)
        {
            Ui.Info(this, L.T("PickPersonAndKind"));
            return;
        }

        if (StartDate.SelectedDate is null)
        {
            Ui.Info(this, L.T("PickStart"));
            return;
        }

        var open = kind.Kind == AbsenceKind.Sick && OpenBox.IsChecked == true;
        DateOnly? end = null;
        if (!open)
        {
            if (EndDate.SelectedDate is null)
            {
                Ui.Info(this, L.T("PickEnd"));
                return;
            }

            end = DateOnly.FromDateTime(EndDate.SelectedDate.Value);
        }

        var draft = new AbsenceDraft(
            null,
            employee.Id,
            kind.Kind,
            DateOnly.FromDateTime(StartDate.SelectedDate.Value),
            end,
            NoteBox.Text);
        if (!SaveFlow.TryApplyAbsence(this, _db, draft))
            return;
        NoteBox.Clear();
        Reload();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        var line = Selected();
        var employee = EmployeeBox.SelectedItem as Employee;
        if (line is null || employee is null)
        {
            Ui.Info(this, L.T("PickAbsence"));
            return;
        }

        if (!Prompts.TryPeriod(
                this,
                "ChangePeriod",
                line.Row.StartDate,
                line.Row.EndDate,
                line.Row.Kind == AbsenceKind.Sick,
                line.Row.IsOpen,
                out var start,
                out var end))
            return;

        var draft = new AbsenceDraft(line.Row.Id, employee.Id, line.Row.Kind, start, end, line.Row.Note);
        if (!SaveFlow.TryApplyAbsence(this, _db, draft))
            return;
        Reload();
    }

    private void CloseSick_Click(object sender, RoutedEventArgs e)
    {
        var line = Selected();
        var employee = EmployeeBox.SelectedItem as Employee;
        if (line is null || employee is null || !line.Row.IsOpen)
        {
            Ui.Info(this, L.T("PickOpenSick"));
            return;
        }

        if (!Prompts.TryPeriod(this, "SickEndTitle", line.Row.StartDate, Ui.Today, false, false, out var start, out var end))
            return;
        if (end is null)
            return;
        var draft = new AbsenceDraft(line.Row.Id, employee.Id, AbsenceKind.Sick, start, end, null);
        if (!SaveFlow.TryApplyAbsence(this, _db, draft))
            return;
        Reload();
    }

    private void CancelAbsence_Click(object sender, RoutedEventArgs e)
    {
        var line = Selected();
        if (line is null)
        {
            Ui.Info(this, L.T("PickAbsence"));
            return;
        }

        if (!Ui.Confirm(this, L.T("CancelAbsenceConfirm"), L.T("Absence")))
            return;
        try
        {
            _db.DeleteAbsence(line.Row.Id);
            Reload();
        }
        catch (Exception ex)
        {
            Ui.Error(this, ex);
        }
    }

    private sealed record KindItem(AbsenceKind Kind, string Title);

    private sealed class AbsenceLine
    {
        public AbsenceLine(AbsenceRow row) => Row = row;

        public AbsenceRow Row { get; }

        public string Period => Labels.Period(Row.StartDate, Row.EndDate);

        public string Label => Labels.Absence(Row.Kind, Row.Note, Row.IsOpen);
    }
}
