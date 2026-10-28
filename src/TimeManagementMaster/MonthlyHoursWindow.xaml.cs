using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public partial class MonthlyHoursWindow : Window
{
    private readonly TimeManagementDatabase _db;
    private DateOnly _month;
    private bool _loading;

    public MonthlyHoursWindow(TimeManagementDatabase db, DateOnly date)
    {
        InitializeComponent();
        _db = db;
        _month = new DateOnly(date.Year, date.Month, 1);
        Reload();
    }
    private void Previous_Click(object sender, RoutedEventArgs e) { _month = _month.AddMonths(-1); Reload(); }
    private void Next_Click(object sender, RoutedEventArgs e) { _month = _month.AddMonths(1); Reload(); }
    private void MonthChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _db is null || MonthPicker.SelectedDate is not DateTime date) return;
        _month = new DateOnly(date.Year, date.Month, 1); Reload();
    }
    private void Reload()
    {
        _loading = true;
        MonthPicker.SelectedDate = _month.ToDateTime(TimeOnly.MinValue);
        _loading = false;
        MonthText.Text = TextCatalog.FormatMonth(_month);
        var selectedId = (SummaryGrid.SelectedItem as MonthlyHoursRow)?.Employee.Id;
        var rows = _db.GetMonthlyHours(_month.Year, _month.Month);
        SummaryGrid.ItemsSource = rows;
        SummaryGrid.SelectedItem = rows.FirstOrDefault(row => row.Employee.Id == selectedId) ?? rows.FirstOrDefault();
        TotalText.Text = L.F("ReportTotal", WorkTime.FormatMinutes(rows.Sum(row => row.NetMinutes)), rows.Sum(row => row.MissingTimeDays));
        LoadDetails();
    }
    private void EmployeeChanged(object sender, SelectionChangedEventArgs e) { if (_db is not null) LoadDetails(); }
    private void LoadDetails() => DetailsGrid.ItemsSource = SummaryGrid.SelectedItem is MonthlyHoursRow row
        ? _db.GetWorkDayDetails(row.Employee.Id, _month.Year, _month.Month) : null;
    private void Export_Click(object sender, RoutedEventArgs e) => Export(false);
    private void ExportDetails_Click(object sender, RoutedEventArgs e) => Export(true);
    private void Export(bool details)
    {
        var dialog = new SaveFileDialog { Filter = L.T("CsvFilter"), FileName = $"{(details ? "hours-days" : "hours")}-{_month:yyyy-MM}.csv" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, details ? _db.ExportWorkDayDetailsCsv(_month.Year, _month.Month)
                : _db.ExportMonthlyHoursCsv(_month.Year, _month.Month), new UTF8Encoding(false));
        }
        catch (Exception ex) { Ui.Error(this, ex); }
    }
    private void Print_Click(object sender, RoutedEventArgs e)
    {
        try { PrintDocuments.Print(this, PrintDocuments.MonthHours(_db, _month), L.T("HoursReport")); }
        catch (Exception ex) { Ui.Error(this, ex); }
    }
}
