using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public partial class ConflictWindow : Window
{
    private readonly Func<IReadOnlyList<ConflictRow>> _load;

    public ConflictDecision Decision { get; private set; } = ConflictDecision.Cancel;

    public ConflictWindow(Func<IReadOnlyList<ConflictRow>> load)
    {
        _load = load;
        InitializeComponent();
        LanguageService.Instance.LanguageChanged += OnLanguage;
        Closed += (_, _) => LanguageService.Instance.LanguageChanged -= OnLanguage;
        Rebuild();
    }

    private void OnLanguage(object? sender, EventArgs e) => Dispatcher.Invoke(Rebuild);

    private void Rebuild()
    {
        Blocks.Children.Clear();
        foreach (var group in _load().GroupBy(row => row.EmployeeId))
        {
            var first = group.First();
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = first.EmployeeName,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            });
            if (!string.IsNullOrWhiteSpace(first.Specialty))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = first.Specialty,
                    Foreground = Palette.Brush("MutedBrush"),
                    FontSize = 12,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            foreach (var row in group)
            {
                stack.Children.Add(Pair(L.T("Dates"), row.Dates));
                stack.Children.Add(Pair(L.T("Requested"), row.Requested));
                stack.Children.Add(Pair(L.T("Existing"), row.Existing));
            }

            Blocks.Children.Add(new Border
            {
                Background = Palette.Brush("PaperBrush"),
                BorderBrush = Palette.Brush("LineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8),
                Child = stack
            });
        }
    }

    private static UIElement Pair(string label, string value)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Palette.Brush("MutedBrush")
        });
        panel.Children.Add(new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });
        return panel;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Decision = ConflictDecision.Cancel;
        DialogResult = false;
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        Decision = ConflictDecision.EditDates;
        DialogResult = false;
    }

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        Decision = ConflictDecision.ReplaceAll;
        DialogResult = true;
    }
}
