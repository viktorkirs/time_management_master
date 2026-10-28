using System.Globalization;

namespace TimeManagementMaster.Core;

public enum AppLanguage
{
    Ru = 0,
    En = 1,
    De = 2,
    Ro = 3
}

public static class TextCatalog
{
    public const string DefaultCode = "de";

    public static readonly AppLanguage DefaultLanguage = AppLanguage.De;

    public static readonly IReadOnlyList<AppLanguage> Languages =
        [AppLanguage.Ru, AppLanguage.En, AppLanguage.De, AppLanguage.Ro];

    private static readonly AsyncLocal<AppLanguage?> Ambient = new();
    private static AppLanguage? _applicationLanguage;
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly CultureInfo Romanian = CultureInfo.GetCultureInfo("ro-RO");

    private static readonly Dictionary<string, string[]> Table = Build();

    public static IReadOnlyCollection<string> Keys => Table.Keys;

    public static AppLanguage Current
    {
        get => _applicationLanguage ?? Ambient.Value ?? DefaultLanguage;
        set => Ambient.Value = value;
    }

    // Desktop language is an application preference. AsyncLocal alone is reset
    // when WPF returns from a dispatcher event to its outer execution context.
    public static void SetApplicationLanguage(AppLanguage language) => _applicationLanguage = language;

    public static CultureInfo Culture => CultureOf(Current);

    public static CultureInfo CultureOf(AppLanguage language) => language switch
    {
        AppLanguage.Ru => Russian,
        AppLanguage.En => English,
        AppLanguage.Ro => Romanian,
        _ => German
    };

    public static string Code(AppLanguage language) => language switch
    {
        AppLanguage.Ru => "ru",
        AppLanguage.En => "en",
        AppLanguage.Ro => "ro",
        _ => "de"
    };

    public static AppLanguage? FromCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        var token = code.Trim().ToLowerInvariant();
        var dash = token.IndexOf('-');
        if (dash > 0)
            token = token[..dash];
        if (token.Length > 2)
            token = token[..2];
        return token switch
        {
            "ru" => AppLanguage.Ru,
            "en" => AppLanguage.En,
            "de" => AppLanguage.De,
            "ro" => AppLanguage.Ro,
            _ => null
        };
    }

    public static string Get(string key) => Get(key, Current);

    public static string Get(string key, AppLanguage language)
    {
        if (!Table.TryGetValue(key, out var row))
            throw new KeyNotFoundException(key);
        var index = (int)language;
        if (index < 0 || index >= row.Length || string.IsNullOrWhiteSpace(row[index]))
            throw new KeyNotFoundException(key + "/" + Code(language));
        return row[index];
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(Culture, Get(key), args);

    public static string FormatDate(DateOnly date) =>
        date.ToString("d", Culture);

    public static string FormatLong(DateOnly date) =>
        date.ToString("dd MMMM yyyy, dddd", Culture);

    public static string FormatMonth(DateOnly date) =>
        date.ToString("MMMM yyyy", Culture);

    public static string FormatDayMonth(DateOnly date)
    {
        var culture = Culture;
        var separator = culture.DateTimeFormat.DateSeparator;
        var value = date.ToDateTime(TimeOnly.MinValue);
        return value.ToString("dd", culture) + separator + value.ToString("MM", culture);
    }

    public static string FormatWeekday(DateOnly date) =>
        date.ToString("ddd", Culture);

    private static Dictionary<string, string[]> Build()
    {
        var table = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Add(string key, string ru, string en, string de, string ro) =>
            table.Add(key, [ru, en, de, ro]);

        Add("AppTitle", "Учёт сотрудников на объектах", "Employees on sites", "Mitarbeitereinsatz auf Baustellen", "Evidența angajaților pe șantiere");
        Add("AppShort", "Учёт на объектах", "Site schedule", "Baustelleneinsatz", "Planificare șantier");
        Add("AlreadyOpen", "Программа уже открыта", "The program is already open", "Das Programm ist bereits geöffnet", "Programul este deja deschis");
        Add("ErrorTitle", "Ошибка", "Error", "Fehler", "Eroare");
        Add("BackupTitle", "Резервная копия", "Backup", "Sicherungskopie", "Copie de rezervă");
        Add("SaveFailedTitle", "Не удалось сохранить", "Could not save", "Speichern fehlgeschlagen", "Salvarea a eșuat");
        Add("MessageTitle", "Сообщение", "Message", "Hinweis", "Mesaj");
        Add("CannotSave", "Нельзя сохранить", "Cannot save", "Speichern nicht möglich", "Nu se poate salva");
        Add("NoChanges", "Изменений нет.", "Nothing to change.", "Keine Änderungen.", "Nicio modificare.");
        Add("Saving", "Сохранение", "Save", "Speichern", "Salvare");
        Add("DbOpenFailed", "Не удалось открыть базу:\n{0}", "Could not open the database:\n{0}", "Die Datenbank konnte nicht geöffnet werden:\n{0}", "Baza de date nu a putut fi deschisă:\n{0}");
        Add("DailyBackupFailed", "Не удалось создать ежедневную копию:\n{0}\nПрограмма будет открыта без новой копии.", "Could not create the daily backup:\n{0}\nThe program will open without a new copy.", "Die tägliche Sicherung konnte nicht erstellt werden:\n{0}\nDas Programm wird ohne neue Kopie geöffnet.", "Copia zilnică nu a putut fi creată:\n{0}\nProgramul se deschide fără o copie nouă.");

        Add("Assign", "Назначить", "Assign", "Einteilen", "Alocare");
        Add("Absence", "Отсутствие", "Absence", "Abwesenheit", "Absență");
        Add("Move", "Перенести", "Move", "Umteilen", "Mutare");
        Add("ClearDays", "Снять дни", "Clear days", "Tage freigeben", "Eliberare zile");
        Add("CopyDay", "Копировать день", "Copy day", "Tag kopieren", "Copiere zi");
        Add("CopyWeek", "Копировать неделю", "Copy week", "Woche kopieren", "Copiere săptămână");
        Add("Print", "Печать", "Print", "Drucken", "Tipărire");
        Add("PrintDay", "Печать дня", "Print day", "Tag drucken", "Tipărire zi");
        Add("PrintWeek", "Печать недели", "Print week", "Woche drucken", "Tipărire săptămână");
        Add("File", "Файл", "File", "Datei", "Fișier");
        Add("ExportCsv", "Экспорт CSV", "Export CSV", "CSV exportieren", "Export CSV");
        Add("Backup", "Резервная копия", "Backup", "Sicherung", "Copie de rezervă");
        Add("Restore", "Восстановить", "Restore", "Wiederherstellen", "Restaurare");
        Add("Employees", "Сотрудники", "Employees", "Mitarbeiter", "Angajați");
        Add("Sites", "Объекты", "Sites", "Baustellen", "Șantiere");
        Add("PeopleDirectory", "Сотрудники и архив", "Employees and archive", "Mitarbeiter und Archiv", "Angajați și arhivă");
        Add("SitesDirectory", "Объекты и архив", "Sites and archive", "Baustellen und Archiv", "Șantiere și arhivă");
        Add("Calendar", "Календарь", "Calendar", "Kalender", "Calendar");
        Add("Language", "Язык", "Language", "Sprache", "Limbă");
        Add("BySite", "По объектам", "By site", "Nach Baustelle", "După șantier");
        Add("ByEmployee", "По сотрудникам", "By employee", "Nach Mitarbeiter", "După angajat");
        Add("Day", "День", "Day", "Tag", "Zi");
        Add("Week", "Неделя", "Week", "Woche", "Săptămână");
        Add("Month", "Месяц", "Month", "Monat", "Lună");
        Add("Today", "Сегодня", "Today", "Heute", "Azi");
        Add("Employee", "Сотрудник", "Employee", "Mitarbeiter", "Angajat");
        Add("Site", "Объект", "Site", "Baustelle", "Șantier");
        Add("AllSites", "Все объекты", "All sites", "Alle Baustellen", "Toate șantierele");
        Add("AssignedOn", "Назначено на {0}: {1}", "Assigned on {0}: {1}", "Eingeteilt am {0}: {1}", "Alocați la {0}: {1}");
        Add("OnSite", "на объекте", "on site", "auf der Baustelle", "pe șantier");
        Add("Unassigned", "Не распределён", "Unassigned", "Nicht eingeteilt", "Nealocat");
        Add("UnassignedPlural", "Не распределены", "Unassigned", "Nicht eingeteilt", "Nealocați");
        Add("AllAssigned", "Все распределены", "Everyone is assigned", "Alle sind eingeteilt", "Toți sunt alocați");
        Add("NoAssignments", "Нет назначений", "No assignments", "Keine Einteilungen", "Nicio alocare");
        Add("Absences", "Отсутствия", "Absences", "Abwesenheiten", "Absențe");
        Add("None", "Нет", "None", "Keine", "Niciuna");
        Add("NobodyFound", "Никого не найдено.", "No one found.", "Niemand gefunden.", "Nimeni găsit.");
        Add("NoEmployeesShort", "Нет сотрудников.", "No employees.", "Keine Mitarbeiter.", "Niciun angajat.");
        Add("WeekendNoUnassigned", "В субботу и воскресенье нераспределённые не показываются.", "Unassigned people are not listed on Saturday or Sunday.", "Samstag und Sonntag stehen nicht in der Liste der nicht eingeteilten Mitarbeiter.", "Sâmbăta și duminica persoanele nealocate nu sunt afișate.");
        Add("EmptyEmployees", "Нет сотрудников. Добавьте первого сотрудника.", "There are no employees. Add the first one.", "Es gibt keine Mitarbeiter. Legen Sie den ersten an.", "Nu există angajați. Adăugați primul angajat.");
        Add("AddEmployee", "Добавить сотрудника", "Add employee", "Mitarbeiter hinzufügen", "Adaugă angajat");
        Add("EmptySites", "Нет объектов. Добавьте первый объект.", "There are no sites. Add the first one.", "Es gibt keine Baustellen. Legen Sie die erste an.", "Nu există șantiere. Adăugați primul șantier.");
        Add("AddSite", "Добавить объект", "Add site", "Baustelle hinzufügen", "Adaugă șantier");
        Add("DemoData", "Демо-данные", "Demo data", "Demodaten", "Date demo");
        Add("DemoOnlyEmpty", "Демо-данные добавляются только когда списки сотрудников и объектов пусты.", "Demo data is added only when the employee and site lists are empty.", "Demodaten werden nur hinzugefügt, wenn die Listen der Mitarbeiter und Baustellen leer sind.", "Datele demo se adaugă numai când listele de angajați și șantiere sunt goale.");
        Add("DemoAdded", "Добавлены демо-сотрудники и объекты.", "Demo employees and sites were added.", "Demo-Mitarbeiter und Baustellen wurden hinzugefügt.", "Au fost adăugați angajați și șantiere demo.");
        Add("ArchivedName", "{0} (архив)", "{0} (archive)", "{0} (Archiv)", "{0} (arhivă)");
        Add("ShownOf", "в списке {0} из {1}", "showing {0} of {1}", "in der Liste {0} von {1}", "în listă {0} din {1}");
        Add("Remove", "Снять", "Remove", "Entfernen", "Scoate");
        Add("Weekend", "выходной", "weekend", "Wochenende", "weekend");
        Add("Archive", "Архив", "Archive", "Archiv", "Arhivă");
        Add("StatusDb", "База: {0}", "Database: {0}", "Datenbank: {0}", "Bază de date: {0}");
        Add("DayCleared", "День снят.", "Day cleared.", "Tag freigegeben.", "Zi eliberată.");
        Add("Saved", "Сохранено.", "Saved.", "Gespeichert.", "Salvat.");
        Add("DaysCleared", "Дни сняты.", "Days cleared.", "Tage freigegeben.", "Zile eliberate.");
        Add("CopySaved", "Копирование сохранено.", "Copy saved.", "Kopie gespeichert.", "Copiere salvată.");
        Add("CsvSaved", "CSV сохранён: {0}", "CSV saved: {0}", "CSV gespeichert: {0}", "CSV salvat: {0}");
        Add("BackupSaved", "Резервная копия сохранена: {0}", "Backup saved: {0}", "Sicherung gespeichert: {0}", "Copie de rezervă salvată: {0}");
        Add("Restored", "Данные восстановлены из файла.", "Data restored from the file.", "Daten aus der Datei wiederhergestellt.", "Datele au fost restaurate din fișier.");
        Add("ClearOneConfirm", "Снять {0} с {1}?", "Remove {0} on {1}?", "{0} am {1} freigeben?", "Scoate {0} la {1}?");
        Add("ClearCellsConfirm", "Снять назначения с выбранных ячеек? Отсутствия не изменяются.", "Clear assignments in the selected cells? Absences stay as they are.", "Einteilungen in den markierten Zellen freigeben? Abwesenheiten bleiben unverändert.", "Eliberați alocările din celulele selectate? Absențele rămân neschimbate.");
        Add("AssignHint", "Один объект на выбранные дни. Если есть пересечения, ничего не записывается, пока вы не нажмёте «Заменить и сохранить».", "One site for the selected days. If anything overlaps, nothing is written until you choose Replace and save.", "Eine Baustelle für die gewählten Tage. Bei Überschneidungen wird nichts geschrieben, bis Sie Ersetzen und speichern wählen.", "Un șantier pentru zilele alese. Dacă există suprapuneri, nu se scrie nimic până la Înlocuiește și salvează.");
        Add("MoveTitle", "Перенести на объект", "Move to a site", "Auf eine Baustelle umteilen", "Mutare pe șantier");
        Add("MoveHint", "Меняется объект только на выбранных днях. Остальные дни остаются. Перенос на тот же объект ничего не меняет.", "Only the selected days change site. The other days stay. Moving to the same site changes nothing.", "Nur die gewählten Tage wechseln die Baustelle. Die übrigen Tage bleiben. Dieselbe Baustelle ändert nichts.", "Se schimbă șantierul doar în zilele alese. Celelalte zile rămân. Mutarea pe același șantier nu schimbă nimic.");
        Add("PickEmployees", "Выберите хотя бы одного сотрудника.", "Select at least one employee.", "Wählen Sie mindestens einen Mitarbeiter.", "Alegeți cel puțin un angajat.");
        Add("PickSiteFirst", "Выберите объект. Если списка нет, сначала добавьте объект.", "Select a site. If the list is empty, add a site first.", "Wählen Sie eine Baustelle. Wenn die Liste leer ist, legen Sie zuerst eine Baustelle an.", "Alegeți un șantier. Dacă lista este goală, adăugați mai întâi un șantier.");
        Add("CheckDates", "Проверьте даты.", "Check the dates.", "Prüfen Sie die Daten.", "Verificați datele.");
        Add("ArchivedBlocked", "{0} (архив, новые назначения недоступны)", "{0} (archived, new assignments are unavailable)", "{0} (Archiv, neue Einteilungen sind nicht möglich)", "{0} (arhivă, alocări noi indisponibile)");

        Add("ConflictTitle", "Пересечения", "Overlaps", "Überschneidungen", "Suprapuneri");
        Add("ConflictIntro", "Есть пересечения. Ничего не записано. Можно отменить, вернуться к датам или заменить все перечисленные конфликты и сохранить весь набор целиком.", "There are overlaps. Nothing has been written. You can cancel, go back to the dates, or replace every listed conflict and save the whole set.", "Es gibt Überschneidungen. Es wurde nichts geschrieben. Sie können abbrechen, zu den Daten zurückkehren oder alle genannten Konflikte ersetzen und den ganzen Satz speichern.", "Există suprapuneri. Nu s-a scris nimic. Puteți anula, reveni la date sau înlocui toate conflictele afișate și salva întregul set.");
        Add("EditDates", "Изменить даты", "Change dates", "Daten ändern", "Modifică datele");
        Add("Cancel", "Отмена", "Cancel", "Abbrechen", "Anulează");
        Add("ReplaceSave", "Заменить и сохранить", "Replace and save", "Ersetzen und speichern", "Înlocuiește și salvează");
        Add("Specialty", "Специальность", "Specialty", "Beruf", "Specialitate");
        Add("Dates", "Даты", "Dates", "Daten", "Date");
        Add("Requested", "Запрошено", "Requested", "Gewünscht", "Cerut");
        Add("Existing", "Уже есть", "Already there", "Bereits vorhanden", "Deja existent");

        Add("Close", "Закрыть", "Close", "Schließen", "Închide");
        Add("ChangePeriod", "Изменить срок", "Change dates", "Zeitraum ändern", "Modifică perioada");
        Add("SetEndDate", "Указать дату окончания", "Set the end date", "Enddatum setzen", "Setează data de sfârșit");
        Add("CancelAbsence", "Отменить отсутствие", "Cancel absence", "Abwesenheit aufheben", "Anulează absența");
        Add("Period", "Период", "Period", "Zeitraum", "Perioadă");
        Add("NewAbsence", "Новое отсутствие", "New absence", "Neue Abwesenheit", "Absență nouă");
        Add("Kind", "Вид", "Type", "Art", "Tip");
        Add("Start", "Начало", "Start", "Beginn", "Început");
        Add("Ongoing", "Продолжается", "Ongoing", "Läuft", "În continuare");
        Add("End", "Окончание", "End", "Ende", "Sfârșit");
        Add("NoteOther", "Пояснение (для «Другое», до 50 символов)", "Note (for Other, up to 50 characters)", "Hinweis (für Sonstiges, bis 50 Zeichen)", "Observație (pentru Altul, maximum 50 de caractere)");
        Add("Add", "Добавить", "Add", "Hinzufügen", "Adaugă");
        Add("PickPersonAndKind", "Выберите сотрудника и вид отсутствия.", "Select an employee and an absence type.", "Wählen Sie einen Mitarbeiter und eine Abwesenheitsart.", "Alegeți un angajat și tipul de absență.");
        Add("PickStart", "Укажите дату начала.", "Enter the start date.", "Geben Sie das Beginndatum an.", "Indicați data de început.");
        Add("PickEnd", "Укажите дату окончания.", "Enter the end date.", "Geben Sie das Enddatum an.", "Indicați data de sfârșit.");
        Add("EndBeforeStart", "Дата окончания раньше даты начала.", "The end date is before the start date.", "Das Enddatum liegt vor dem Beginndatum.", "Data de sfârșit este înainte de data de început.");
        Add("PickAbsence", "Выберите отсутствие в списке.", "Select an absence in the list.", "Wählen Sie eine Abwesenheit in der Liste.", "Alegeți o absență din listă.");
        Add("PickOpenSick", "Выберите открытый больничный.", "Select an open sick leave.", "Wählen Sie eine laufende Krankmeldung.", "Alegeți un concediu medical deschis.");
        Add("SickEndTitle", "Дата окончания больничного", "Sick leave end date", "Ende der Krankmeldung", "Data de sfârșit a concediului medical");
        Add("CancelAbsenceConfirm", "Отменить это отсутствие? Освободившиеся дни снова станут свободными.", "Cancel this absence? The freed days become open again.", "Diese Abwesenheit aufheben? Die frei werdenden Tage sind wieder offen.", "Anulați această absență? Zilele eliberate devin din nou libere.");

        Add("Copy", "Копировать", "Copy", "Kopieren", "Copiere");
        Add("CopyAction", "Скопировать", "Copy", "Kopieren", "Copiază");
        Add("SourceDate", "Дата источника", "Source date", "Quelldatum", "Data sursă");
        Add("CopyTarget", "Куда копировать", "Copy to", "Ziel", "Unde se copiază");
        Add("WeekdaysOnly", "Только пн–пт", "Weekdays only", "Nur Mo–Fr", "Doar lu–vi");
        Add("CopyFootnote", "Копируются только назначения. Пустая ячейка источника не очищает цель. Люди, которые есть только в цели, остаются. Конфликты показываются все сразу.", "Only assignments are copied. An empty source cell does not clear the target. People who exist only on the target stay. Every conflict is shown at once.", "Es werden nur Einteilungen kopiert. Eine leere Quellzelle leert das Ziel nicht. Personen, die nur im Ziel stehen, bleiben. Alle Konflikte werden auf einmal gezeigt.", "Se copiază doar alocările. O celulă sursă goală nu golește destinația. Persoanele care există doar la destinație rămân. Toate conflictele sunt afișate deodată.");
        Add("CopyWeekTitle", "Копировать неделю", "Copy week", "Woche kopieren", "Copiere săptămână");
        Add("CopyDayTitle", "Копировать день", "Copy day", "Tag kopieren", "Copiere zi");
        Add("CopyWeekSource", "Неделя источника (любой день этой недели)", "Source week (any day of that week)", "Quellwoche (ein beliebiger Tag dieser Woche)", "Săptămâna sursă (orice zi din acea săptămână)");
        Add("CopyDaySource", "День источника", "Source day", "Quelltag", "Ziua sursă");
        Add("CopyWeekHint", "Назначения переносятся по дням недели: понедельник к понедельникам выбранного диапазона и так далее. Явная суббота источника копируется, если не включено «Только пн–пт».", "Assignments follow the weekday: Monday goes to Mondays in the chosen range, and so on. An explicit Saturday in the source is copied unless Weekdays only is on.", "Einteilungen folgen dem Wochentag: Montag auf die Montage des gewählten Bereichs und so weiter. Ein ausdrücklicher Samstag der Quelle wird kopiert, wenn Nur Mo–Fr aus ist.", "Alocările urmează ziua săptămânii: luni merge la zilele de luni din interval și tot așa. O sâmbătă explicită din sursă se copiază dacă Doar lu–vi este oprit.");
        Add("CopyDayHint", "Набор «сотрудник → объект» этого дня раскладывается на каждый день цели.", "The employee-to-site set of this day is placed on every target day.", "Die Zuordnung Mitarbeiter → Baustelle dieses Tages wird auf jeden Zieltag gelegt.", "Setul angajat → șantier al acestei zile se pune pe fiecare zi destinație.");
        Add("PickDates", "Укажите даты.", "Enter the dates.", "Geben Sie die Daten an.", "Indicați datele.");
        Add("FromWord", "С", "From", "Von", "De la");
        Add("ToWord", "по", "to", "bis", "până la");
        Add("Continue", "Дальше", "Continue", "Weiter", "Continuă");
        Add("BothDates", "Укажите обе даты.", "Enter both dates.", "Geben Sie beide Daten an.", "Indicați ambele date.");

        Add("ShowArchive", "показывать архив", "show archive", "Archiv anzeigen", "arată arhiva");
        Add("Edit", "Изменить", "Edit", "Bearbeiten", "Modifică");
        Add("ToArchive", "В архив", "Archive", "Archivieren", "În arhivă");
        Add("RestoreArchive", "Вернуть из архива", "Restore from archive", "Aus Archiv holen", "Scoate din arhivă");
        Add("NewEmployee", "Новый сотрудник", "New employee", "Neuer Mitarbeiter", "Angajat nou");
        Add("EmployeeCard", "Сотрудник", "Employee", "Mitarbeiter", "Angajat");
        Add("LastName", "Фамилия", "Last name", "Nachname", "Nume");
        Add("FirstName", "Имя", "First name", "Vorname", "Prenume");
        Add("LastNameStar", "Фамилия *", "Last name *", "Nachname *", "Nume *");
        Add("FirstNameStar", "Имя *", "First name *", "Vorname *", "Prenume *");
        Add("Phone", "Телефон", "Phone", "Telefon", "Telefon");
        Add("Note", "Примечание", "Note", "Notiz", "Observație");
        Add("PickEmployee", "Выберите сотрудника.", "Select an employee.", "Wählen Sie einen Mitarbeiter.", "Alegeți un angajat.");
        Add("ArchiveEmployeeTitle", "Архив сотрудника", "Archive employee", "Mitarbeiter archivieren", "Arhivare angajat");
        Add("ArchiveEmployeeIntro", "Сотрудник пропадёт из новых назначений. Назначения с завтрашнего дня будут сняты. Отсутствие, начавшееся раньше сегодня, закроется вчерашним днём. Отсутствие с сегодняшнего дня сохранится только на сегодня, более позднее будет удалено. Прошлые дни не изменятся.", "The employee disappears from new assignments. Assignments from tomorrow on are removed. An absence that started before today closes yesterday. An absence that starts today is kept for today only. A later one is deleted. Past days stay as they are.", "Der Mitarbeiter verschwindet aus neuen Einteilungen. Einteilungen ab morgen werden entfernt. Eine Abwesenheit, die vor heute begann, endet gestern. Eine Abwesenheit ab heute bleibt nur für heute. Eine spätere wird gelöscht. Vergangene Tage bleiben.", "Angajatul dispare din alocările noi. Alocările de mâine încolo sunt scoase. O absență începută înainte de azi se închide ieri. O absență de azi rămâne doar pentru azi. Una mai târzie este ștearsă. Zilele trecute rămân.");
        Add("AlreadyArchivedEmployee", "Сотрудник уже в архиве.", "This employee is already archived.", "Dieser Mitarbeiter ist bereits im Archiv.", "Angajatul este deja în arhivă.");
        Add("NotArchivedEmployee", "Сотрудник не в архиве.", "This employee is not archived.", "Dieser Mitarbeiter ist nicht im Archiv.", "Angajatul nu este în arhivă.");
        Add("NewSite", "Новый объект", "New site", "Neue Baustelle", "Șantier nou");
        Add("SiteCardTitle", "Объект", "Site", "Baustelle", "Șantier");
        Add("SiteName", "Название объекта", "Site name", "Name der Baustelle", "Numele șantierului");
        Add("SiteNameStar", "Название *", "Name *", "Name *", "Nume *");
        Add("Address", "Адрес", "Address", "Adresse", "Adresă");
        Add("Contact", "Контактное лицо", "Contact", "Kontakt", "Persoană de contact");
        Add("PickSite", "Выберите объект.", "Select a site.", "Wählen Sie eine Baustelle.", "Alegeți un șantier.");
        Add("AlreadyArchivedSite", "Объект уже в архиве.", "This site is already archived.", "Diese Baustelle ist bereits im Archiv.", "Șantierul este deja în arhivă.");
        Add("NotArchivedSite", "Объект не в архиве.", "This site is not archived.", "Diese Baustelle ist nicht im Archiv.", "Șantierul nu este în arhivă.");
        Add("SiteCard", "Карточка объекта", "Site card", "Baustellenkarte", "Fișa șantierului");
        Add("Card", "Карточка", "Card", "Karte", "Fișă");
        Add("Show", "Показать", "Show", "Anzeigen", "Afișează");
        Add("NoAssignmentsInRange", "На выбранные даты назначений нет.", "There are no assignments in this range.", "In diesem Zeitraum gibt es keine Einteilungen.", "Nu există alocări în intervalul ales.");
        Add("AssignToSite", "Назначить на объект", "Assign to site", "Auf die Baustelle einteilen", "Alocare pe șantier");
        Add("AssignSiteHint", "Назначение идёт по тем же правилам, что и с главного окна: конфликты не заменяются молча.", "Assignment follows the same rules as the main window: conflicts are not replaced silently.", "Die Einteilung folgt denselben Regeln wie im Hauptfenster: Konflikte werden nicht still ersetzt.", "Alocarea urmează aceleași reguli ca în fereastra principală: conflictele nu sunt înlocuite în tăcere.");
        Add("ArchiveVerb", "Архивировать", "Archive", "Archivieren", "Arhivează");
        Add("NoFutureRecords", "Будущих записей нет. Прошлые дни сохранятся.", "There are no future records. Past days stay as they are.", "Keine zukünftigen Einträge. Vergangene Tage bleiben erhalten.", "Nu există înregistrări viitoare. Zilele trecute rămân neschimbate.");
        Add("ArchiveSiteTitle", "Архив объекта", "Archive site", "Baustelle archivieren", "Arhivare șantier");
        Add("ArchiveSiteIntro", "Объект пропадёт из новых назначений. Назначения с завтрашнего дня будут сняты, и в рабочие дни эти сотрудники станут нераспределёнными, если у них нет отсутствия. Прошлые дни не изменятся.", "The site disappears from new assignments. Assignments from tomorrow on are removed, and on workdays those employees become unassigned unless they have an absence. Past days stay as they are.", "Die Baustelle verschwindet aus neuen Einteilungen. Einteilungen ab morgen werden entfernt, und an Arbeitstagen werden diese Mitarbeiter nicht eingeteilt, sofern keine Abwesenheit vorliegt. Vergangene Tage bleiben.", "Șantierul dispare din alocările noi. Alocările de mâine încolo sunt scoase, iar în zilele lucrătoare acești angajați devin nealocați dacă nu au o absență. Zilele trecute rămân.");

        Add("ClearIntro", "Снимаются только назначения на объект. Отсутствия не изменяются. После снятия рабочего дня сотрудник снова нераспределён. Снятие субботы или воскресенья не добавляет его в этот список.", "Only site assignments are cleared. Absences stay as they are. After a workday is cleared, the employee is unassigned again. Clearing a Saturday or Sunday does not add the person to that list.", "Es werden nur Baustellen-Einteilungen entfernt. Abwesenheiten bleiben. Nach dem Freigeben eines Arbeitstags ist der Mitarbeiter wieder nicht eingeteilt. Samstag oder Sonntag freizugeben setzt die Person nicht auf diese Liste.", "Se scot doar alocările pe șantier. Absențele rămân. După eliberarea unei zile lucrătoare, angajatul este din nou nealocat. Eliberarea sâmbetei sau duminicii nu îl adaugă în acea listă.");
        Add("ClearAction", "Снять", "Clear", "Freigeben", "Eliberează");
        Add("ClearConfirm", "Снять назначения с выбранных дней?", "Clear assignments on the selected days?", "Einteilungen an den gewählten Tagen freigeben?", "Eliberați alocările din zilele alese?");

        Add("OneDay", "Один день", "One day", "Ein Tag", "O zi");
        Add("PeriodMode", "Период", "Period", "Zeitraum", "Perioadă");
        Add("SeparateDays", "Отдельные дни", "Individual days", "Einzelne Tage", "Zile separate");
        Add("PickDate", "Укажите дату.", "Enter a date.", "Geben Sie ein Datum an.", "Indicați o dată.");
        Add("PickPeriodEnds", "Укажите начало и конец периода.", "Enter the start and end of the period.", "Geben Sie Beginn und Ende des Zeitraums an.", "Indicați începutul și sfârșitul perioadei.");
        Add("NoDaysInRange", "В выбранном диапазоне нет дней.", "There are no days in the selected range.", "Im gewählten Bereich gibt es keine Tage.", "În intervalul ales nu există zile.");
        Add("AddAtLeastOne", "Добавьте хотя бы одну дату.", "Add at least one date.", "Fügen Sie mindestens ein Datum hinzu.", "Adăugați cel puțin o dată.");
        Add("AddDate", "Добавить", "Add", "Hinzufügen", "Adaugă");
        Add("RemoveDate", "Убрать выбранную дату", "Remove selected date", "Gewähltes Datum entfernen", "Scoate data selectată");
        Add("WeekdaysHint", "Снимите флажок, чтобы записать и выходные.", "Clear the box to include weekends.", "Haken entfernen, um auch Wochenenden zu schreiben.", "Debifați pentru a include și weekendul.");
        Add("SeparateDaysHint", "Отдельные дни записываются как есть, включая субботу и воскресенье.", "Individual days are written as chosen, including Saturday and Sunday.", "Einzelne Tage werden so geschrieben, wie sie gewählt sind, einschließlich Samstag und Sonntag.", "Zilele separate se scriu așa cum sunt alese, inclusiv sâmbăta și duminica.");
        Add("SelectAll", "Выбрать всех", "Select all", "Alle auswählen", "Selectează toți");
        Add("ClearSelection", "Очистить", "Clear", "Leeren", "Golește");
        Add("SelectedCount", "Отмечено: {0}", "Selected: {0}", "Markiert: {0}", "Bifați: {0}");
        Add("Save", "Сохранить", "Save", "Speichern", "Salvează");

        Add("Sick", "Больничный", "Sick leave", "Krankmeldung", "Concediu medical");
        Add("SickOngoing", "Больничный (продолжается)", "Sick leave (ongoing)", "Krankmeldung (läuft)", "Concediu medical (în continuare)");
        Add("Vacation", "Отпуск", "Vacation", "Urlaub", "Concediu");
        Add("Unpaid", "Неоплачиваемый", "Unpaid leave", "Unbezahlter Urlaub", "Concediu fără plată");
        Add("Other", "Другое", "Other", "Sonstiges", "Altul");
        Add("OtherNote", "Другое: {0}", "Other: {0}", "Sonstiges: {0}", "Altul: {0}");
        Add("CsvSite", "объект", "site", "baustelle", "șantier");
        Add("CsvSick", "больничный", "sick leave", "krankmeldung", "concediu medical");
        Add("CsvVacation", "отпуск", "vacation", "urlaub", "concediu");
        Add("CsvUnpaid", "неоплачиваемый", "unpaid leave", "unbezahlter urlaub", "concediu fără plată");
        Add("CsvOther", "другое", "other", "sonstiges", "altul");
        Add("CsvUnassigned", "нераспределён", "unassigned", "nicht eingeteilt", "nealocat");
        Add("CsvHeader", "дата;фамилия;имя;специальность;категория;объект;пояснение;адрес", "date;last name;first name;specialty;category;site;note;address", "Datum;Nachname;Vorname;Beruf;Kategorie;Baustelle;Hinweis;Adresse", "dată;nume;prenume;specialitate;categorie;șantier;observație;adresă");
        Add("SiteRequest", "Объект: {0}", "Site: {0}", "Baustelle: {0}", "Șantier: {0}");
        Add("OngoingWord", "продолжается", "ongoing", "läuft", "în continuare");
        Add("PeriodOngoing", "{0} — продолжается", "{0} — ongoing", "{0} — läuft", "{0} — în continuare");
        Add("PeriodRange", "{0} — {1}", "{0} — {1}", "{0} — {1}", "{0} — {1}");

        Add("NoSchema", "В базе нет версии схемы.", "The database has no schema version.", "Die Datenbank hat keine Schemaversion.", "Baza de date nu are o versiune de schemă.");
        Add("FileMissing", "Файл не найден. Рабочая база не изменена.", "File not found. The working database was not changed.", "Datei nicht gefunden. Die Arbeitsdatenbank wurde nicht geändert.", "Fișierul nu a fost găsit. Baza de lucru nu a fost modificată.");
        Add("FileCorrupt", "Файл повреждён. Рабочая база не изменена.", "The file is damaged. The working database was not changed.", "Die Datei ist beschädigt. Die Arbeitsdatenbank wurde nicht geändert.", "Fișierul este deteriorat. Baza de lucru nu a fost modificată.");
        Add("NotThisApp", "Это не база программы. Рабочая база не изменена.", "This is not a database of this program. The working database was not changed.", "Das ist keine Datenbank dieses Programms. Die Arbeitsdatenbank wurde nicht geändert.", "Acesta nu este o bază a programului. Baza de lucru nu a fost modificată.");
        Add("BadSchema", "Несовместимая версия данных. Рабочая база не изменена.", "Incompatible data version. The working database was not changed.", "Unverträgliche Datenversion. Die Arbeitsdatenbank wurde nicht geändert.", "Versiune de date incompatibilă. Baza de lucru nu a fost modificată.");
        Add("FileOk", "Файл подходит для восстановления.", "The file can be restored.", "Die Datei kann wiederhergestellt werden.", "Fișierul poate fi restaurat.");
        Add("NotAppOrCorrupt", "Это не база программы или файл повреждён. Рабочая база не изменена.", "This is not a database of this program, or the file is damaged. The working database was not changed.", "Das ist keine Datenbank dieses Programms oder die Datei ist beschädigt. Die Arbeitsdatenbank wurde nicht geändert.", "Acesta nu este o bază a programului sau fișierul este deteriorat. Baza de lucru nu a fost modificată.");
        Add("SameFile", "Выбран текущий файл базы. Рабочая база не изменена.", "The current database file was selected. The working database was not changed.", "Die aktuelle Datenbankdatei wurde gewählt. Die Arbeitsdatenbank wurde nicht geändert.", "A fost ales fișierul curent al bazei. Baza de lucru nu a fost modificată.");
        Add("SafetyFailed", "Не удалось вернуть страховочную копию: {0}", "Could not restore the safety copy: {0}", "Die Sicherheitskopie konnte nicht zurückgespielt werden: {0}", "Copia de siguranță nu a putut fi readusă: {0}");
        Add("BadDbVersion", "Несовместимая версия базы ({0}).", "Incompatible database version ({0}).", "Unverträgliche Datenbankversion ({0}).", "Versiune de bază incompatibilă ({0}).");
        Add("SaveDataFailed", "Не удалось сохранить данные: {0}", "Could not save the data: {0}", "Die Daten konnten nicht gespeichert werden: {0}", "Datele nu au putut fi salvate: {0}");
        Add("FieldRequired", "{0} обязательно.", "{0} is required.", "{0} ist erforderlich.", "{0} este obligatoriu.");
        Add("PhoneTooLong", "Телефон не длиннее 40 символов.", "Phone is at most 40 characters.", "Telefon hat höchstens 40 Zeichen.", "Telefonul are cel mult 40 de caractere.");
        Add("ConflictsRemain", "Есть конфликты. Данные не изменены.", "There are conflicts. The data was not changed.", "Es gibt Konflikte. Die Daten wurden nicht geändert.", "Există conflicte. Datele nu au fost modificate.");
        Add("OpenSickNoAssign", "Открытый больничный нельзя перекрыть назначением.", "An open sick leave cannot be covered by an assignment.", "Eine laufende Krankmeldung kann nicht durch eine Einteilung überdeckt werden.", "Un concediu medical deschis nu poate fi acoperit de o alocare.");
        Add("AbsenceMissing", "Отсутствие не найдено.", "Absence not found.", "Abwesenheit nicht gefunden.", "Absența nu a fost găsită.");
        Add("SourceEmpty", "В источнике нет назначений. Целевые дни не изменены.", "The source has no assignments. Target days were not changed.", "Die Quelle hat keine Einteilungen. Zieltage wurden nicht geändert.", "Sursa nu are alocări. Zilele destinație nu au fost modificate.");
        Add("SourceEmptyDays", "В источнике нет назначений для выбранных дней. Целевые дни не изменены.", "The source has no assignments for the selected days. Target days were not changed.", "Die Quelle hat für die gewählten Tage keine Einteilungen. Zieltage wurden nicht geändert.", "Sursa nu are alocări pentru zilele alese. Zilele destinație nu au fost modificate.");
        Add("NoDaysToWrite", "Нет дней для записи.", "There are no days to write.", "Es gibt keine Tage zum Schreiben.", "Nu există zile de scris.");
        Add("TwoSitesOneDay", "В одном наборе у сотрудника больше одного объекта на одну дату.", "In one batch an employee has more than one site on the same date.", "In einem Satz hat ein Mitarbeiter mehr als eine Baustelle am selben Tag.", "Într-un singur set un angajat are mai mult de un șantier în aceeași dată.");
        Add("EmployeeMissingId", "Сотрудник #{0} не найден.", "Employee #{0} was not found.", "Mitarbeiter #{0} wurde nicht gefunden.", "Angajatul #{0} nu a fost găsit.");
        Add("EmployeeArchivedAssign", "Сотрудник в архиве: {0}. Новые назначения недоступны, пока сотрудник не возвращён из архива.", "Employee is archived: {0}. New assignments are unavailable until the employee is restored.", "Mitarbeiter im Archiv: {0}. Neue Einteilungen sind nicht möglich, bis der Mitarbeiter zurückgeholt wird.", "Angajat în arhivă: {0}. Alocările noi nu sunt posibile până la scoaterea din arhivă.");
        Add("SiteMissingId", "Объект #{0} не найден.", "Site #{0} was not found.", "Baustelle #{0} wurde nicht gefunden.", "Șantierul #{0} nu a fost găsit.");
        Add("SiteArchivedAssign", "Объект в архиве: {0}. Назначение невозможно, пока объект не возвращён из архива.", "Site is archived: {0}. Assignment is impossible until the site is restored.", "Baustelle im Archiv: {0}. Einteilung ist nicht möglich, bis die Baustelle zurückgeholt wird.", "Șantier în arhivă: {0}. Alocarea nu este posibilă până la scoaterea din arhivă.");
        Add("OpenSickBlocks", "Открытый больничный у {0} с {1}. Новое назначение невозможно, пока не указана дата окончания.", "Open sick leave for {0} since {1}. A new assignment is impossible until an end date is set.", "Laufende Krankmeldung bei {0} seit {1}. Eine neue Einteilung ist nicht möglich, bis ein Enddatum gesetzt ist.", "Concediu medical deschis pentru {0} din {1}. O alocare nouă nu este posibilă până la setarea datei de sfârșit.");
        Add("OpenEndOnlySick", "Дату окончания можно не указывать только у больничного.", "Only sick leave may omit the end date.", "Nur eine Krankmeldung darf ohne Enddatum bleiben.", "Doar concediul medical poate rămâne fără dată de sfârșit.");
        Add("EmployeeMissing", "Сотрудник не найден.", "Employee not found.", "Mitarbeiter nicht gefunden.", "Angajatul nu a fost găsit.");
        Add("SiteMissing", "Объект не найден.", "Site not found.", "Baustelle nicht gefunden.", "Șantierul nu a fost găsit.");
        Add("EmployeeArchivedAbsence", "Сотрудник в архиве: {0}. Отсутствие нельзя записать, пока сотрудник не возвращён из архива.", "Employee is archived: {0}. An absence cannot be saved until the employee is restored.", "Mitarbeiter im Archiv: {0}. Eine Abwesenheit kann nicht gespeichert werden, bis der Mitarbeiter zurückgeholt wird.", "Angajat în arhivă: {0}. O absență nu poate fi salvată până la scoaterea din arhivă.");
        Add("OtherNoteRequired", "Для отсутствия «Другое» нужно пояснение.", "Other absence needs a note.", "Sonstiges braucht einen Hinweis.", "Altul are nevoie de o observație.");
        Add("OtherNoteTooLong", "Пояснение для «Другое» не длиннее 50 символов.", "The note for Other is at most 50 characters.", "Der Hinweis für Sonstiges hat höchstens 50 Zeichen.", "Observația pentru Altul are cel mult 50 de caractere.");
        Add("UnknownKind", "Неизвестный вид отсутствия: {0}", "Unknown absence type: {0}", "Unbekannte Abwesenheitsart: {0}", "Tip de absență necunoscut: {0}");
        Add("DuplicateSiteName", "Активный объект с таким названием уже есть.", "An active site with this name already exists.", "Eine aktive Baustelle mit diesem Namen gibt es bereits.", "Există deja un șantier activ cu acest nume.");
        Add("SiteWord", "объект", "site", "Baustelle", "șantier");
        Add("EmployeeWord", "Сотрудник", "Employee", "Mitarbeiter", "Angajat");
        Add("AssignmentWillClear", "Назначение: {0}, {1} — будет снято", "Assignment: {0}, {1} — will be removed", "Einteilung: {0}, {1} — wird entfernt", "Alocare: {0}, {1} — va fi eliminată");
        Add("AbsenceWillDelete", "{0}, {1} — будет удалено", "{0}, {1} — will be deleted", "{0}, {1} — wird gelöscht", "{0}, {1} — va fi ștearsă");
        Add("AbsenceEndBecomes", "{0}, {1} — окончание станет {2}", "{0}, {1} — the end becomes {2}", "{0}, {1} — Ende wird {2}", "{0}, {1} — sfârșitul devine {2}");
        Add("SiteAssignmentWillClear", "{0}, {1} — назначение на «{2}» будет снято", "{0}, {1} — the assignment to «{2}» will be removed", "{0}, {1} — die Einteilung auf «{2}» wird entfernt", "{0}, {1} — alocarea pe «{2}» va fi eliminată");

        Add("DaySheet", "Табель на {0}", "Roster for {0}", "Einsatzplan für {0}", "Pontaj pentru {0}");
        Add("AssignedCount", "Назначено: {0}", "Assigned: {0}", "Eingeteilt: {0}", "Alocați: {0}");
        Add("AddressLine", "Адрес: {0}", "Address: {0}", "Adresse: {0}", "Adresă: {0}");
        Add("WeekSheet", "Неделя {0} — {1}", "Week {0} — {1}", "Woche {0} — {1}", "Săptămâna {0} — {1}");
        Add("UnassignedLine", "Нераспределённые: {0}", "Unassigned: {0}", "Nicht eingeteilt: {0}", "Nealocați: {0}");
        Add("AbsencesLine", "Отсутствия: {0}", "Absences: {0}", "Abwesenheiten: {0}", "Absențe: {0}");
        Add("PrintJobDay", "Табель за день", "Day roster", "Tagesplan", "Pontaj zilnic");
        Add("PrintJobWeek", "Табель за неделю", "Week roster", "Wochenplan", "Pontaj săptămânal");
        Add("NoAssignmentsShort", "нет назначений", "no assignments", "keine Einteilungen", "nicio alocare");
        Add("RestoreConfirm", "Текущие данные будут заменены. Перед заменой будет создана страховочная копия.", "Current data will be replaced. A safety copy is made before the replacement.", "Die aktuellen Daten werden ersetzt. Vorher wird eine Sicherheitskopie angelegt.", "Datele curente vor fi înlocuite. Înainte se face o copie de siguranță.");
        Add("RestoreTitle", "Восстановление", "Restore", "Wiederherstellen", "Restaurare");
        Add("CsvFilter", "CSV (*.csv)|*.csv", "CSV (*.csv)|*.csv", "CSV (*.csv)|*.csv", "CSV (*.csv)|*.csv");
        Add("SqliteFilter", "База SQLite (*.sqlite)|*.sqlite", "SQLite database (*.sqlite)|*.sqlite", "SQLite-Datenbank (*.sqlite)|*.sqlite", "Bază SQLite (*.sqlite)|*.sqlite");
        Add("SqliteOrAll", "База SQLite (*.sqlite)|*.sqlite|Все файлы (*.*)|*.*", "SQLite database (*.sqlite)|*.sqlite|All files (*.*)|*.*", "SQLite-Datenbank (*.sqlite)|*.sqlite|Alle Dateien (*.*)|*.*", "Bază SQLite (*.sqlite)|*.sqlite|Toate fișierele (*.*)|*.*");
        Add("CsvFilePrefix", "табель", "roster", "einsatz", "pontaj");
        Add("BackupFilePrefix", "backup", "backup", "sicherung", "copie");
        Add("LangRu", "Русский", "Русский", "Русский", "Русский");
        Add("LangEn", "English", "English", "English", "English");
        Add("LangDe", "Deutsch", "Deutsch", "Deutsch", "Deutsch");
        Add("LangRo", "Română", "Română", "Română", "Română");
        Add("JumpDate", "Перейти к дате", "Go to date", "Zu Datum", "La dată");

        Add("Planning", "Планирование", "Planning", "Einsatzplanung", "Planificare");
        Add("EmployeeFormHint", "Укажите фамилию и имя. Остальные сведения необязательны.", "Enter a first and last name. Other details are optional.", "Vor- und Nachnamen angeben. Weitere Angaben sind optional.", "Introduceți numele și prenumele. Celelalte date sunt opționale.");
        Add("AssignedPeople", "Назначены", "Assigned", "Eingeteilt", "Alocați");
        Add("OverviewDate", "Сводка на {0} · Данные сохраняются на этом компьютере", "Overview for {0} · Data is saved on this computer", "Übersicht für {0} · Daten werden auf diesem Computer gespeichert", "Situație la {0} · Datele se salvează pe acest computer");
        Add("PlanningHint", "Люди, объекты и назначения — в одном месте.", "People, sites and assignments in one place.", "Mitarbeiter, Baustellen und Einteilungen im Überblick.", "Angajați, șantiere și alocări într-un singur loc.");
        Add("QuickCreate", "БЫСТРОЕ ДОБАВЛЕНИЕ", "QUICK ADD", "SCHNELL HINZUFÜGEN", "ADĂUGARE RAPIDĂ");
        Add("LocalWorkspace", "Работа офлайн", "Offline workspace", "Offline arbeiten", "Lucru offline");
        Add("LocalWorkspaceHint", "Данные сохраняются на этом компьютере.", "Data is saved on this computer.", "Daten werden auf diesem Computer gespeichert.", "Datele se salvează pe acest computer.");
        Add("AssignPeople", "Назначить сотрудников", "Assign employees", "Mitarbeiter einteilen", "Alocă angajați");
        Add("ActiveTeam", "В активном составе", "Active team members", "Aktive Mitarbeiter", "Angajați activi");
        Add("FindEmployee", "Найти сотрудника", "Find an employee", "Mitarbeiter suchen", "Caută un angajat");
        Add("SearchEmployeeHint", "Фамилия, имя или специальность · Ctrl+F", "Name or specialty · Ctrl+F", "Name oder Beruf · Strg+F", "Nume sau specialitate · Ctrl+F");
        Add("ResetFilters", "Сбросить", "Reset", "Zurücksetzen", "Resetează");
        Add("MoreActions", "Ещё ···", "More ···", "Mehr ···", "Mai mult ···");
        Add("PreviousPeriod", "Предыдущий период", "Previous period", "Vorheriger Zeitraum", "Perioada anterioară");
        Add("NextPeriod", "Следующий период", "Next period", "Nächster Zeitraum", "Perioada următoare");
        Add("SiteBoardTitle", "Объекты на день", "Sites for the day", "Baustellen am Tag", "Șantierele zilei");
        Add("EmployeeBoardTitle", "Календарь сотрудников", "Employee calendar", "Mitarbeiterkalender", "Calendarul angajaților");
        Add("CreateSite", "Создать объект", "Create site", "Baustelle anlegen", "Creează șantier");
        Add("CreateSiteHint", "Для начала достаточно названия. Остальное можно добавить позже.", "Start with a name. You can add the rest later.", "Ein Name genügt. Weitere Angaben können Sie später ergänzen.", "Pentru început este suficient un nume. Restul poate fi adăugat ulterior.");
        Add("EditSiteHint", "Сведения об объекте можно изменить в любой момент.", "You can update site details at any time.", "Sie können die Angaben jederzeit ändern.", "Datele șantierului pot fi modificate oricând.");
        Add("SiteNameExample", "Например: Жилой комплекс «Северный»", "For example: Riverside apartments", "Zum Beispiel: Wohnanlage Nord", "De exemplu: Complexul Rezidențial Nord");
        Add("OptionalAddress", "Адрес (необязательно)", "Address (optional)", "Adresse (optional)", "Adresă (opțional)");
        Add("SiteExtraDetails", "Контакты и заметка (необязательно)", "Contact details and note (optional)", "Kontakt und Notiz (optional)", "Contact și notă (opțional)");
        Add("AssignAfterCreate", "Сразу назначить сотрудников после создания", "Assign employees after creating", "Anschließend Mitarbeiter einteilen", "Alocă angajați după creare");
        Add("SiteNameRequired", "Введите название объекта.", "Enter a site name.", "Geben Sie einen Baustellennamen ein.", "Introduceți numele șantierului.");
        Add("SiteCreated", "Объект «{0}» создан.", "Site “{0}” created.", "Baustelle „{0}“ angelegt.", "Șantierul „{0}” a fost creat.");
        Add("SiteDirectoryHint", "Создавайте объекты, меняйте сведения и назначайте сотрудников.", "Create sites, edit details and assign employees.", "Baustellen anlegen, Angaben bearbeiten und Mitarbeiter einteilen.", "Creați șantiere, modificați datele și alocați angajați.");
        Add("SearchSites", "Поиск по названию или адресу", "Search by name or address", "Nach Name oder Adresse suchen", "Caută după nume sau adresă");
        Add("NoSitesFound", "Объекты не найдены. Измените поиск или создайте новый объект.", "No sites found. Change the search or create a site.", "Keine Baustellen gefunden. Suche ändern oder Baustelle anlegen.", "Nu s-au găsit șantiere. Modificați căutarea sau creați un șantier.");
        Add("GettingStarted", "Начните с первого объекта", "Start with your first site", "Mit der ersten Baustelle starten", "Începeți cu primul șantier");
        Add("GettingStartedHint", "Добавьте объект и сотрудников, затем назначьте их на нужные даты.", "Add a site and employees, then assign them to the dates you need.", "Baustelle und Mitarbeiter anlegen, dann die gewünschten Tage einteilen.", "Adăugați un șantier și angajați, apoi alocați-i pe datele dorite.");
        Add("NoEmployeesToAssign", "Добавьте сотрудников, чтобы назначить их на объект.", "Add employees to assign them to a site.", "Legen Sie Mitarbeiter an, um sie einzuteilen.", "Adăugați angajați pentru a-i aloca unui șantier.");
        Add("StepSite", "1. Объект", "1. Site", "1. Baustelle", "1. Șantier");
        Add("StepPeople", "2. Сотрудники", "2. Employees", "2. Mitarbeiter", "2. Angajați");
        Add("StepDates", "3. Даты", "3. Dates", "3. Tage", "3. Date");
        Add("SelectionKept", "Выбранные сотрудники остаются отмечены при поиске.", "Selected employees stay checked while searching.", "Ausgewählte Mitarbeiter bleiben bei der Suche markiert.", "Angajații selectați rămân bifați în timpul căutării.");

        Add("WorkHours", "Рабочее время", "Working time", "Arbeitszeit", "Timp de lucru");
        Add("HoursUnit", "ч", "h", "Std.", "ore");
        Add("StepTime", "4. Рабочее время и обед", "4. Work and lunch times", "4. Arbeitszeit und Mittagspause", "4. Program și pauză de masă");
        Add("TrackTime", "Указать время для выбранных дней", "Set times for the selected days", "Zeiten für die gewählten Tage erfassen", "Indică programul pentru zilele alese");
        Add("WorkStart", "Начало работы", "Work starts", "Arbeitsbeginn", "Începutul lucrului");
        Add("WorkEnd", "Конец работы", "Work ends", "Arbeitsende", "Sfârșitul lucrului");
        Add("Lunch", "Обед", "Lunch break", "Mittagspause", "Pauză de masă");
        Add("LunchFrom", "Обед с", "Lunch from", "Pause von", "Pauză de la");
        Add("LunchTo", "Обед до", "Lunch until", "Pause bis", "Pauză până la");
        Add("HasLunch", "Есть обеденный перерыв", "Include a lunch break", "Mit Mittagspause", "Include pauză de masă");
        Add("NextDayEnd", "Конец работы на следующий день", "Work ends the next day", "Arbeitsende am Folgetag", "Lucrul se termină a doua zi");
        Add("TimeNotSet", "Время не указано", "Time not set", "Zeit fehlt", "Program necompletat");
        Add("TimeFormatHint", "Введите время в формате 08:00.", "Use the 08:00 time format.", "Uhrzeit im Format 08:00 eingeben.", "Introduceți ora în formatul 08:00.");
        Add("InvalidWorkRange", "Конец должен быть позже начала. Для ночной смены отметьте следующий день. Длительность — не более 24 часов.", "End must follow start. For overnight work, select next day. Maximum duration is 24 hours.", "Das Ende muss nach dem Beginn liegen. Für Nachtschichten Folgetag wählen. Höchstens 24 Stunden.", "Sfârșitul trebuie să urmeze începutului. Pentru ture de noapte selectați ziua următoare. Maxim 24 de ore.");
        Add("LunchBothEnds", "Укажите начало и конец обеда.", "Set both lunch start and end.", "Beginn und Ende der Pause angeben.", "Indicați începutul și sfârșitul pauzei.");
        Add("LunchOutsideWork", "Обед должен находиться внутри рабочего времени и быть короче рабочего дня.", "Lunch must be within working time and shorter than the shift.", "Die Pause muss innerhalb der Arbeitszeit liegen und kürzer als die Schicht sein.", "Pauza trebuie să fie în timpul programului și mai scurtă decât tura.");
        Add("NetTimeSummary", "За день: {0} ч · обед: {1} мин", "Per day: {0} h · lunch: {1} min", "Pro Tag: {0} Std. · Pause: {1} Min.", "Pe zi: {0} ore · pauză: {1} min");
        Add("HoursReport", "Табель за месяц", "Monthly hours", "Monatsübersicht", "Pontaj lunar");
        Add("HoursReportHint", "Часы = конец − начало − обед по сохранённым назначениям. Отсутствия в часы не входят. Ночная смена относится к дню её начала.", "Hours = end − start − lunch from saved assignments. Absences are excluded. Overnight shifts belong to their start date.", "Stunden = Ende − Beginn − Pause aus gespeicherten Einteilungen. Abwesenheiten zählen nicht als Arbeitszeit. Nachtschichten zählen zum Startdatum.", "Ore = sfârșit − început − pauză din alocările salvate. Absențele nu se includ. Turele de noapte aparțin datei de început.");
        Add("AssignedDays", "Дней на объектах", "Assigned days", "Einsatztage", "Zile alocate");
        Add("TimedDays", "Дней с временем", "Days with times", "Tage mit Zeiten", "Zile cu program");
        Add("MissingTimeDays", "Без времени", "Missing times", "Zeiten fehlen", "Fără program");
        Add("TotalHours", "Часы (ч:мин)", "Hours (h:mm)", "Stunden (h:mm)", "Ore (h:mm)");
        Add("ReportTotal", "Всего учтено: {0} ч · назначений без времени: {1}", "Recorded total: {0} h · assignments missing times: {1}", "Erfasst: {0} Std. · Einteilungen ohne Zeit: {1}", "Total înregistrat: {0} ore · alocări fără program: {1}");
        Add("AbsenceDaysHint", "Отпуск и больничный показаны в календарных днях. Часы без указанного времени не рассчитаны.", "Leave is shown in calendar days. Assignments without times are excluded from hours.", "Abwesenheiten in Kalendertagen. Einteilungen ohne Zeiten sind nicht in den Stunden enthalten.", "Absențele sunt în zile calendaristice. Alocările fără program nu intră în ore.");
        Add("ReportDetails", "Назначения выбранного сотрудника", "Selected employee’s assignments", "Einteilungen des gewählten Mitarbeiters", "Alocările angajatului selectat");
        Add("ExportDetails", "Экспорт по дням", "Export daily detail", "Tagesdaten exportieren", "Export pe zile");
        Add("HoursCsvHeader", "месяц;фамилия;имя;специальность;дней на объектах;дней с временем;дней без времени;часы (ч:мин);часы (десятичные);отпуск (дни);больничный (дни);неоплачиваемый (дни);другое (дни)", "month;last name;first name;specialty;assigned days;timed days;missing times;hours (h:mm);decimal hours;vacation days;sick days;unpaid days;other days", "Monat;Nachname;Vorname;Beruf;Einsatztage;Tage mit Zeiten;Tage ohne Zeiten;Stunden (h:mm);Dezimalstunden;Urlaubstage;Krankheitstage;unbezahlte Tage;sonstige Tage", "lună;nume;prenume;specialitate;zile alocate;zile cu program;zile fără program;ore (h:mm);ore zecimale;zile concediu;zile medical;zile fără plată;alte zile");
        Add("HoursDetailsCsvHeader", "дата;фамилия;имя;объект;начало;конец;конец на следующий день;обед с;обед до;часы (ч:мин);примечание", "date;last name;first name;site;start;end;ends next day;lunch start;lunch end;hours (h:mm);note", "Datum;Nachname;Vorname;Baustelle;Beginn;Ende;Ende am Folgetag;Pause von;Pause bis;Stunden (h:mm);Hinweis", "data;nume;prenume;șantier;început;sfârșit;sfârșit a doua zi;pauză de la;pauză până la;ore (h:mm);notă");
        Add("CalendarEntry", "Запись в календаре", "Calendar entry", "Kalendereintrag", "Înregistrare în calendar");
        Add("WorkDay", "Рабочий день на объекте", "Work day at a site", "Arbeitstag auf Baustelle", "Zi de lucru pe șantier");
        Add("CalendarEntryHint", "Укажите вид записи и даты включительно. Замена существующих записей потребует подтверждения.", "Choose an entry type and inclusive dates. Replacing existing entries requires confirmation.", "Art und Zeitraum einschließlich beider Tage wählen. Ersetzen vorhandener Einträge erfordert Bestätigung.", "Alegeți tipul și perioada, inclusiv ambele date. Înlocuirea necesită confirmare.");
        Add("CalendarHelp", "Двойной щелчок по дню — работа, отпуск или больничный. Правая кнопка — быстрый выбор.", "Double-click a day for work, vacation or sick leave. Right-click for quick actions.", "Doppelklick auf einen Tag: Arbeit, Urlaub oder Krankheit. Rechtsklick: Schnellauswahl.", "Dublu clic pe o zi: lucru, concediu sau medical. Clic dreapta: alegere rapidă.");
        Add("EditWorkDay", "Назначение и время…", "Assignment and time…", "Einteilung und Zeit…", "Alocare și program…");
        Add("LeaveRegister", "Отпуска и больничные", "Leave and sickness", "Urlaub und Krankheit", "Concedii și medicale");
        Add("AllEmployeeAbsences", "Все отсутствия сотрудника…", "All employee absences…", "Alle Abwesenheiten…", "Toate absențele angajatului…");
        Add("CurrentEntry", "Сейчас: {0}", "Current entry: {0}", "Aktuell: {0}", "În prezent: {0}");
        Add("ChooseCalendarCell", "Выберите день сотрудника в календаре.", "Select an employee’s calendar day.", "Wählen Sie einen Tag im Mitarbeiterkalender.", "Selectați o zi în calendarul angajatului.");

        return table;
    }
}
