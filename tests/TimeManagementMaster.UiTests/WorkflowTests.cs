using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TimeManagementMaster.Core;

namespace TimeManagementMaster.UiTests;

// The real WPF controls run on one STA dispatcher; these tests never open the user's database.
[CollectionDefinition("WPF", DisableParallelization = true)]
public class WpfCollection;

[Collection("WPF")]
public class WorkflowTests
{
    private static readonly Lazy<Dispatcher> Ui = new(StartDispatcher);

    [Fact]
    public void AssignmentUsesChosenFutureDate() => OnUi(() =>
    {
        using var data = new TestDatabase();
        var date = new DateOnly(2027, 2, 12);
        var window = new AssignmentWindow(data.Db, "Assign", "AssignHint", null, null, date);
        var dates = (DateSelectionControl)window.FindName("Dates");
        Assert.True(dates.TryGetDates(out var selected, out _));
        Assert.Equal([date], selected);
        window.Close();
    });

    [Fact]
    public void SeparateCalendarDatesDoNotFillGapsOrLoseWeekends() => OnUi(() =>
    {
        var control = new DateSelectionControl();
        DateOnly[] expected = [new(2027, 2, 12), new(2027, 2, 14), new(2027, 2, 18)];
        control.SetDates([expected[2], expected[0], expected[1], expected[0]]);
        Assert.True(control.TryGetDates(out var selected, out _));
        Assert.Equal(expected, selected);
        control.SetDates([expected[0]]);
        Assert.True(control.TryGetDates(out selected, out _));
        Assert.Equal([expected[0]], selected);
    });

    [Fact]
    public void SearchPreservesPeopleSelectedAcrossDifferentQueries() => OnUi(() =>
    {
        var people = new EmployeeCheckList();
        people.LoadPeople([
            new(1, "Meier", "Anna", null, null, null, false),
            new(2, "Schmidt", "Lukas", null, null, null, false)
        ], [1]);
        ((TextBox)people.FindName("SearchBox")).Text = "Schmidt";
        var panel = (StackPanel)people.FindName("Panel");
        ((CheckBox)panel.Children[1]).IsChecked = true;
        Assert.Equal(new long[] { 1, 2 }, people.SelectedEmployees().Select(person => person.Id));
        ((TextBox)people.FindName("SearchBox")).Text = "Nobody";
        Assert.Equal(2, people.SelectedEmployees().Count);
        var clear = LogicalTreeHelper.GetChildren((DependencyObject)people.Content).OfType<DockPanel>().First();
        clear.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(people.SelectedEmployees());
    });

    [Fact]
    public void SiteCanBeCreatedWithOnlyAName() => OnUi(() =>
    {
        using var data = new TestDatabase();
        var window = new SiteEditorWindow(data.Db);
        InDialog(window, () =>
        {
            ((TextBox)window.FindName("NameBox")).Text = "  New site  ";
            Click(window, "SaveButton");
        });
        var site = Assert.Single(data.Db.SearchSites(null, false));
        Assert.Equal("New site", site.Name);
        Assert.Null(site.Address);
        Assert.Equal(site.Id, window.SavedSite?.Id);
    });

    [Fact]
    public void InvalidOrCancelledSiteDoesNotWriteAnything() => OnUi(() =>
    {
        using var data = new TestDatabase();
        data.Db.CreateSite("Nord", null, null, null, null);
        var window = new SiteEditorWindow(data.Db);
        InDialog(window, () =>
        {
            Click(window, "SaveButton");
            Assert.Equal(Visibility.Visible, ((TextBlock)window.FindName("ErrorText")).Visibility);
            ((TextBox)window.FindName("NameBox")).Text = "nord";
            Click(window, "SaveButton");
            Assert.Equal(Visibility.Visible, ((TextBlock)window.FindName("ErrorText")).Visibility);
            Assert.Single(data.Db.SearchSites(null, false));
            ((TextBox)window.FindName("NameBox")).Text = "Unsubmitted site";
        });
        Assert.Null(window.SavedSite);
        Assert.Single(data.Db.SearchSites(null, false));
    });

    [Fact]
    public void DialogLanguageSurvivesDispatcherExecutionContexts() => OnUi(() =>
    {
        LanguageService.Instance.Apply(AppLanguage.Ru, persist: false);
        using (ExecutionContext.SuppressFlow())
        {
            Assert.Equal("Создать объект", Task.Run(() => TextCatalog.Get("CreateSite")).GetAwaiter().GetResult());
        }
        using var data = new TestDatabase();
        var window = new SiteEditorWindow(data.Db);
        Assert.Equal("Новый объект", window.Title);
        window.Close();
    });

    private static void Click(Window window, string name) =>
        ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void GroupAssignmentStoresEnteredLunchForEveryChosenDayAndPerson() => OnUi(() =>
    {
        using var data = new TestDatabase();
        var first = data.Db.CreateEmployee("First", "Anna", null, null, null);
        var second = data.Db.CreateEmployee("Second", "Lukas", null, null, null);
        var site = data.Db.CreateSite("Nord", null, null, null, null);
        DateOnly[] dates = [new(2027, 2, 11), new(2027, 2, 12)];
        var window = new AssignmentWindow(data.Db, "Assign", "AssignHint", site.Id, [first.Id, second.Id], initialDates: dates);
        InDialog(window, () =>
        {
            var editor = (WorkTimeControl)window.FindName("WorkTimeEditor");
            ((TextBox)editor.FindName("LunchStartBox")).Text = "12:15";
            ((TextBox)editor.FindName("LunchEndBox")).Text = "12:45";
            Click(window, "SaveButton");
        });
        Assert.Equal(4, data.Db.ListAssignments().Count);
        Assert.All(data.Db.ListAssignments(), row => Assert.Equal(510, row.Time?.NetMinutes));
        Assert.All(data.Db.GetMonthlyHours(2027, 2), row => Assert.Equal("17:00", row.HoursText));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CalendarSickLeaveRequiresConfirmationBeforeReplacingWork(bool replace) => OnUi(() =>
    {
        using var data = new TestDatabase();
        var person = data.Db.CreateEmployee("First", "Anna", null, null, null);
        var site = data.Db.CreateSite("Nord", null, null, null, null);
        var date = new DateOnly(2027, 2, 12);
        var time = new WorkTime(new(8, 0), new(17, 0), new(12, 0), new(13, 0));
        data.Db.ApplyAssignments([new(person.Id, site.Id, date, time), new(person.Id, site.Id, date.AddDays(1), time)], false);
        var window = new CalendarEntryWindow(data.Db, person.Id, date, AbsenceKind.Sick);
        var seen = false;
        InDialog(window, () =>
        {
            window.Dispatcher.BeginInvoke(() =>
            {
                var conflict = Application.Current.Windows.OfType<ConflictWindow>().Single();
                seen = true;
                Click(conflict, replace ? "ReplaceButton" : "CancelButton");
            });
            Click(window, "SaveButton");
        });
        Assert.True(seen);
        Assert.Equal(replace ? CellKind.Sick : CellKind.Assignment, data.Db.GetEmployeeDay(person.Id, date).Kind);
        Assert.Equal(time, data.Db.GetEmployeeDay(person.Id, date.AddDays(1)).Time);
        Assert.Equal(replace ? 480 : 960, data.Db.GetMonthlyHours(2027, 2).Single().NetMinutes);
    });

    [Fact]
    public void WorkTimeInputsSubtractLunchAndRejectOutsideBreak() => OnUi(() =>
    {
        var control = new WorkTimeControl();
        Assert.True(control.TryGetTime(out var time, out _));
        Assert.Equal(480, time?.NetMinutes);
        ((TextBox)control.FindName("LunchEndBox")).Text = "18:00";
        Assert.False(control.TryGetTime(out _, out _));
        ((CheckBox)control.FindName("LunchBox")).IsChecked = false;
        Assert.True(control.TryGetTime(out time, out _));
        Assert.Equal(540, time?.NetMinutes);
        control.SetTime(null);
        Assert.True(control.TryGetTime(out time, out _));
        Assert.Null(time);
    });

    [Theory]
    [InlineData(AbsenceKind.Sick)]
    [InlineData(AbsenceKind.Vacation)]
    public void CalendarAbsenceUsesSelectedEmployeeAndDate(AbsenceKind kind) => OnUi(() =>
    {
        using var data = new TestDatabase();
        var first = data.Db.CreateEmployee("First", "Anna", null, null, null);
        var selected = data.Db.CreateEmployee("Second", "Lukas", null, null, null);
        var date = new DateOnly(2027, 2, 12);
        var window = new CalendarEntryWindow(data.Db, selected.Id, date, kind);
        InDialog(window, () =>
        {
            Assert.Equal(selected.Id, ((Employee)((ComboBox)window.FindName("EmployeeBox")).SelectedItem).Id);
            Assert.Equal(date.ToDateTime(TimeOnly.MinValue), ((DatePicker)window.FindName("StartDate")).SelectedDate);
            Click(window, "SaveButton");
        });
        var absence = Assert.Single(data.Db.ListAbsences());
        Assert.Equal(selected.Id, absence.EmployeeId);
        Assert.Equal(kind, absence.Kind);
        Assert.Equal(date, absence.StartDate);
        Assert.Equal(date, absence.EndDate);
        Assert.Empty(data.Db.ListAbsences(first.Id));
    });

    [Fact]
    public void CalendarWorkEntrySavesTimeAndLunch() => OnUi(() =>
    {
        using var data = new TestDatabase();
        var person = data.Db.CreateEmployee("First", "Anna", null, null, null);
        data.Db.CreateSite("Nord", null, null, null, null);
        var date = new DateOnly(2027, 2, 13); // Explicit Saturday must not disappear.
        var window = new CalendarEntryWindow(data.Db, person.Id, date, forceWork: true);
        InDialog(window, () => Click(window, "SaveButton"));
        var assignment = Assert.Single(data.Db.ListAssignments());
        Assert.Equal(date, assignment.Date);
        Assert.Equal(480, assignment.Time?.NetMinutes);
        var reopen = new CalendarEntryWindow(data.Db, person.Id, date);
        Assert.True(((WorkTimeControl)reopen.FindName("TimeEditor")).TryGetTime(out var time, out _));
        Assert.Equal(assignment.Time, time);
        reopen.Close();
    });

    [Fact]
    public void MonthlyReportShowsUntimedAssignmentsSeparately() => OnUi(() =>
    {
        using var data = new TestDatabase();
        var person = data.Db.CreateEmployee("First", "Anna", null, null, null);
        var site = data.Db.CreateSite("Nord", null, null, null, null);
        data.Db.ApplyAssignments([new(person.Id, site.Id, new(2027, 2, 12)), new(person.Id, site.Id, new(2027, 2, 13),
            new WorkTime(new(8, 0), new(17, 0), new(12, 0), new(13, 0)))], false);
        var window = new MonthlyHoursWindow(data.Db, new(2027, 2, 12));
        var row = Assert.Single(((DataGrid)window.FindName("SummaryGrid")).Items.OfType<MonthlyHoursRow>());
        Assert.Equal("8:00", row.HoursText);
        Assert.Equal(1, row.MissingTimeDays);
        Assert.Equal(2, ((DataGrid)window.FindName("DetailsGrid")).Items.Count);
        window.Close();
    });

    [Fact]
    public void SelectedSiteShowsItsNameInsteadOfRecordFields() => OnUi(() =>
    {
        using var data = new TestDatabase();
        data.Db.CreateSite("Nord", "Hafenstraße 4", null, null, null);
        var window = new AssignmentWindow(data.Db, "Assign", "AssignHint", null, null);
        InDialog(window, () =>
        {
            var combo = (ComboBox)window.FindName("SiteBox");
            combo.UpdateLayout();
            var text = Descendants<TextBlock>(combo).Select(block => block.Text).ToList();
            Assert.Contains("Nord", text);
            Assert.DoesNotContain(text, value => value.Contains("Id ="));
        });
    });

    [Fact]
    public void FirstSiteIsVisibleBeforeEmployeesAreAdded() => OnUi(() =>
    {
        using var data = new TestDatabase();
        data.Db.CreateSite("Nord", null, null, null, null);
        var window = new MainWindow(data.Db);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("EmptyState")).Visibility);
        Assert.NotEmpty(((StackPanel)window.FindName("SitePanel")).Children);
        Assert.Equal("0", ((TextBlock)window.FindName("TeamCount")).Text);
        window.Close();
    });

    [Fact]
    public void SiteActionsFollowActiveAndArchivedSelection() => OnUi(() =>
    {
        using var data = new TestDatabase();
        var site = data.Db.CreateSite("Nord", null, null, null, null);
        var window = new SitesWindow(data.Db);
        Assert.False(((Button)window.FindName("AssignButton")).IsEnabled);
        ((DataGrid)window.FindName("Grid")).SelectedIndex = 0;
        Assert.True(((Button)window.FindName("AssignButton")).IsEnabled);
        Assert.False(((Button)window.FindName("RestoreButton")).IsEnabled);
        data.Db.ArchiveSite(site.Id, DateOnly.FromDateTime(DateTime.Today));
        ((CheckBox)window.FindName("ArchiveBox")).IsChecked = true;
        Assert.False(((Button)window.FindName("AssignButton")).IsEnabled);
        Assert.True(((Button)window.FindName("RestoreButton")).IsEnabled);
        window.Close();
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void InDialog(Window window, Action test)
    {
        Exception? failure = null;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() =>
        {
            try { test(); }
            catch (Exception ex) { failure = ex; }
            finally { if (window.IsVisible) window.Close(); }
        });
        window.ShowDialog();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void OnUi(Action test) => Ui.Value.Invoke(test);

    private static Dispatcher StartDispatcher()
    {
        Dispatcher? dispatcher = null;
        Exception? failure = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/TimeManagementMaster;component/Theme.xaml", UriKind.Relative)
                });
                LanguageService.Instance.Apply(AppLanguage.Ru, persist: false);
                dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception ex) { failure = ex; }
            finally { ready.Set(); }
            if (failure is null) Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return dispatcher!;
    }

    private sealed class TestDatabase : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "tmm-ui-" + Guid.NewGuid().ToString("N"));
        public TimeManagementDatabase Db { get; }
        public TestDatabase() => Db = TimeManagementDatabase.Open(Path.Combine(_folder, "data.sqlite"), Path.Combine(_folder, "backups"));
        public void Dispose()
        {
            Db.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_folder, recursive: true);
        }
    }
}
