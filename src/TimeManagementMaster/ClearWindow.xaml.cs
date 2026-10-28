using System.Windows;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public partial class ClearWindow : Window
{
    private readonly TimeManagementDatabase _db;

    public ClearWindow(TimeManagementDatabase db, IReadOnlyCollection<long>? preselected)
    {
        InitializeComponent();
        _db = db;
        People.LoadPeople(_db.SearchEmployees(null, includeArchived: false), preselected);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var people = People.SelectedEmployees();
        if (people.Count == 0)
        {
            Ui.Info(this, L.T("PickEmployees"));
            return;
        }

        if (!Dates.TryGetDates(out var dates, out var error))
        {
            Ui.Info(this, error ?? L.T("CheckDates"));
            return;
        }

        if (!Ui.Confirm(this, L.T("ClearConfirm"), L.T("ClearDays")))
            return;

        try
        {
            var keys = people.SelectMany(person => dates.Select(date => new DayKey(person.Id, date))).ToList();
            _db.ClearAssignments(keys);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Ui.Error(this, ex);
        }
    }
}
