using System.Windows;
using System.Windows.Input;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public partial class SitesWindow : Window
{
    private readonly TimeManagementDatabase _db;
    private readonly DateOnly _date;

    public SitesWindow(TimeManagementDatabase db, DateOnly? date = null)
    {
        InitializeComponent();
        _db = db;
        _date = date ?? Ui.Today;
        LoadRows();
    }

    public static bool AddSite(Window owner, TimeManagementDatabase db, DateOnly? date = null)
    {
        var form = new SiteEditorWindow(db, offerAssignment: true) { Owner = owner };
        if (form.ShowDialog() != true || form.SavedSite is null)
            return false;
        if (form.AssignAfterSave)
        {
            var assignment = new AssignmentWindow(db, "AssignToSite", "AssignSiteHint", form.SavedSite.Id, null, date) { Owner = owner };
            assignment.ShowDialog();
        }
        return true;
    }

    private void Reload(object sender, RoutedEventArgs e) => LoadRows();

    private void LoadRows()
    {
        if (SearchBox is null || Grid is null || _db is null)
            return;
        var selectedId = Selected()?.Id;
        var sites = _db.SearchSites(SearchBox.Text, ArchiveBox.IsChecked == true);
        Grid.ItemsSource = sites;
        Grid.SelectedItem = sites.FirstOrDefault(site => site.Id == selectedId);
        EmptyText.Visibility = sites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateActions();
    }

    private Site? Selected() => Grid.SelectedItem as Site;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (AddSite(this, _db, _date))
            LoadRows();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (e is MouseButtonEventArgs mouse && mouse.ChangedButton != MouseButton.Left)
            return;
        var site = Selected();
        if (site is null)
        {
            Ui.Info(this, L.T("PickSite"));
            return;
        }

        var form = new SiteEditorWindow(_db, site) { Owner = this };
        if (form.ShowDialog() == true)
            LoadRows();
    }

    private void Selection_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateActions();

    private void UpdateActions()
    {
        if (EditButton is null)
            return;
        var site = Selected();
        EditButton.IsEnabled = CardButton.IsEnabled = site is not null;
        AssignButton.IsEnabled = ArchiveButton.IsEnabled = site is { IsArchived: false };
        RestoreButton.IsEnabled = site is { IsArchived: true };
    }

    private void Assign_Click(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { IsArchived: false } site)
            return;
        var dialog = new AssignmentWindow(_db, "AssignToSite", "AssignSiteHint", site.Id, null, _date) { Owner = this };
        dialog.ShowDialog();
    }

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        var site = Selected();
        if (site is null)
        {
            Ui.Info(this, L.T("PickSite"));
            return;
        }

        var card = new SiteCardWindow(_db, site.Id, _date) { Owner = this };
        card.ShowDialog();
        LoadRows();
    }

    private void Archive_Click(object sender, RoutedEventArgs e)
    {
        var site = Selected();
        if (site is null)
        {
            Ui.Info(this, L.T("PickSite"));
            return;
        }

        if (site.IsArchived)
        {
            Ui.Info(this, L.T("AlreadyArchivedSite"));
            return;
        }

        var today = Ui.Today;
        var dialog = new ArchiveWindow(
            "ArchiveSiteTitle",
            "ArchiveSiteIntro",
            () => _db.PreviewSiteArchive(site.Id, today))
        { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            _db.ArchiveSite(site.Id, today);
            LoadRows();
        }
        catch (Exception ex)
        {
            Ui.Error(this, ex);
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var site = Selected();
        if (site is null)
        {
            Ui.Info(this, L.T("PickSite"));
            return;
        }

        if (!site.IsArchived)
        {
            Ui.Info(this, L.T("NotArchivedSite"));
            return;
        }

        try
        {
            _db.RestoreSite(site.Id);
            LoadRows();
        }
        catch (Exception ex)
        {
            Ui.Error(this, ex);
        }
    }

}
