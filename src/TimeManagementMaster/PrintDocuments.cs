using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

internal static class PrintDocuments
{
    public static void Print(Window owner, FlowDocument document, string jobName)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
            return;
        document.PageHeight = dialog.PrintableAreaHeight;
        document.PageWidth = dialog.PrintableAreaWidth;
        document.PagePadding = new Thickness(36);
        document.ColumnWidth = dialog.PrintableAreaWidth;
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, jobName);
    }

    public static FlowDocument Day(DayBoard board)
    {
        var doc = NewDocument();
        doc.Blocks.Add(Heading(L.F("DaySheet", TextCatalog.FormatLong(board.Date)), 20));
        doc.Blocks.Add(Body(L.F("AssignedCount", board.AssignedHeadcount)));
        foreach (var group in board.Sites)
        {
            var name = group.Site.IsArchived ? L.F("ArchivedName", group.Site.Name) : group.Site.Name;
            doc.Blocks.Add(Heading($"{name} — {group.Count}", 16));
            if (!string.IsNullOrWhiteSpace(group.Site.Address))
                doc.Blocks.Add(Body(L.F("AddressLine", group.Site.Address)));
            if (group.Employees.Count == 0)
                doc.Blocks.Add(Body(L.T("NoAssignments")));
            else
                foreach (var person in group.Employees)
                    doc.Blocks.Add(Body("• " + Ui.PersonLine(person)));
        }

        doc.Blocks.Add(Heading(L.T("UnassignedPlural"), 16));
        if (CalendarDates.IsWeekend(board.Date))
            doc.Blocks.Add(Body(L.T("WeekendNoUnassigned")));
        else if (board.ActiveEmployeeCount == 0)
            doc.Blocks.Add(Body(L.T("NoEmployeesShort")));
        else if (board.Unassigned.Count == 0)
            doc.Blocks.Add(Body(L.T("AllAssigned")));
        else
            foreach (var person in board.Unassigned)
                doc.Blocks.Add(Body("• " + Ui.PersonLine(person)));

        doc.Blocks.Add(Heading(L.T("Absences"), 16));
        if (board.Absences.Count == 0)
            doc.Blocks.Add(Body(L.T("None")));
        else
            foreach (var item in board.Absences)
                doc.Blocks.Add(Body($"• {item.Employee.FullName} — {item.Text}"));
        return doc;
    }

    public static FlowDocument Week(TimeManagementDatabase db, DateOnly anyDay)
    {
        var monday = CalendarDates.MondayOf(anyDay);
        var days = CalendarDates.Expand(monday, monday.AddDays(6), weekdaysOnly: false);
        var rows = db.GetEmployeeCalendar(monday, monday.AddDays(6), null, null);
        var doc = NewDocument();
        doc.Blocks.Add(Heading(
            L.F("WeekSheet", TextCatalog.FormatDate(monday), TextCatalog.FormatDate(monday.AddDays(6))),
            20));

        var table = new Table { CellSpacing = 0, FontSize = 11 };
        table.Columns.Add(new TableColumn { Width = new GridLength(140) });
        foreach (var _ in days)
            table.Columns.Add(new TableColumn { Width = new GridLength(78) });
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        var header = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0xE9, 0xEC, 0xEF)) };
        header.Cells.Add(Cell(L.T("Employee"), true));
        foreach (var date in days)
        {
            var label = TextCatalog.FormatDayMonth(date) + "\n" + TextCatalog.FormatWeekday(date);
            if (date == Ui.Today)
                label += "\n" + L.T("Today");
            header.Cells.Add(Cell(label, true));
        }

        group.Rows.Add(header);
        foreach (var row in rows)
        {
            var line = new TableRow();
            line.Cells.Add(Cell(row.Employee.FullName, false));
            foreach (var cell in row.Days)
                line.Cells.Add(Cell(PrintText(cell), false));
            group.Rows.Add(line);
        }

        doc.Blocks.Add(table);
        foreach (var date in days)
        {
            doc.Blocks.Add(Heading(date.ToString("dddd, dd MMMM", TextCatalog.Culture), 15));
            var board = db.GetDayBoard(date);
            foreach (var site in board.Sites)
            {
                var names = site.Employees.Count == 0
                    ? L.T("NoAssignmentsShort")
                    : string.Join(", ", site.Employees.Select(person => person.FullName));
                doc.Blocks.Add(Body($"{site.Site.Name} — {site.Count}: {names}"));
            }

            if (!CalendarDates.IsWeekend(date))
            {
                var free = board.Unassigned.Count == 0 && board.ActiveEmployeeCount > 0
                    ? L.T("AllAssigned")
                    : string.Join(", ", board.Unassigned.Select(person => person.FullName));
                doc.Blocks.Add(Body(L.F("UnassignedLine", string.IsNullOrEmpty(free) ? L.T("None") : free)));
            }

            var absent = board.Absences.Count == 0
                ? L.T("None")
                : string.Join("; ", board.Absences.Select(item => item.Employee.FullName + " — " + item.Text));
            doc.Blocks.Add(Body(L.F("AbsencesLine", absent)));
        }

        return doc;
    }

    public static FlowDocument MonthHours(TimeManagementDatabase db, DateOnly month)
    {
        var doc = NewDocument();
        doc.Blocks.Add(Heading(L.T("HoursReport") + " · " + TextCatalog.FormatMonth(month), 20));
        doc.Blocks.Add(Body(L.T("HoursReportHint")));
        var table = new Table { CellSpacing = 0, FontSize = 10 };
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        var header = new TableRow();
        foreach (var key in new[] { "Employee", "AssignedDays", "MissingTimeDays", "TotalHours", "Vacation", "Sick" }) header.Cells.Add(Cell(L.T(key), true));
        group.Rows.Add(header);
        var rows = db.GetMonthlyHours(month.Year, month.Month);
        foreach (var row in rows)
        {
            var line = new TableRow();
            foreach (var value in new[] { row.EmployeeName, row.AssignmentDays.ToString(), row.MissingTimeDays.ToString(), row.HoursText, row.VacationDays.ToString(), row.SickDays.ToString() }) line.Cells.Add(Cell(value, false));
            group.Rows.Add(line);
        }
        doc.Blocks.Add(table);
        doc.Blocks.Add(Body(L.F("ReportTotal", WorkTime.FormatMinutes(rows.Sum(row => row.NetMinutes)), rows.Sum(row => row.MissingTimeDays))));
        doc.Blocks.Add(Body(L.T("AbsenceDaysHint")));
        return doc;
    }

    private static string PrintText(DayCell cell)
    {
        if (cell.Kind != CellKind.Empty)
            return cell.Text + (cell.Time is WorkTime time ? "\n" + time.RangeText + "\n" + time.HoursText + " " + L.T("HoursUnit") : "");
        return CalendarDates.IsWeekend(cell.Date) ? L.T("Weekend") : L.T("Unassigned");
    }

    private static FlowDocument NewDocument() => new()
    {
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 12,
        PagePadding = new Thickness(36)
    };

    private static Paragraph Heading(string text, double size) => new(new Run(text))
    {
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 10, 0, 4)
    };

    private static Paragraph Body(string text) => new(new Run(text))
    {
        Margin = new Thickness(0, 1, 0, 1)
    };

    private static TableCell Cell(string text, bool header)
    {
        var paragraph = new Paragraph(new Run(text)) { Margin = new Thickness(1) };
        if (header)
            paragraph.FontWeight = FontWeights.SemiBold;
        return new TableCell(paragraph)
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(0.4),
            Padding = new Thickness(2)
        };
    }
}
