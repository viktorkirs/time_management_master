using System.Windows;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public partial class CopyWindow : Window
{
    private readonly TimeManagementDatabase _db;
    private readonly bool _week;

    public CopyWindow(TimeManagementDatabase db, bool week, DateOnly source)
    {
        InitializeComponent();
        _db = db;
        _week = week;
        LanguageService.Instance.LanguageChanged += OnLanguage;
        Closed += (_, _) => LanguageService.Instance.LanguageChanged -= OnLanguage;
        ApplyLanguage();
        var today = source.ToDateTime(TimeOnly.MinValue);
        SourceDate.SelectedDate = today;
        TargetFrom.SelectedDate = today.AddDays(week ? 7 : 1);
        TargetTo.SelectedDate = today.AddDays(week ? 13 : 1);
    }

    private void OnLanguage(object? sender, EventArgs e) => Dispatcher.Invoke(ApplyLanguage);

    private void ApplyLanguage()
    {
        Title = _week ? L.T("CopyWeekTitle") : L.T("CopyDayTitle");
        SourceLabel.Text = _week ? L.T("CopyWeekSource") : L.T("CopyDaySource");
        HintText.Text = _week ? L.T("CopyWeekHint") : L.T("CopyDayHint");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SourceDate.SelectedDate is null || TargetFrom.SelectedDate is null || TargetTo.SelectedDate is null)
        {
            Ui.Info(this, L.T("PickDates"));
            return;
        }

        var source = DateOnly.FromDateTime(SourceDate.SelectedDate.Value);
        var from = DateOnly.FromDateTime(TargetFrom.SelectedDate.Value);
        var to = DateOnly.FromDateTime(TargetTo.SelectedDate.Value);
        var batch = _week
            ? _db.BuildWeekCopy(source, from, to, WeekdaysOnly.IsChecked == true)
            : _db.BuildDayCopy(source, from, to, WeekdaysOnly.IsChecked == true);
        if (batch.Operations.Count == 0)
        {
            Ui.Info(this, batch.EmptyNotice ?? L.T("SourceEmpty"));
            return;
        }

        if (!SaveFlow.TryApplyAssignments(this, _db, batch.Operations))
            return;
        DialogResult = true;
    }
}
