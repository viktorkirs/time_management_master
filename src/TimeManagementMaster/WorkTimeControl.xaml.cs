using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public partial class WorkTimeControl : UserControl
{
    public WorkTimeControl()
    {
        InitializeComponent();
        Loaded += (_, _) => { LanguageService.Instance.LanguageChanged += OnLanguage; UpdateSummary(); };
        Unloaded += (_, _) => LanguageService.Instance.LanguageChanged -= OnLanguage;
        UpdateSummary();
    }

    private void OnLanguage(object? sender, EventArgs e) => Dispatcher.Invoke(UpdateSummary);
    private void Changed(object sender, RoutedEventArgs e) => UpdateSummary();

    public void SetTime(WorkTime? time)
    {
        TrackBox.IsChecked = time is not null;
        if (time is not null)
        {
            StartBox.Text = WorkTime.Clock(time.Start);
            EndBox.Text = WorkTime.Clock(time.End);
            NextDayBox.IsChecked = time.EndsNextDay;
            LunchBox.IsChecked = time.LunchStart.HasValue;
            LunchStartBox.Text = time.LunchStart is TimeOnly start ? WorkTime.Clock(start) : "12:00";
            LunchEndBox.Text = time.LunchEnd is TimeOnly end ? WorkTime.Clock(end) : "13:00";
        }
        UpdateSummary();
    }

    public bool TryGetTime(out WorkTime? time, out string? error)
    {
        time = null;
        error = null;
        if (TrackBox.IsChecked != true) return true;
        if (!Parse(StartBox.Text, out var start) || !Parse(EndBox.Text, out var end))
        {
            error = L.T("TimeFormatHint"); return false;
        }
        TimeOnly? lunchStart = null, lunchEnd = null;
        if (LunchBox.IsChecked == true)
        {
            if (!Parse(LunchStartBox.Text, out var from) || !Parse(LunchEndBox.Text, out var to))
            {
                error = L.T("LunchBothEnds"); return false;
            }
            lunchStart = from; lunchEnd = to;
        }
        time = new WorkTime(start, end, lunchStart, lunchEnd, NextDayBox.IsChecked == true);
        error = time.ValidationError();
        return error is null;
    }

    private static bool Parse(string value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private void UpdateSummary()
    {
        if (Summary is null || LunchEndBox is null) return;
        Fields.Visibility = TrackBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        LunchFields.Visibility = LunchBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        var valid = TryGetTime(out var time, out var error);
        Summary.Text = !valid ? error : time is null ? L.T("TimeNotSet") : L.F("NetTimeSummary", time.HoursText, time.LunchMinutes);
        Summary.Foreground = Palette.Brush(valid ? "MutedBrush" : "DangerBrush");
    }
}
