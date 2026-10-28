using System.Windows;
using System.Windows.Input;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public partial class EmployeesWindow : Window
{
    private readonly TimeManagementDatabase _db;

    public EmployeesWindow(TimeManagementDatabase db)
    {
        InitializeComponent();
        _db = db;
        LoadRows();
    }

    public static bool AddEmployee(Window owner, TimeManagementDatabase db)
    {
        var form = new TextForm(owner, "NewEmployee",
        [
            ("LastNameStar", "", 0),
            ("FirstNameStar", "", 0),
            ("Specialty", "", 0),
            ("Phone", "", 40),
            ("Note", "", 0)
        ], values => SaveNew(db, values));
        return form.ShowDialog() == true;
    }

    private void Reload(object sender, RoutedEventArgs e) => LoadRows();

    private void LoadRows()
    {
        if (SearchBox is null || Grid is null)
            return;
        Grid.ItemsSource = _db.SearchEmployees(SearchBox.Text, ArchiveBox.IsChecked == true);
    }

    private Employee? Selected() => Grid.SelectedItem as Employee;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (AddEmployee(this, _db))
            LoadRows();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (e is MouseButtonEventArgs mouse && mouse.ChangedButton != MouseButton.Left)
            return;
        var person = Selected();
        if (person is null)
        {
            Ui.Info(this, L.T("PickEmployee"));
            return;
        }

        var form = new TextForm(this, "EmployeeCard",
        [
            ("LastNameStar", person.LastName, 0),
            ("FirstNameStar", person.FirstName, 0),
            ("Specialty", person.Specialty ?? "", 0),
            ("Phone", person.Phone ?? "", 40),
            ("Note", person.Note ?? "", 0)
        ], values => SaveExisting(_db, person.Id, values));
        if (form.ShowDialog() == true)
            LoadRows();
    }

    private void Archive_Click(object sender, RoutedEventArgs e)
    {
        var person = Selected();
        if (person is null)
        {
            Ui.Info(this, L.T("PickEmployee"));
            return;
        }

        if (person.IsArchived)
        {
            Ui.Info(this, L.T("AlreadyArchivedEmployee"));
            return;
        }

        var today = Ui.Today;
        var dialog = new ArchiveWindow(
            "ArchiveEmployeeTitle",
            "ArchiveEmployeeIntro",
            () => _db.PreviewEmployeeArchive(person.Id, today))
        { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            _db.ArchiveEmployee(person.Id, today);
            LoadRows();
        }
        catch (Exception ex)
        {
            Ui.Error(this, ex);
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var person = Selected();
        if (person is null)
        {
            Ui.Info(this, L.T("PickEmployee"));
            return;
        }

        if (!person.IsArchived)
        {
            Ui.Info(this, L.T("NotArchivedEmployee"));
            return;
        }

        try
        {
            _db.RestoreEmployee(person.Id);
            LoadRows();
        }
        catch (Exception ex)
        {
            Ui.Error(this, ex);
        }
    }

    private static string? SaveNew(TimeManagementDatabase db, string[] values)
    {
        try
        {
            db.CreateEmployee(values[0], values[1], values[2], values[3], values[4]);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string? SaveExisting(TimeManagementDatabase db, long id, string[] values)
    {
        try
        {
            db.UpdateEmployee(id, values[0], values[1], values[2], values[3], values[4]);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
