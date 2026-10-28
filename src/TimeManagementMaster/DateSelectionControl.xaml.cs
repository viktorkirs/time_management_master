using System.Windows;
using System.Windows.Controls;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public partial class DateSelectionControl : UserControl
{
    private readonly List<DateOnly> _dates = [];

    public DateSelectionControl()
    {
        InitializeComponent();
        var today = DateTime.Today;
        SingleDate.SelectedDate = today;
        FromDate.SelectedDate = today;
        ToDate.SelectedDate = today;
        ExtraDate.SelectedDate = today;
        Loaded += (_, _) => LanguageService.Instance.LanguageChanged += OnLanguage;
        Unloaded += (_, _) => LanguageService.Instance.LanguageChanged -= OnLanguage;
    }

    private void OnLanguage(object? sender, EventArgs e) => Dispatcher.Invoke(RefreshList);

    public bool TryGetDates(out IReadOnlyList<DateOnly> dates, out string? error)
    {
        dates = [];
        error = null;
        if (OneDayRadio.IsChecked == true)
        {
            if (SingleDate.SelectedDate is null)
            {
                error = L.T("PickDate");
                return false;
            }

            dates = [DateOnly.FromDateTime(SingleDate.SelectedDate.Value)];
            return true;
        }

        if (PeriodRadio.IsChecked == true)
        {
            if (FromDate.SelectedDate is null || ToDate.SelectedDate is null)
            {
                error = L.T("PickPeriodEnds");
                return false;
            }

            var start = DateOnly.FromDateTime(FromDate.SelectedDate.Value);
            var end = DateOnly.FromDateTime(ToDate.SelectedDate.Value);
            if (end < start)
            {
                error = L.T("EndBeforeStart");
                return false;
            }

            dates = CalendarDates.Expand(start, end, WeekdaysOnly.IsChecked == true);
            if (dates.Count == 0)
            {
                error = L.T("NoDaysInRange");
                return false;
            }

            return true;
        }

        if (_dates.Count == 0)
        {
            error = L.T("AddAtLeastOne");
            return false;
        }

        dates = _dates.OrderBy(date => date).ToList();
        return true;
    }

    public void SetDates(IReadOnlyCollection<DateOnly> dates)
    {
        var ordered = dates.Distinct().Order().ToList();
        if (ordered.Count == 0)
            return;
        SingleDate.SelectedDate = ordered[0].ToDateTime(TimeOnly.MinValue);
        FromDate.SelectedDate = ordered[0].ToDateTime(TimeOnly.MinValue);
        ToDate.SelectedDate = ordered[^1].ToDateTime(TimeOnly.MinValue);
        ExtraDate.SelectedDate = ordered[0].ToDateTime(TimeOnly.MinValue);
        _dates.Clear();
        _dates.AddRange(ordered);
        if (ordered.Count == 1)
            OneDayRadio.IsChecked = true;
        else
            ListRadio.IsChecked = true;
        RefreshList();
    }

    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        if (OneDayPanel is null)
            return;
        OneDayPanel.Visibility = OneDayRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PeriodPanel.Visibility = PeriodRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ListPanel.Visibility = ListRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddDate_Click(object sender, RoutedEventArgs e)
    {
        if (ExtraDate.SelectedDate is null)
            return;
        var date = DateOnly.FromDateTime(ExtraDate.SelectedDate.Value);
        if (_dates.Contains(date))
            return;
        _dates.Add(date);
        _dates.Sort();
        RefreshList();
    }

    private void RemoveDate_Click(object sender, RoutedEventArgs e)
    {
        var index = DatesList.SelectedIndex;
        if (index < 0 || index >= _dates.Count)
            return;
        _dates.RemoveAt(index);
        RefreshList();
    }

    private void RefreshList() =>
        DatesList.ItemsSource = _dates.Select(TextCatalog.FormatDate).ToList();
}
