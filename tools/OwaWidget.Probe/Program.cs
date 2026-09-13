using System.Globalization;
using System.Text;
using OwaWidget.Eas;
using OwaWidget.Eas.Commands;
using OwaWidget.Eas.Models;
using OwaWidget.Eas.Parsers;
using OwaWidget.Eas.Recurrence;

Console.OutputEncoding = Encoding.UTF8;

var server = Environment.GetEnvironmentVariable("OWA_SERVER") is { Length: > 0 } configured
    ? configured
    : Prompt("Сервер (https://owa.example.com): ");
var user = args.Length > 0 ? args[0] : Prompt("Логин (домен\\имя): ");
var password = ReadPassword("Пароль: ");

var deviceId = LoadOrCreateDeviceId();

var options = new EasOptions
{
    ServerUri = new Uri(server),
    User = user,
    Password = password,
    DeviceId = deviceId,
    DeviceType = "OwaWidget",
    ProtocolVersion = "14.1",
    UserAgent = "OwaWidget/1.0"
};

Console.WriteLine();
Console.WriteLine($"Сервер   : {server}");
Console.WriteLine($"Логин    : {user}");
Console.WriteLine($"DeviceId : {deviceId}");
Console.WriteLine(new string('-', 70));

using var client = new EasClient(options);

try
{
    Console.WriteLine("[1/3] OPTIONS...");
    var versions = await client.GetSupportedVersionsAsync();
    Console.WriteLine($"      версии протокола: {string.Join(", ", versions)}");

    Console.WriteLine("[2/3] Provision...");
    var policyKey = await ProvisionCommand.AcquirePolicyKeyAsync(client, options);
    Console.WriteLine($"      PolicyKey: {policyKey}");

    Console.WriteLine("[3/4] FolderSync...");
    var folders = await FolderSyncCommand.ExecuteAsync(client);
    Console.WriteLine($"      SyncKey: {folders.SyncKey}, папок: {folders.Changed.Count}");
    Console.WriteLine();

    foreach (var folder in folders.Changed.OrderBy(f => (int)f.Type).ThenBy(f => f.DisplayName))
    {
        var mark = folder.IsCalendar ? "[КАЛЕНДАРЬ]" : folder.IsMail ? "[ПОЧТА]" : "          ";
        Console.WriteLine($"{mark} {(int)folder.Type,3} {folder.Type,-20} id={folder.ServerId,-6} {folder.DisplayName}");
    }

    var inbox = folders.Changed.FirstOrDefault(f => f.Type == EasFolderType.Inbox);
    if (inbox is null)
    {
        Console.WriteLine();
        Console.WriteLine("Папка Входящие не найдена, чтение почты пропущено.");
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"[4/4] Sync почты из папки {inbox.DisplayName} (id={inbox.ServerId})...");

    var collectionId = inbox.ServerId;

    Func<string, SyncRequest> mailRequest = key => new SyncRequest
    {
        CollectionId = collectionId,
        SyncKey = key,
        WindowSize = 50,
        FilterType = 3,
        BodyType = 1,
        BodyTruncationSize = 400
    };

    var syncKey = await SyncCommand.InitializeAsync(client, collectionId);
    Console.WriteLine($"      начальный SyncKey: {syncKey}");
    Console.Write("      вычитываю базовую линию");

    var messages = new List<EasMessage>();
    var pages = 0;

    while (true)
    {
        var page = await SyncCommand.ExecuteAsync(client, mailRequest(syncKey));
        syncKey = page.SyncKey;
        pages++;
        Console.Write('.');

        foreach (var change in page.Changes)
        {
            if (change.Type == SyncChangeType.Add && change.ApplicationData is not null)
            {
                messages.Add(EmailParser.Parse(change.ServerId, collectionId, change.ApplicationData));
            }
        }

        if (!page.MoreAvailable || pages >= 40)
        {
            break;
        }
    }

    Console.WriteLine();
    Console.WriteLine($"      базовая линия: {messages.Count} писем за неделю, страниц: {pages}");
    Console.WriteLine($"      следующий SyncKey: {syncKey}");
    Console.WriteLine();

    messages = messages
        .OrderByDescending(message => message.DateReceived ?? DateTimeOffset.MinValue)
        .Take(15)
        .ToList();

    foreach (var message in messages)
    {
        var when = message.DateReceived?.ToLocalTime().ToString("dd.MM HH:mm") ?? "  --  ";
        var unread = message.IsRead ? " " : "•";
        Console.WriteLine($"{unread} {when}  {Truncate(message.DisplaySender, 30),-30}  {Truncate(message.DisplaySubject, 60)}");

        if (message.Preview is not null)
        {
            Console.WriteLine($"                 {Truncate(message.Preview, 88)}");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Непрочитанных среди показанных — {messages.Count(m => !m.IsRead)}.");
    Console.WriteLine(new string('-', 70));

    var calendarFolder = folders.Changed.FirstOrDefault(f => f.Type == EasFolderType.Calendar);
    var calendarId = calendarFolder?.ServerId ?? string.Empty;
    var calendarSyncKey = "0";
    var appointments = new List<EasAppointment>();

    Func<string, SyncRequest> calendarRequest = key => new SyncRequest
    {
        CollectionId = calendarId,
        SyncKey = key,
        WindowSize = 50,
        FilterType = 5,
        BodyType = 1,
        BodyTruncationSize = 1024
    };

    if (calendarFolder is not null)
    {
        Console.Write($"Календарь (id={calendarId}): вычитываю базовую линию");
        calendarSyncKey = await SyncCommand.InitializeAsync(client, calendarId);

        var calendarPages = 0;
        while (true)
        {
            var page = await SyncCommand.ExecuteAsync(client, calendarRequest(calendarSyncKey));
            calendarSyncKey = page.SyncKey;
            calendarPages++;
            Console.Write('.');

            foreach (var change in page.Changes)
            {
                if (change.Type == SyncChangeType.Add && change.ApplicationData is not null)
                {
                    appointments.Add(CalendarParser.Parse(change.ServerId, change.ApplicationData));
                }
            }

            if (!page.MoreAvailable || calendarPages >= 40)
            {
                break;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"      событий: {appointments.Count}, страниц: {calendarPages}");
        Console.WriteLine();

        var russian = CultureInfo.GetCultureInfo("ru-RU");
        var now = DateTimeOffset.Now;
        var horizon = now.AddDays(7);

        var occurrences = RecurrenceExpander.ExpandAll(appointments, now, horizon);
        var seriesCount = appointments.Count(a => a.Recurrence is not null);

        Console.WriteLine($"Встречи на 7 дней вперёд: {occurrences.Count} (серий в календаре: {seriesCount}, из них раскрыто вхождений: {occurrences.Count(o => o.IsRecurring)})");
        Console.WriteLine();

        foreach (var occurrence in occurrences.Take(25))
        {
            var mark = occurrence.IsException ? "*" : occurrence.IsRecurring ? "~" : " ";
            var when = occurrence.Start.ToLocalTime().ToString("ddd dd.MM HH:mm", russian);
            var place = string.IsNullOrWhiteSpace(occurrence.Location) ? "—" : Truncate(occurrence.Location, 24);
            var alarm = occurrence.Start.AddMinutes(-5).ToLocalTime().ToString("HH:mm");

            Console.WriteLine($"{mark} {when}  {Truncate(occurrence.Subject, 40),-40}  напомню в {alarm}  {place}");

            if (occurrence.OnlineMeetingLink is not null)
            {
                Console.WriteLine($"                 {Truncate(occurrence.OnlineMeetingLink, 74)}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("(~ — вхождение повторяющейся серии, * — перенесённое вхождение)");

        Console.WriteLine();
        Console.WriteLine(new string('-', 70));
    }
    Console.WriteLine("Режим ожидания push. Отправьте себе письмо — оно должно появиться");
    Console.WriteLine("здесь за несколько секунд. Ctrl+C — выход.");
    Console.WriteLine();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cts.Cancel();
    };

    var watched = new List<PingFolder> { new(collectionId, PingCommand.EmailClass) };
    if (calendarFolder is not null)
    {
        watched.Add(new PingFolder(calendarId, PingCommand.CalendarClass));
    }

    var heartbeat = PingCommand.DefaultHeartbeatSeconds;

    while (!cts.IsCancellationRequested)
    {
        PingResult ping;
        try
        {
            ping = await PingCommand.ExecuteAsync(client, watched, heartbeat, cts.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }

        var stamp = DateTime.Now.ToString("HH:mm:ss");

        switch (ping.Kind)
        {
            case PingStatus.Expired:
                Console.WriteLine($"[{stamp}] тишина ({heartbeat} с), соединение переоткрыто");
                break;

            case PingStatus.Changes:
                foreach (var folderId in ping.ChangedFolders)
                {
                    if (folderId == calendarId && calendarFolder is not null)
                    {
                        var moreCalendar = true;
                        while (moreCalendar)
                        {
                            var delta = await SyncCommand.ExecuteAsync(client, calendarRequest(calendarSyncKey), cts.Token);
                            calendarSyncKey = delta.SyncKey;
                            moreCalendar = delta.MoreAvailable;

                            foreach (var change in delta.Changes)
                            {
                                if (change.ApplicationData is null)
                                {
                                    Console.WriteLine($"[{stamp}] встреча удалена: {change.ServerId}");
                                    continue;
                                }

                                var appointment = CalendarParser.Parse(change.ServerId, change.ApplicationData);
                                var when = appointment.Start?.ToLocalTime().ToString("dd.MM HH:mm") ?? "??";
                                var verb = change.Type == SyncChangeType.Add ? "НОВАЯ ВСТРЕЧА" : "ВСТРЕЧА ИЗМЕНЕНА";
                                Console.WriteLine($"[{stamp}] {verb}: {when} — {appointment.DisplaySubject}");
                            }
                        }

                        continue;
                    }

                    if (folderId != collectionId)
                    {
                        Console.WriteLine($"[{stamp}] изменения в папке {folderId}, пропускаю");
                        continue;
                    }

                    var more = true;
                    while (more)
                    {
                        var delta = await SyncCommand.ExecuteAsync(client, mailRequest(syncKey), cts.Token);
                        syncKey = delta.SyncKey;
                        more = delta.MoreAvailable;

                        foreach (var change in delta.Changes)
                        {
                            switch (change.Type)
                            {
                                case SyncChangeType.Add when change.ApplicationData is not null:
                                    var incoming = EmailParser.Parse(change.ServerId, collectionId, change.ApplicationData);
                                    Console.WriteLine($"[{stamp}] НОВОЕ ПИСЬМО: {incoming.DisplaySender} — {incoming.DisplaySubject}");
                                    if (incoming.Preview is not null)
                                    {
                                        Console.WriteLine($"           {Truncate(incoming.Preview, 88)}");
                                    }

                                    break;

                                case SyncChangeType.Delete:
                                case SyncChangeType.SoftDelete:
                                    Console.WriteLine($"[{stamp}] письмо удалено: {change.ServerId}");
                                    break;

                                case SyncChangeType.Change:
                                    Console.WriteLine($"[{stamp}] письмо изменено (прочитано/помечено): {change.ServerId}");
                                    break;
                            }
                        }
                    }
                }

                break;

            case PingStatus.InvalidHeartbeat:
                heartbeat = ping.SuggestedHeartbeat ?? 300;
                Console.WriteLine($"[{stamp}] сервер требует heartbeat {heartbeat} с, подстраиваюсь");
                break;

            case PingStatus.FolderHierarchyOutOfDate:
                Console.WriteLine($"[{stamp}] иерархия папок изменилась, нужен FolderSync");
                break;

            case PingStatus.TooManyFolders:
                Console.WriteLine($"[{stamp}] слишком много папок, предел {ping.MaxFolders}");
                break;

            default:
                Console.WriteLine($"[{stamp}] Ping вернул статус {ping.Status}, пауза 30 с");
                await Task.Delay(TimeSpan.FromSeconds(30), cts.Token);
                break;
        }
    }

    Console.WriteLine();
    Console.WriteLine("ГОТОВО: выход из режима ожидания.");
}
catch (EasHttpException ex)
{
    Console.WriteLine();
    Console.WriteLine($"ОШИБКА HTTP {(int)ex.StatusCode} на команде {ex.Command}: {ex.Message}");
    if (ex.IsAuthFailure)
    {
        Console.WriteLine("Проверьте логин и пароль. Повторных попыток не делаем, чтобы не заблокировать учётку.");
    }
}
catch (EasStatusException ex)
{
    Console.WriteLine();
    Console.WriteLine($"ОШИБКА протокола: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine($"ОШИБКА: {ex.GetType().Name}: {ex.Message}");
}

return;

static string Truncate(string value, int maxLength)
{
    return value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}

static string Prompt(string label)
{
    Console.Write(label);
    return Console.ReadLine()?.Trim() ?? string.Empty;
}

static string ReadPassword(string label)
{
    Console.Write(label);
    var builder = new StringBuilder();

    while (true)
    {
        var key = Console.ReadKey(intercept: true);

        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return builder.ToString();
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (builder.Length > 0)
            {
                builder.Length--;
                Console.Write("\b \b");
            }

            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            builder.Append(key.KeyChar);
            Console.Write('*');
        }
    }
}

static string LoadOrCreateDeviceId()
{
    var directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OwaWidget");
    var path = Path.Combine(directory, "device-id.txt");

    if (File.Exists(path))
    {
        var existing = File.ReadAllText(path).Trim();
        if (existing.Length > 0)
        {
            return existing;
        }
    }

    Directory.CreateDirectory(directory);
    var created = EasOptions.NewDeviceId();
    File.WriteAllText(path, created);
    return created;
}