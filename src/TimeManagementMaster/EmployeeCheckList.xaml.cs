using System.Windows;
using System.Windows.Controls;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public partial class EmployeeCheckList : UserControl
{
    private readonly List<CheckBox> _boxes = [];

    public EmployeeCheckList()
    {
        InitializeComponent();
        Loaded += (_, _) => { LanguageService.Instance.LanguageChanged += OnLanguage; UpdateCount(); };
        Unloaded += (_, _) => LanguageService.Instance.LanguageChanged -= OnLanguage;
    }

    private void OnLanguage(object? sender, EventArgs e) => Dispatcher.Invoke(UpdateCount);

    public void LoadPeople(IReadOnlyList<Employee> people, IReadOnlyCollection<long>? selected)
    {
        Panel.Children.Clear();
        _boxes.Clear();
        var chosen = selected?.ToHashSet() ?? [];
        foreach (var person in people)
        {
            var box = new CheckBox
            {
                Content = Ui.PersonLine(person),
                Tag = person,
                IsChecked = chosen.Contains(person.Id),
                Margin = new Thickness(0, 2, 0, 2)
            };
            box.Checked += (_, _) => UpdateCount();
            box.Unchecked += (_, _) => UpdateCount();
            _boxes.Add(box);
            Panel.Children.Add(box);
        }

        ApplyFilter();
    }

    public IReadOnlyList<Employee> SelectedEmployees() =>
        _boxes.Where(box => box.IsChecked == true)
            .Select(box => (Employee)box.Tag)
            .ToList();

    private void SearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void All_Click(object sender, RoutedEventArgs e)
    {
        foreach (var box in _boxes.Where(box => box.Visibility == Visibility.Visible))
            box.IsChecked = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var box in _boxes)
            box.IsChecked = false;
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        foreach (var box in _boxes)
        {
            var person = (Employee)box.Tag;
            var match = query.Length == 0
                || person.LastName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || person.FirstName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (person.Specialty?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
            box.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateCount();
        if (EmptyText is not null)
        {
            EmptyText.Text = L.T(_boxes.Count == 0 ? "NoEmployeesToAssign" : "NobodyFound");
            EmptyText.Visibility = _boxes.All(box => box.Visibility != Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateCount()
    {
        if (CountText is null)
            return;
        CountText.Text = L.F("SelectedCount", SelectedEmployees().Count);
    }
}
