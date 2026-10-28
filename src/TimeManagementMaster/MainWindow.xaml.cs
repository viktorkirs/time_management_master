using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public partial class MainWindow : Window
{
    private readonly TimeManagementDatabase _db;
    private readonly Dictionary<DataGridColumn, DateOnly> _columnDates = new();
    private DateOnly _cursor;
    private bool _suppress;

    public MainWindow(TimeManagementDatabase db)
    {
        InitializeComponent();
        _db = db;
        _cursor = Ui.Today;
        LanguageService.Instance.LanguageChanged += OnLanguage;
        Closed += (_, _) => LanguageService.Instance.LanguageChanged -= OnLanguage;
        LangBox.ItemsSource = new LangChoice[]
        {
            new("de", TextCatalog.Get("LangDe")),
            new("ru", TextCatalog.Get("LangRu")),
            new("en", TextCatalog.Get("LangEn")),
            new("ro", TextCatalog.Get("LangRo"))
        };
        SelectLanguage();
        ReloadSiteFilter();
        Refresh();
    }

    private void OnLanguage(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
        SelectLanguage();
        ReloadSiteFilter();
        Refresh();
    });

    private void SelectLanguage()
    {
        _suppress = true;
        var code = TextCatalog.Code(TextCatalog.Current);
        LangBox.SelectedItem = LangBox.Items.OfType<LangChoice>().FirstOrDefault(item => item.Code == code);
        _suppress = false;
    }

    private void Lang_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || LangBox.SelectedItem is not LangChoice choice)
            return;
        var language = TextCatalog.FromCode(choice.Code);
        if (language is null || language.Value == TextCatalog.Current)
            return;
        LanguageService.Instance.Apply(language.Value, persist: true);
    }

    private void ViewChanged(object sender, RoutedEventArgs e) => Refresh();

    private void RangeChanged(object sender, RoutedEventArgs e) => Refresh();

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress)
            return;
        Refresh();
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => Shift(-1);

    private void Next_Click(object sender, RoutedEventArgs e) => Shift(1);

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _cursor = Ui.Today;
        Refresh();
    }

    private void JumpDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _db is null || JumpDate.SelectedDate is null)
            return;
        var date = DateOnly.FromDateTime(JumpDate.SelectedDate.Value);
        if (date == _cursor)
            return;
        _cursor = date;
        Refresh();
    }

    private void Shift(int direction)
    {
        if (ByEmployeeRadio.IsChecked == true && WeekRadio.IsChecked == true)
            _cursor = _cursor.AddDays(7 * direction);
        else if (ByEmployeeRadio.IsChecked == true && MonthRadio.IsChecked == true)
            _cursor = _cursor.AddMonths(direction);
        else
            _cursor = _cursor.AddDays(direction);
        Refresh();
    }

    private void Refresh()
    {
        if (SitePanel is null || _db is null)
            return;

        var byEmployee = ByEmployeeRadio.IsChecked == true;
        RangePanel.Visibility = byEmployee ? Visibility.Visible : Visibility.Collapsed;
        QuickCalendarBar.Visibility = byEmployee ? Visibility.Visible : Visibility.Collapsed;
        var (from, to) = VisibleRange();
        RangeLabel.Text = byEmployee && MonthRadio.IsChecked == true
            ? TextCatalog.FormatMonth(_cursor)
            : from == to
                ? _cursor.ToString(TextCatalog.Current == AppLanguage.De ? "d. MMMM yyyy" : "d MMMM yyyy", TextCatalog.Culture)
                : TextCatalog.FormatDate(from) + " — " + TextCatalog.FormatDate(to);

        _suppress = true;
        JumpDate.SelectedDate = _cursor.ToDateTime(TimeOnly.MinValue);
        _suppress = false;

        var board = _db.GetDayBoard(_cursor);
        TeamCount.Text = board.ActiveEmployeeCount.ToString(TextCatalog.Culture);
        AssignedCount.Text = board.AssignedHeadcount.ToString(TextCatalog.Culture);
        AbsentCount.Text = board.Absences.Count.ToString(TextCatalog.Culture);
        SummaryDate.Text = TextCatalog.FormatDate(_cursor);
        BoardTitle.Text = L.T(byEmployee ? "EmployeeBoardTitle" : "SiteBoardTitle");
        HeadcountText.Text = L.F("AssignedOn", TextCatalog.FormatDate(_cursor), board.AssignedHeadcount);
        ShowRail(board);
        SetStatus(L.F("OverviewDate", TextCatalog.FormatDate(_cursor)));
        StatusText.ToolTip = _db.FilePath;

        var anyEmployees = _db.SearchEmployees(null, false).Count;
        var anySites = _db.SearchSites(null, false).Count;
        var blocked = anyEmployees == 0 && anySites == 0 && board.AssignedHeadcount == 0 && board.Absences.Count == 0;
        EmptyBody.Children.Clear();
        if (blocked)
        {
            EmptyBody.Children.Add(new TextBlock
            {
                Text = L.T("GettingStarted"), FontSize = 24, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            });
            EmptyBody.Children.Add(EmptySentence("GettingStartedHint"));
            var actions = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };
            if (anySites == 0)
                actions.Children.Add(EmptyButton("AddSite", primary: true, (_, _) =>
                {
                    if (SitesWindow.AddSite(this, _db, _cursor))
                        AfterDirectory();
                }));
            if (anyEmployees == 0)
                actions.Children.Add(EmptyButton("AddEmployee", primary: false, (_, _) =>
                {
                    if (EmployeesWindow.AddEmployee(this, _db))
                        AfterDirectory();
                }));
            if (_db.SearchEmployees(null, true).Count == 0 && _db.SearchSites(null, true).Count == 0)
                actions.Children.Add(EmptyButton("DemoData", primary: false, Demo_Click));
            EmptyBody.Children.Add(actions);
        }
        EmptyState.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
        if (blocked)
        {
            SiteScroll.Visibility = Visibility.Collapsed;
            EmployeeGrid.Visibility = Visibility.Collapsed;
            return;
        }

        SiteScroll.Visibility = byEmployee ? Visibility.Collapsed : Visibility.Visible;
        EmployeeGrid.Visibility = byEmployee ? Visibility.Visible : Visibility.Collapsed;
        if (byEmployee)
            BuildEmployeeGrid(from, to);
        else
            BuildSiteView(board);
    }

    private (DateOnly From, DateOnly To) VisibleRange()
    {
        if (ByEmployeeRadio.IsChecked != true || DayRadio.IsChecked == true)
            return (_cursor, _cursor);
        if (WeekRadio.IsChecked == true)
        {
            var monday = CalendarDates.MondayOf(_cursor);
            return (monday, monday.AddDays(6));
        }

        var first = new DateOnly(_cursor.Year, _cursor.Month, 1);
        return (first, first.AddMonths(1).AddDays(-1));
    }

    private void ShowRail(DayBoard board)
    {
        if (CalendarDates.IsWeekend(board.Date))
        {
            UnassignedCount.Text = "—";
            UnassignedHint.Text = L.T("WeekendNoUnassigned");
            return;
        }

        UnassignedCount.Text = board.Unassigned.Count.ToString(TextCatalog.Culture);
        UnassignedHint.Text = board.ActiveEmployeeCount > 0 && board.Unassigned.Count == 0
            ? L.T("AllAssigned")
            : TextCatalog.FormatDate(board.Date);
    }

    private static TextBlock EmptySentence(string textKey) => new()
    {
        Text = L.T(textKey),
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14,
        MaxWidth = 480,
        Margin = new Thickness(0, 0, 0, 8)
    };

    private Button EmptyButton(string textKey, bool primary, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = L.T(textKey),
            Style = (Style)FindResource(primary ? "PrimaryButton" : typeof(Button)),
            Margin = new Thickness(0, 0, 8, 0)
        };
        button.Click += click;
        return button;
    }

    private void BuildSiteView(DayBoard board)
    {
        SitePanel.Children.Clear();
        var query = SearchBox.Text.Trim();
        var siteFilter = SelectedSiteId();
        foreach (var group in board.Sites)
        {
            if (siteFilter is long only && group.Site.Id != only)
                continue;
            var people = group.Employees.Where(person => Matches(person, query)).ToList();
            if (query.Length > 0 && people.Count == 0)
                continue;
            SitePanel.Children.Add(SiteCard(group, people, query.Length > 0));
        }

        SitePanel.Children.Add(Card(UnassignedBlock(board, query)));
        SitePanel.Children.Add(Card(AbsenceBlock(board, query)));
    }

    private UIElement SiteCard(SiteDayGroup group, List<Employee> people, bool filtered)
    {
        var root = new StackPanel();
        var header = new DockPanel { LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (!group.Site.IsArchived)
        {
            var assign = new Button { Content = L.T("Assign"), Margin = new Thickness(0, 0, 8, 0) };
            assign.Click += (_, _) => AssignAtSite(group.Site.Id);
            actions.Children.Add(assign);
        }
        var details = new Button { Content = L.T("Card"), Margin = new Thickness(0) };
        details.Click += (_, _) =>
        {
            new SiteCardWindow(_db, group.Site.Id, _cursor) { Owner = this }.ShowDialog();
            AfterDirectory();
        };
        actions.Children.Add(details);
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        var pill = new Border
        {
            Background = Palette.Brush("AccentBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = group.Count.ToString(TextCatalog.Culture),
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            }
        };
        var badge = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.Children.Add(new TextBlock
        {
            Text = L.T("OnSite"),
            Foreground = Palette.Brush("MutedBrush"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        badge.Children.Add(pill);
        DockPanel.SetDock(badge, Dock.Right);
        header.Children.Add(badge);
        var titles = new StackPanel();
        var name = group.Site.IsArchived ? L.F("ArchivedName", group.Site.Name) : group.Site.Name;
        titles.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(group.Site.Address))
        {
            titles.Children.Add(new TextBlock
            {
                Text = group.Site.Address,
                Foreground = Palette.Brush("MutedBrush"),
                FontSize = 13,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }

        header.Children.Add(titles);
        root.Children.Add(header);
        if (filtered && people.Count != group.Employees.Count)
        {
            root.Children.Add(new TextBlock
            {
                Text = L.F("ShownOf", people.Count, group.Employees.Count),
                Foreground = Palette.Brush("MutedBrush"),
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0)
            });
        }

        if (people.Count == 0)
            root.Children.Add(Note(L.T("NoAssignments")));
        else
            foreach (var person in people)
                root.Children.Add(PersonRow(person));
        return Card(root);
    }

    private UIElement PersonRow(Employee person)
    {
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var remove = new Button
        {
            Content = L.T("Remove"),
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(8, 4, 8, 4),
            MinHeight = 28,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(remove, Dock.Right);
        remove.Click += (_, _) => ClearOne(person);
        row.Children.Add(remove);
        row.Children.Add(Chip(Ui.PersonLine(person), Palette.Brush("CanvasBrush")));
        return row;
    }

    private StackPanel UnassignedBlock(DayBoard board, string query)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(L.T("UnassignedPlural")));
        if (CalendarDates.IsWeekend(board.Date))
        {
            stack.Children.Add(Note(L.T("WeekendNoUnassigned")));
            return stack;
        }

        if (board.ActiveEmployeeCount == 0)
        {
            stack.Children.Add(Note(L.T("NoEmployeesShort")));
            return stack;
        }

        var people = board.Unassigned.Where(person => Matches(person, query)).ToList();
        if (board.Unassigned.Count == 0)
            stack.Children.Add(Note(L.T("AllAssigned")));
        else if (people.Count == 0)
            stack.Children.Add(Note(L.T("NobodyFound")));
        else
            foreach (var person in people)
            {
                var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
                var assign = new Button { Content = L.T("Assign"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 4, 8, 4), MinHeight = 28 };
                assign.Click += (_, _) => AssignAtSite(null, [person.Id]);
                DockPanel.SetDock(assign, Dock.Right);
                row.Children.Add(assign);
                row.Children.Add(Chip(Ui.PersonLine(person), Palette.Brush("CanvasBrush")));
                stack.Children.Add(row);
            }
        return stack;
    }

    private StackPanel AbsenceBlock(DayBoard board, string query)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(L.T("Absences")));
        var items = board.Absences.Where(item => Matches(item.Employee, query)).ToList();
        if (items.Count == 0)
            stack.Children.Add(Note(query.Length == 0 ? L.T("None") : L.T("NobodyFound")));
        else
            foreach (var item in items)
            {
                stack.Children.Add(Chip(
                    item.Employee.FullName + " — " + item.Text,
                    AbsenceFill(item.Absence.Kind)));
            }

        return stack;
    }

    private void BuildEmployeeGrid(DateOnly from, DateOnly to)
    {
        var rows = _db.GetEmployeeCalendar(from, to, SearchBox.Text, SelectedSiteId());
        var dates = CalendarDates.Expand(from, to, weekdaysOnly: false);
        EmployeeGrid.Columns.Clear();
        _columnDates.Clear();
        var nameColumn = new DataGridTextColumn
        {
            Header = L.T("Employee"),
            Binding = new Binding(nameof(EmployeeRowVm.Caption)),
            Width = new DataGridLength(220),
            ElementStyle = WrapStyle()
        };
        EmployeeGrid.Columns.Add(nameColumn);

        var view = new List<EmployeeRowVm>();
        foreach (var row in rows)
        {
            var caption = string.IsNullOrWhiteSpace(row.Employee.Specialty)
                ? row.Employee.FullName
                : row.Employee.FullName + "\n" + row.Employee.Specialty;
            if (row.Employee.IsArchived)
                caption += "\n" + L.T("Archive");
            var vm = new EmployeeRowVm { Id = row.Employee.Id, Caption = caption };
            foreach (var cell in row.Days)
                vm.Cells[CellKey(cell.Date)] = CellVm.From(cell);
            view.Add(vm);
        }

        foreach (var date in dates)
        {
            var key = CellKey(date);
            var header = TextCatalog.FormatDayMonth(date) + "\n" + TextCatalog.FormatWeekday(date);
            if (date == Ui.Today)
                header += "\n" + L.T("Today");
            var column = new DataGridTemplateColumn
            {
                Header = header,
                Width = new DataGridLength(112),
                CellTemplate = CellTemplate(key),
                HeaderStyle = HeaderStyle(date)
            };
            _columnDates[column] = date;
            EmployeeGrid.Columns.Add(column);
        }

        EmployeeGrid.ItemsSource = view;
        if (view.Count == 0 && _db.SearchEmployees(null, true).Count > 0)
            SetStatus(L.T("NobodyFound"));
    }

    private void ClearOne(Employee person)
    {
        if (!Ui.Confirm(this, L.F("ClearOneConfirm", person.FullName, TextCatalog.FormatDate(_cursor)), L.T("ClearDays")))
            return;
        try
        {
            _db.ClearAssignments(person.Id, [_cursor]);
            Refresh();
            SetStatus(L.T("DayCleared"));
        }
        catch (Exception ex)
        {
            Ui.Error(this, ex);
        }
    }

    private void Assign_Click(object sender, RoutedEventArgs e) =>
        OpenAssignment("Assign", "AssignHint");

    private void Move_Click(object sender, RoutedEventArgs e) =>
        OpenAssignment("MoveTitle", "MoveHint");

    private void OpenAssignment(string titleKey, string hintKey)
    {
        var dates = ByEmployeeRadio.IsChecked == true ? SelectedDayKeys().Select(key => key.Date).Distinct().ToArray() : [];
        var dialog = new AssignmentWindow(_db, titleKey, hintKey, null,
            ByEmployeeRadio.IsChecked == true ? SelectedEmployeeIds() : null, _cursor, dates) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            AfterDirectory();
            SetStatus(L.T("Saved"));
        }
        else
            AfterDirectory();
    }

    private void AssignAtSite(long? siteId, IReadOnlyCollection<long>? selected = null)
    {
        var dialog = new AssignmentWindow(_db, "AssignToSite", "AssignSiteHint", siteId, selected, _cursor) { Owner = this };
        var saved = dialog.ShowDialog() == true;
        AfterDirectory();
        if (saved)
            SetStatus(L.T("Saved"));
    }

    private void Planning_Click(object sender, RoutedEventArgs e) => Refresh();

    private void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        _suppress = true;
        SearchBox.Clear();
        SiteFilter.SelectedIndex = 0;
        _suppress = false;
        Refresh();
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
            return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedDayKeys();
        if (selected.Count > 0)
        {
            if (!Ui.Confirm(this, L.T("ClearCellsConfirm"), L.T("ClearDays")))
                return;
            try
            {
                _db.ClearAssignments(selected);
                Refresh();
                SetStatus(L.T("DaysCleared"));
            }
            catch (Exception ex)
            {
                Ui.Error(this, ex);
            }

            return;
        }

        var dialog = new ClearWindow(_db, SelectedEmployeeIds()) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            Refresh();
            SetStatus(L.T("DaysCleared"));
        }
    }

    private void CopyDay_Click(object sender, RoutedEventArgs e) => Copy(false);

    private void CopyWeek_Click(object sender, RoutedEventArgs e) => Copy(true);

    private void Copy(bool week)
    {
        var dialog = new CopyWindow(_db, week, _cursor) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            Refresh();
            SetStatus(L.T("CopySaved"));
        }
    }

    private void Absence_Click(object sender, RoutedEventArgs e)
    {
        var ids = ByEmployeeRadio.IsChecked == true ? SelectedEmployeeIds() : [];
        var target = CalendarTarget();
        var dialog = new AbsenceWindow(_db, target?.EmployeeId ?? (ids.Count == 1 ? ids[0] : null), target?.Date ?? _cursor) { Owner = this };
        dialog.ShowDialog();
        Refresh();
    }

    private void MonthlyHours_Click(object sender, RoutedEventArgs e)
    {
        new MonthlyHoursWindow(_db, _cursor) { Owner = this }.ShowDialog();
        Refresh();
    }

    private DayKey? CalendarTarget() => ByEmployeeRadio.IsChecked == true
        && EmployeeGrid.CurrentCell.Item is EmployeeRowVm row
        && EmployeeGrid.CurrentCell.Column is DataGridColumn column && _columnDates.TryGetValue(column, out var date)
            ? new DayKey(row.Id, date) : null;

    private void Vacation_Click(object sender, RoutedEventArgs e) => OpenCalendarEntry(AbsenceKind.Vacation);
    private void Sick_Click(object sender, RoutedEventArgs e) => OpenCalendarEntry(AbsenceKind.Sick);
    private void Unpaid_Click(object sender, RoutedEventArgs e) => OpenCalendarEntry(AbsenceKind.Unpaid);
    private void OtherAbsence_Click(object sender, RoutedEventArgs e) => OpenCalendarEntry(AbsenceKind.Other);
    private void EditWorkDay_Click(object sender, RoutedEventArgs e) => OpenCalendarEntry(forceWork: true);

    private void OpenCalendarEntry(AbsenceKind? kind = null, bool forceWork = false)
    {
        var target = CalendarTarget();
        var dialog = new CalendarEntryWindow(_db, target?.EmployeeId, target?.Date ?? _cursor, kind, forceWork) { Owner = this };
        var saved = dialog.ShowDialog() == true;
        AfterDirectory();
        if (saved) SetStatus(L.T("Saved"));
    }

    private static DataGridCell? CalendarCell(object source)
    {
        var current = source as DependencyObject;
        while (current is not null && current is not DataGridCell)
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        return current as DataGridCell;
    }

    private bool SelectCalendarCell(object source)
    {
        var cell = CalendarCell(source);
        if (cell?.DataContext is not EmployeeRowVm || !_columnDates.ContainsKey(cell.Column)) return false;
        EmployeeGrid.SelectedCells.Clear();
        EmployeeGrid.CurrentCell = new DataGridCellInfo(cell);
        if (!EmployeeGrid.SelectedCells.Contains(EmployeeGrid.CurrentCell))
            EmployeeGrid.SelectedCells.Add(EmployeeGrid.CurrentCell);
        return true;
    }

    private void Calendar_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !SelectCalendarCell(e.OriginalSource)) return;
        e.Handled = true;
        OpenCalendarEntry();
    }

    private void Calendar_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (!SelectCalendarCell(e.OriginalSource)) e.Handled = true;
    }

    private void CalendarMenu_Opening(object sender, ContextMenuEventArgs e)
    {
        if (CalendarTarget() is null) e.Handled = true;
    }

    private void PrintDay_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PrintDocuments.Print(this, PrintDocuments.Day(_db.GetDayBoard(_cursor)), L.T("PrintJobDay"));
        }
        catch (Exception ex)
        {
            Ui.Error(this, ex);
        }
    }

    private void PrintWeek_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PrintDocuments.Print(this, PrintDocuments.Week(_db, _cursor), L.T("PrintJobWeek"));
        }
        catch (Exception ex)
        {
            Ui.Error(this, ex);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var (from, to) = VisibleRange();
        if (!Prompts.TryDateRange(this, "ExportCsv", from, to, out var exportFrom, out var exportTo))
            return;
        var save = new SaveFileDialog
        {
            Filter = L.T("CsvFilter"),
            FileName = $"{L.T("CsvFilePrefix")}-{exportFrom:yyyy-MM-dd}_{exportTo:yyyy-MM-dd}.csv"
        };
        if (save.ShowDialog(this) != true)
            return;
        try
        {
            var csv = _db.ExportCsv(exportFrom, exportTo);
            File.WriteAllText(save.FileName, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            SetStatus(L.F("CsvSaved", save.FileName));
        }
        catch (Exception ex)
        {
            Ui.Error(this, ex);
        }
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        var save = new SaveFileDialog
        {
            Filter = L.T("SqliteFilter"),
            FileName = $"{L.T("BackupFilePrefix")}-{DateTime.Now:yyyy-MM-dd-HHmm}.sqlite"
        };
        if (save.ShowDialog(this) != true)
            return;
        try
        {
            _db.CreateManualBackup(save.FileName);
            SetStatus(L.F("BackupSaved", save.FileName));
        }
        catch (Exception ex)
        {
            Ui.Error(this, ex);
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var open = new OpenFileDialog { Filter = L.T("SqliteOrAll") };
        if (open.ShowDialog(this) != true)
            return;
        var inspection = TimeManagementDatabase.Inspect(open.FileName);
        if (!inspection.IsCompatible)
        {
            MessageBox.Show(this, inspection.Message, L.T("RestoreTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Ui.Confirm(this, L.T("RestoreConfirm"), L.T("RestoreTitle")))
            return;
        try
        {
            _db.RestoreFrom(open.FileName);
            ReloadSiteFilter();
            Refresh();
            SetStatus(L.T("Restored"));
        }
        catch (Exception ex)
        {
            Ui.Error(this, ex);
            Refresh();
        }
    }

    private void AddEmployee_Click(object sender, RoutedEventArgs e)
    {
        if (EmployeesWindow.AddEmployee(this, _db))
            AfterDirectory();
    }

    private void AddSite_Click(object sender, RoutedEventArgs e)
    {
        if (SitesWindow.AddSite(this, _db, _cursor))
            AfterDirectory();
    }

    private void Demo_Click(object sender, RoutedEventArgs e)
    {
        if (_db.SearchEmployees(null, true).Count > 0 || _db.SearchSites(null, true).Count > 0)
        {
            Ui.Info(this, L.T("DemoOnlyEmpty"));
            return;
        }

        try
        {
            var anna = _db.CreateEmployee("Meier", "Anna", "Maurer", null, null);
            var lukas = _db.CreateEmployee("Schmidt", "Lukas", "Zimmermann", null, null);
            var maria = _db.CreateEmployee("Ionescu", "Maria", "Maler", null, null);
            var north = _db.CreateSite("Nord", "Hafenstraße 4", null, null, null);
            var south = _db.CreateSite("Süd", "Waldweg 12", null, null, null);
            var monday = CalendarDates.MondayOf(Ui.Today);
            var ops = new List<AssignmentOp>();
            foreach (var person in new[] { anna, lukas })
            {
                for (var day = 0; day < 5; day++)
                    ops.Add(new AssignmentOp(person.Id, north.Id, monday.AddDays(day)));
            }

            for (var day = 0; day < 5; day++)
                ops.Add(new AssignmentOp(maria.Id, south.Id, monday.AddDays(day)));
            _db.ApplyAssignments(ops, false);
            AfterDirectory();
            SetStatus(L.T("DemoAdded"));
        }
        catch (Exception ex)
        {
            Ui.Error(this, ex);
            AfterDirectory();
        }
    }

    private void Employees_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new EmployeesWindow(_db) { Owner = this };
        dialog.ShowDialog();
        AfterDirectory();
    }

    private void Sites_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SitesWindow(_db, _cursor) { Owner = this };
        dialog.ShowDialog();
        AfterDirectory();
    }

    private void AfterDirectory()
    {
        ReloadSiteFilter();
        Refresh();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && EmployeeGrid.IsKeyboardFocusWithin && ByEmployeeRadio.IsChecked == true)
        {
            Clear_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void ReloadSiteFilter()
    {
        _suppress = true;
        var selected = (SiteFilter.SelectedItem as SiteChoice)?.Id;
        var items = new List<SiteChoice> { new(null, L.T("AllSites")) };
        items.AddRange(_db.SearchSites(null, true).Select(site =>
            new SiteChoice(site.Id, site.IsArchived ? L.F("ArchivedName", site.Name) : site.Name)));
        SiteFilter.ItemsSource = items;
        SiteFilter.SelectedItem = items.FirstOrDefault(item => item.Id == selected) ?? items[0];
        _suppress = false;
    }

    private long? SelectedSiteId() => (SiteFilter.SelectedItem as SiteChoice)?.Id;

    private List<long> SelectedEmployeeIds()
    {
        var ids = new HashSet<long>();
        foreach (var cell in EmployeeGrid.SelectedCells)
        {
            if (cell.Item is EmployeeRowVm row)
                ids.Add(row.Id);
        }

        return ids.ToList();
    }

    private List<DayKey> SelectedDayKeys()
    {
        var keys = new List<DayKey>();
        foreach (var cell in EmployeeGrid.SelectedCells)
        {
            if (cell.Item is EmployeeRowVm row && _columnDates.TryGetValue(cell.Column, out var date))
                keys.Add(new DayKey(row.Id, date));
        }

        return keys.Distinct().ToList();
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private static bool Matches(Employee employee, string query) =>
        query.Length == 0
        || employee.LastName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || employee.FirstName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (employee.Specialty?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    private static string CellKey(DateOnly date) => "d" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static Border Card(UIElement child) => new()
    {
        Background = Palette.Brush("PaperBrush"),
        BorderBrush = Palette.Brush("LineBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(20),
        Margin = new Thickness(0, 0, 0, 12),
        Child = child
    };

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 4)
    };

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        Foreground = Palette.Brush("MutedBrush"),
        Margin = new Thickness(0, 6, 0, 0),
        TextWrapping = TextWrapping.Wrap
    };

    private static Border Chip(string text, Brush fill) => new()
    {
        Background = fill,
        BorderBrush = Palette.Brush("LineBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(8, 4, 8, 4),
        Margin = new Thickness(0, 6, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.Brush("InkBrush")
        }
    };

    private static Brush AbsenceFill(AbsenceKind kind) => kind switch
    {
        AbsenceKind.Sick => Palette.Brush("SickBrush"),
        AbsenceKind.Vacation => Palette.Brush("VacationBrush"),
        AbsenceKind.Unpaid => Palette.Brush("UnpaidBrush"),
        _ => Palette.Brush("OtherBrush")
    };

    private static Style WrapStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(4, 2, 4, 2)));
        style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        return style;
    }

    private static Style HeaderStyle(DateOnly date)
    {
        var basis = (Style)Application.Current.FindResource(typeof(DataGridColumnHeader));
        var style = new Style(typeof(DataGridColumnHeader), basis);
        var weekend = CalendarDates.IsWeekend(date);
        var today = date == Ui.Today;
        style.Setters.Add(new Setter(Control.BackgroundProperty, weekend ? Palette.Brush("WeekendHeaderBrush") : Palette.Brush("HeaderBrush")));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        if (today)
        {
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Palette.Brush("AccentBrush")));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        }

        return style;
    }

    private static DataTemplate CellTemplate(string key)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new Binding(".") { Converter = CellBrushConverter.Fill, ConverterParameter = key });
        border.SetBinding(Border.BorderBrushProperty, new Binding(".") { Converter = CellBrushConverter.Mark, ConverterParameter = key });
        border.SetBinding(Border.BorderThicknessProperty, new Binding(".") { Converter = CellMetricConverter.Thickness, ConverterParameter = key });
        border.SetValue(Border.PaddingProperty, new Thickness(4, 2, 4, 2));
        border.SetBinding(FrameworkElement.ToolTipProperty, new Binding(".") { Converter = CellTextConverter.Tip, ConverterParameter = key });
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        text.SetValue(TextBlock.FontSizeProperty, 12.0);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetBinding(TextBlock.TextProperty, new Binding(".") { Converter = CellTextConverter.Text, ConverterParameter = key });
        text.SetBinding(TextBlock.ForegroundProperty, new Binding(".") { Converter = CellBrushConverter.Foreground, ConverterParameter = key });
        border.AppendChild(text);
        return new DataTemplate { VisualTree = border };
    }

    private sealed record LangChoice(string Code, string Title)
    {
        public override string ToString() => Title;
    }

    private sealed record SiteChoice(long? Id, string Title)
    {
        public override string ToString() => Title;
    }

    private sealed class EmployeeRowVm
    {
        public long Id { get; init; }

        public string Caption { get; init; } = "";

        public Dictionary<string, CellVm> Cells { get; } = new();
    }

    private sealed class CellVm
    {
        public string Text { get; init; } = "";

        public string Tip { get; init; } = "";

        public Brush Fill { get; init; } = Brushes.Transparent;

        public Brush Foreground { get; init; } = Brushes.Black;

        public Brush Mark { get; init; } = Brushes.Transparent;

        public Thickness MarkThickness { get; init; }

        public static CellVm From(DayCell cell)
        {
            var weekend = CalendarDates.IsWeekend(cell.Date);
            var today = cell.Date == Ui.Today;
            var mark = today ? Palette.Brush("AccentBrush") : Brushes.Transparent;
            var thickness = today ? new Thickness(2) : new Thickness(0);
            var ink = Palette.Brush("InkBrush");
            var muted = Palette.Brush("MutedBrush");
            var (text, fill, foreground) = cell.Kind switch
            {
                CellKind.Assignment => (cell.Text + "\n" + (cell.Time is null ? L.T("TimeNotSet") : cell.Time.HoursText + " " + L.T("HoursUnit")), Palette.Brush("AssignBrush"), ink),
                CellKind.Sick => (cell.Text, Palette.Brush("SickBrush"), ink),
                CellKind.Vacation => (cell.Text, Palette.Brush("VacationBrush"), ink),
                CellKind.Unpaid => (cell.Text, Palette.Brush("UnpaidBrush"), ink),
                CellKind.Other => (cell.Text, Palette.Brush("OtherBrush"), ink),
                _ when weekend => (L.T("Weekend"), Palette.Brush("WeekendBrush"), muted),
                _ => (L.T("Unassigned"), Palette.Brush("PaperBrush"), muted)
            };
            return new CellVm
            {
                Text = text,
                Tip = cell.Time is WorkTime time ? $"{cell.Text}\n{time.RangeText}\n{L.T("Lunch")}: {time.LunchText}\n{time.HoursText} {L.T("HoursUnit")}" : text,
                Fill = fill,
                Foreground = foreground,
                Mark = mark,
                MarkThickness = thickness
            };
        }
    }

    private sealed class CellTextConverter : IValueConverter
    {
        public static readonly CellTextConverter Text = new(false);
        public static readonly CellTextConverter Tip = new(true);
        private readonly bool _tip;

        private CellTextConverter(bool tip) => _tip = tip;

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is EmployeeRowVm row && parameter is string key && row.Cells.TryGetValue(key, out var cell))
                return _tip ? cell.Tip : cell.Text;
            return "";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class CellBrushConverter : IValueConverter
    {
        public static readonly CellBrushConverter Fill = new(0);
        public static readonly CellBrushConverter Foreground = new(1);
        public static readonly CellBrushConverter Mark = new(2);
        private readonly int _part;

        private CellBrushConverter(int part) => _part = part;

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is EmployeeRowVm row && parameter is string key && row.Cells.TryGetValue(key, out var cell))
            {
                return _part switch
                {
                    1 => cell.Foreground,
                    2 => cell.Mark,
                    _ => cell.Fill
                };
            }

            return Brushes.Transparent;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class CellMetricConverter : IValueConverter
    {
        public static readonly CellMetricConverter Thickness = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is EmployeeRowVm row && parameter is string key && row.Cells.TryGetValue(key, out var cell))
                return cell.MarkThickness;
            return new Thickness(0);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
