using System.Windows;
using System.Windows.Controls;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

internal static class Ui
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public static void Error(Window owner, Exception ex) =>
        MessageBox.Show(owner, ex.Message, L.T("SaveFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Error);

    public static void Info(Window owner, string message, string? title = null) =>
        MessageBox.Show(owner, message, title ?? L.T("MessageTitle"), MessageBoxButton.OK, MessageBoxImage.Information);

    public static bool Confirm(Window owner, string message, string title)
        => MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static string PersonLine(Employee employee)
    {
        if (string.IsNullOrWhiteSpace(employee.Specialty))
            return employee.FullName;
        return employee.FullName + " — " + employee.Specialty;
    }
}

internal static class SaveFlow
{
    public static bool TryApplyAssignments(Window owner, TimeManagementDatabase db, IReadOnlyList<AssignmentOp> operations)
    {
        ChangePlan plan;
        try
        {
            plan = db.PlanAssignments(operations);
        }
        catch (Exception ex)
        {
            Ui.Error(owner, ex);
            return false;
        }

        return Commit(
            owner,
            plan,
            () => db.PlanAssignments(operations).Conflicts,
            () => db.ApplyAssignments(operations, true),
            () => db.ApplyAssignments(operations, false));
    }

    public static bool TryApplyAbsence(Window owner, TimeManagementDatabase db, AbsenceDraft draft)
    {
        ChangePlan plan;
        try
        {
            plan = db.PlanAbsence(draft);
        }
        catch (Exception ex)
        {
            Ui.Error(owner, ex);
            return false;
        }

        return Commit(
            owner,
            plan,
            () => db.PlanAbsence(draft).Conflicts,
            () => db.ApplyAbsence(draft, true),
            () => db.ApplyAbsence(draft, false));
    }

    private static bool Commit(
        Window owner,
        ChangePlan plan,
        Func<IReadOnlyList<ConflictRow>> reloadConflicts,
        Action replaceAll,
        Action saveClean)
    {
        if (plan.IsBlocked)
        {
            MessageBox.Show(
                owner,
                string.Join(Environment.NewLine, plan.Blockers),
                L.T("CannotSave"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        try
        {
            if (plan.HasConflicts)
            {
                var dialog = new ConflictWindow(reloadConflicts) { Owner = owner };
                dialog.ShowDialog();
                if (dialog.Decision != ConflictDecision.ReplaceAll)
                    return false;
                replaceAll();
                return true;
            }

            if (plan.ChangeCount == 0)
            {
                Ui.Info(owner, L.T("NoChanges"), L.T("Saving"));
                return false;
            }

            saveClean();
            return true;
        }
        catch (Exception ex)
        {
            Ui.Error(owner, ex);
            return false;
        }
    }
}

public enum ConflictDecision
{
    Cancel,
    EditDates,
    ReplaceAll
}

internal sealed class TextForm : Window
{
    private readonly TextBox[] _boxes;
    private readonly Func<string[], string?> _save;
    private readonly TextBlock _error = new() { Foreground = Palette.Brush("DangerBrush"), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 16, 0, 0) };

    public TextForm(Window owner, string titleKey, (string LabelKey, string Value, int MaxLength)[] fields, Func<string[], string?> save)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        _save = save;
        Owner = owner;
        Width = 520;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SetBinding(TitleProperty, LanguageService.Instance.Binding(titleKey));

        var root = new StackPanel { Margin = new Thickness(24) };
        var heading = new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        heading.SetBinding(TextBlock.TextProperty, LanguageService.Instance.Binding(titleKey));
        root.Children.Add(heading);
        var hint = new TextBlock { Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 0, 0, 16) };
        hint.SetBinding(TextBlock.TextProperty, LanguageService.Instance.Binding("EmployeeFormHint"));
        root.Children.Add(hint);
        var details = new StackPanel();
        var extra = new Expander { Content = details, Margin = new Thickness(0, 16, 0, 0) };
        extra.SetBinding(HeaderedContentControl.HeaderProperty, LanguageService.Instance.Binding("SiteExtraDetails"));
        _boxes = new TextBox[fields.Length];
        for (var i = 0; i < fields.Length; i++)
        {
            var optional = fields[i].LabelKey is "Phone" or "Note";
            var target = optional ? details : root;
            var label = new TextBlock { Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 4) };
            label.SetBinding(TextBlock.TextProperty, LanguageService.Instance.Binding(fields[i].LabelKey));
            target.Children.Add(label);
            var box = new TextBox { Text = fields[i].Value };
            if (fields[i].MaxLength > 0)
                box.MaxLength = fields[i].MaxLength;
            _boxes[i] = box;
            target.Children.Add(box);
            if (optional && !string.IsNullOrEmpty(fields[i].Value)) extra.IsExpanded = true;
        }
        root.Children.Add(extra);
        root.Children.Add(_error);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button { IsCancel = true, MinWidth = 100 };
        cancel.SetBinding(Button.ContentProperty, LanguageService.Instance.Binding("Cancel"));
        var ok = new Button { MinWidth = 120, IsDefault = true, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        ok.SetBinding(Button.ContentProperty, LanguageService.Instance.Binding("Save"));
        ok.Click += Save_Click;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => _boxes[0].Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var error = _save(_boxes.Select(box => box.Text).ToArray());
        if (error is not null)
        {
            _error.Text = error;
            _error.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}

internal static class Prompts
{
    public static bool TryPeriod(
        Window owner,
        string titleKey,
        DateOnly start,
        DateOnly? end,
        bool allowOpen,
        bool openNow,
        out DateOnly newStart,
        out DateOnly? newEnd)
    {
        newStart = start;
        newEnd = end;
        var dialog = new Window
        {
            Owner = owner,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize
        };
        dialog.SetBinding(Window.TitleProperty, LanguageService.Instance.Binding(titleKey));
        var root = new StackPanel { Margin = new Thickness(16) };
        var startLabel = new TextBlock { Margin = new Thickness(0, 0, 0, 4) };
        startLabel.SetBinding(TextBlock.TextProperty, LanguageService.Instance.Binding("Start"));
        root.Children.Add(startLabel);
        var startPicker = new DatePicker { SelectedDate = start.ToDateTime(TimeOnly.MinValue) };
        root.Children.Add(startPicker);
        var openBox = new CheckBox
        {
            IsChecked = allowOpen && (openNow || end is null),
            Visibility = allowOpen ? Visibility.Visible : Visibility.Collapsed,
            Margin = new Thickness(0, 10, 0, 0)
        };
        openBox.SetBinding(ContentControl.ContentProperty, LanguageService.Instance.Binding("Ongoing"));
        root.Children.Add(openBox);
        var endLabel = new TextBlock { Margin = new Thickness(0, 10, 0, 4) };
        endLabel.SetBinding(TextBlock.TextProperty, LanguageService.Instance.Binding("End"));
        root.Children.Add(endLabel);
        var endPicker = new DatePicker { SelectedDate = (end ?? start).ToDateTime(TimeOnly.MinValue) };
        root.Children.Add(endPicker);
        openBox.Checked += (_, _) => endPicker.IsEnabled = false;
        openBox.Unchecked += (_, _) => endPicker.IsEnabled = true;
        endPicker.IsEnabled = openBox.IsChecked != true;

        DateOnly? resultStart = null;
        DateOnly? resultEnd = null;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button { IsCancel = true, MinWidth = 100 };
        cancel.SetBinding(Button.ContentProperty, LanguageService.Instance.Binding("Cancel"));
        var ok = new Button { MinWidth = 120, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        ok.SetBinding(Button.ContentProperty, LanguageService.Instance.Binding("Save"));
        ok.Click += (_, _) =>
        {
            if (startPicker.SelectedDate is null)
            {
                Ui.Info(dialog, L.T("PickStart"));
                return;
            }

            var chosenStart = DateOnly.FromDateTime(startPicker.SelectedDate.Value);
            DateOnly? chosenEnd = null;
            if (openBox.IsChecked != true)
            {
                if (endPicker.SelectedDate is null)
                {
                    Ui.Info(dialog, L.T("PickEnd"));
                    return;
                }

                chosenEnd = DateOnly.FromDateTime(endPicker.SelectedDate.Value);
                if (chosenEnd < chosenStart)
                {
                    Ui.Info(dialog, L.T("EndBeforeStart"));
                    return;
                }
            }

            resultStart = chosenStart;
            resultEnd = chosenEnd;
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        dialog.Content = root;
        if (dialog.ShowDialog() != true || resultStart is null)
            return false;
        newStart = resultStart.Value;
        newEnd = resultEnd;
        return true;
    }

    public static bool TryDateRange(Window owner, string titleKey, DateOnly from, DateOnly to, out DateOnly newFrom, out DateOnly newTo)
    {
        newFrom = from;
        newTo = to;
        var dialog = new Window
        {
            Owner = owner,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize
        };
        dialog.SetBinding(Window.TitleProperty, LanguageService.Instance.Binding(titleKey));
        var root = new StackPanel { Margin = new Thickness(16) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var fromPicker = new DatePicker { SelectedDate = from.ToDateTime(TimeOnly.MinValue) };
        var toPicker = new DatePicker { SelectedDate = to.ToDateTime(TimeOnly.MinValue) };
        var fromLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        fromLabel.SetBinding(TextBlock.TextProperty, LanguageService.Instance.Binding("FromWord"));
        var toLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        toLabel.SetBinding(TextBlock.TextProperty, LanguageService.Instance.Binding("ToWord"));
        row.Children.Add(fromLabel);
        row.Children.Add(fromPicker);
        row.Children.Add(toLabel);
        row.Children.Add(toPicker);
        root.Children.Add(row);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button { IsCancel = true, MinWidth = 100 };
        cancel.SetBinding(Button.ContentProperty, LanguageService.Instance.Binding("Cancel"));
        buttons.Children.Add(cancel);
        var ok = new Button { MinWidth = 120, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        ok.SetBinding(Button.ContentProperty, LanguageService.Instance.Binding("Continue"));
        DateOnly pickedFrom = from;
        DateOnly pickedTo = to;
        ok.Click += (_, _) =>
        {
            if (fromPicker.SelectedDate is null || toPicker.SelectedDate is null)
            {
                Ui.Info(dialog, L.T("BothDates"));
                return;
            }

            pickedFrom = DateOnly.FromDateTime(fromPicker.SelectedDate.Value);
            pickedTo = DateOnly.FromDateTime(toPicker.SelectedDate.Value);
            if (pickedTo < pickedFrom)
            {
                Ui.Info(dialog, L.T("EndBeforeStart"));
                return;
            }

            dialog.DialogResult = true;
        };
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        dialog.Content = root;
        if (dialog.ShowDialog() != true)
            return false;
        newFrom = pickedFrom;
        newTo = pickedTo;
        return true;
    }
}
