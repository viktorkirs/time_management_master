using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using TimeManagementMaster.Core;

namespace TimeManagementMaster;

public partial class App : Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;

    internal static TimeManagementDatabase? Database { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LanguageService.Instance.Apply(LanguagePreference.Load(), persist: false);
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(StampLanguage));
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(TextCatalog.Culture.IetfLanguageTag)));

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, TextCatalog.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // One dispatcher on this Windows session. An abandoned mutex means the previous process died.
        _mutex = new Mutex(false, @"Local\TimeManagementMaster.SingleInstance");
        try
        {
            _ownsMutex = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }

        if (!_ownsMutex)
        {
            MessageBox.Show(
                TextCatalog.Get("AlreadyOpen"),
                TextCatalog.Get("AppTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            Database = TimeManagementDatabase.OpenDefault();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                TextCatalog.Format("DbOpenFailed", ex.Message),
                TextCatalog.Get("ErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        try
        {
            Database.EnsureDailyBackup(DateOnly.FromDateTime(DateTime.Now));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                TextCatalog.Format("DailyBackupFailed", ex.Message),
                TextCatalog.Get("BackupTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        var window = new MainWindow(Database);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Already released.
            }
        }

        _mutex?.Dispose();
        Database?.Dispose();
        base.OnExit(e);
    }

    private static void StampLanguage(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
            window.Language = XmlLanguage.GetLanguage(TextCatalog.Culture.IetfLanguageTag);
    }
}
