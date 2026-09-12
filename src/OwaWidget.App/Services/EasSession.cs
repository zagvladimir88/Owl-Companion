using OwaWidget.Eas;
using OwaWidget.Eas.Commands;
using OwaWidget.Eas.Models;
using OwaWidget.Eas.Parsers;
using OwaWidget.Eas.Recurrence;

namespace OwaWidget.App.Services;

public enum SessionStatus
{
    Connecting,
    Syncing,
    Watching,
    Reconnecting,
    AuthenticationFailed,
    Stopped
}

public sealed class EasSession : IDisposable
{
    private const int MaxPages = 40;
    private const int MaxRetainedMessages = 200;

    private readonly AppSettings _settings;
    private readonly AppState _state;
    private readonly List<EasMessage> _messages = new();
    private readonly List<EasAppointment> _appointments = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _mailSyncLock = new(1, 1);
    private readonly SemaphoreSlim _calendarSyncLock = new(1, 1);

    private static readonly TimeSpan BaselineTimeout = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan DeltaTimeout = TimeSpan.FromSeconds(60);

    private EasClient? _client;
    private bool _baselineDone;
    private string? _lastReported;

    public EasSession(AppSettings settings, AppState state)
    {
        _settings = settings;
        _state = state;
    }

    public event Action<IReadOnlyList<EasMessage>>? MailArrived;

    public event Action? MailChanged;

    public event Action<IReadOnlyList<EasOccurrence>>? CalendarChanged;

    public event Action<SessionStatus, string>? StatusChanged;

    public IReadOnlyList<EasMessage> Messages
    {
        get
        {
            lock (_gate)
            {
                return _messages.OrderByDescending(m => m.DateReceived ?? DateTimeOffset.MinValue).ToList();
            }
        }
    }

    public int UnreadCount
    {
        get
        {
            lock (_gate)
            {
                return _messages.Count(m => !m.IsRead);
            }
        }
    }

    public async Task<string?> FetchBodyAsync(
        string collectionId,
        string serverId,
        CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            return null;
        }

        try
        {
            var result = await RunWithProvisioningAsync(
                () => ItemOperationsCommand.FetchAsync(Client, collectionId, serverId, 32768, cancellationToken),
                cancellationToken);

            if (result.Status != 1 || result.Properties is null)
            {
                Log.Info($"fetch body: status={result.Status}, no properties");
                return null;
            }

            var body = result.Properties.Child("Body")?.ChildText("Data")
                       ?? result.Properties.ChildText("Body");

            if (string.IsNullOrEmpty(body))
            {
                return null;
            }

            lock (_gate)
            {
                var index = _messages.FindIndex(m => m.ServerId == serverId);
                if (index >= 0)
                {
                    _messages[index] = _messages[index] with { Body = body };
                }

                var appointment = _appointments.FindIndex(a => a.ServerId == serverId);
                if (appointment >= 0)
                {
                    _appointments[appointment] = _appointments[appointment] with { Body = body };
                }
            }

            return body;
        }
        catch (Exception exception)
        {
            Log.Error("fetch body failed", exception);
            return null;
        }
    }

    public async Task<bool> RespondToMeetingAsync(
        string serverId,
        DateTimeOffset? instanceStart,
        MeetingUserResponse response,
        bool fromInbox,
        CancellationToken cancellationToken = default)
    {
        var collectionId = fromInbox ? _state.InboxId : _state.CalendarId;

        if (_client is null || collectionId is null)
        {
            return false;
        }

        try
        {
            var result = await RunWithProvisioningAsync(
                () => MeetingResponseCommand.ExecuteAsync(Client, new MeetingResponseRequest
                {
                    CollectionId = collectionId,
                    RequestId = serverId,
                    Response = response,
                    InstanceId = instanceStart
                }, cancellationToken),
                cancellationToken);

            Log.Info($"meeting response {response} accepted, calendarId={result.CalendarId ?? "-"}");

            await DrainCalendarAsync(cancellationToken);
            await DrainMailAsync(notify: false, cancellationToken);

            return true;
        }
        catch (Exception exception)
        {
            Log.Error($"meeting response {response} failed", exception);
            return false;
        }
    }

    public void PrimeFromCache()
    {
        var cachedMail = CacheStore.LoadMail(_state);
        var cachedAppointments = CacheStore.LoadCalendar(_state);
        var resync = false;

        if (cachedMail is null && _state.MailSyncKey != "0")
        {
            Log.Info("no usable mail cache while sync key is advanced, forcing full resync");
            _state.MailSyncKey = "0";
            resync = true;
        }

        if (cachedAppointments is null && _state.CalendarSyncKey != "0")
        {
            Log.Info("no usable calendar cache while sync key is advanced, forcing full resync");
            _state.CalendarSyncKey = "0";
            resync = true;
        }

        if (resync)
        {
            AppStorage.Save(_state);
        }

        int messages;
        int appointments;

        lock (_gate)
        {
            _messages.Clear();
            if (cachedMail is not null)
            {
                _messages.AddRange(cachedMail);
            }

            _appointments.Clear();
            if (cachedAppointments is not null)
            {
                _appointments.AddRange(cachedAppointments);
            }

            messages = _messages.Count;
            appointments = _appointments.Count;
        }

        Log.Info($"cache primed: messages={messages}, appointments={appointments}");

        if (messages == 0 && appointments == 0)
        {
            return;
        }

        if (messages > 0)
        {
            MailChanged?.Invoke();
        }

        if (appointments > 0)
        {
            CalendarChanged?.Invoke(UpcomingOccurrences());
        }
    }

    public void ClearCache()
    {
        lock (_gate)
        {
            _messages.Clear();
            _appointments.Clear();
        }

        CacheStore.Clear();
        _state.MailSyncKey = "0";
        _state.CalendarSyncKey = "0";
        _baselineDone = false;
        AppStorage.Save(_state);

        MailChanged?.Invoke();
        CalendarChanged?.Invoke(Array.Empty<EasOccurrence>());
    }

    public IReadOnlyList<EasOccurrence> UpcomingOccurrences(int days = 7)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.Now;
            return RecurrenceExpander.ExpandAll(_appointments, now.AddHours(-2), now.AddDays(days));
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(15);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Report(SessionStatus.Connecting, "Подключение…");
                Connect();

                await EnsureProvisionedAsync(cancellationToken);
                await SynchronizeFoldersAsync(cancellationToken);

                Report(SessionStatus.Syncing, "Синхронизация…");
                await BaselineAsync(cancellationToken);

                backoff = TimeSpan.FromSeconds(15);
                await PingLoopAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (EasHttpException exception) when (exception.IsAuthFailure)
            {
                Log.Error("auth failed, session stopped", exception);
                Report(SessionStatus.AuthenticationFailed, "Неверный логин или пароль");
                return;
            }
            catch (EasStatusException exception) when (exception.InvalidSyncKey)
            {
                Log.Error("invalid sync key, resetting", exception);
                ResetSyncKeys();
                Report(SessionStatus.Reconnecting, "Ключи синхронизации сброшены, повтор");
            }
            catch (Exception exception)
            {
                Log.Error($"session failed, retrying in {backoff.TotalSeconds:F0}s", exception);
                Report(SessionStatus.Reconnecting, Describe(exception));
                await Task.Delay(backoff, cancellationToken);
                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 300));
            }
        }

        Report(SessionStatus.Stopped, "Остановлено");
    }

    public async Task MarkReadAsync(string serverId, CancellationToken cancellationToken = default)
    {
        if (_client is null || _state.InboxId is null)
        {
            return;
        }

        bool alreadyRead;
        lock (_gate)
        {
            alreadyRead = _messages.Any(m => m.ServerId == serverId && m.IsRead);
        }

        if (alreadyRead)
        {
            return;
        }

        await _mailSyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await RunWithProvisioningAsync(
                () => SyncCommand.ExecuteAsync(Client, new SyncRequest
                {
                    CollectionId = _state.InboxId,
                    SyncKey = _state.MailSyncKey,
                    WindowSize = 50,
                    FilterType = _settings.MailFilterType,
                    BodyType = 1,
                    BodyTruncationSize = 6000,
                    ClientChanges = new[] { new SyncFlagChange(serverId, true) }
                }, cancellationToken),
                cancellationToken);

            _state.MailSyncKey = result.SyncKey;

            lock (_gate)
            {
                var index = _messages.FindIndex(m => m.ServerId == serverId);
                if (index >= 0)
                {
                    _messages[index] = _messages[index] with { IsRead = true };
                }
            }

            Log.Info("mark read: sent to server");
            MailChanged?.Invoke();
        }
        catch (Exception exception)
        {
            Log.Error("mark read failed", exception);
        }
        finally
        {
            _mailSyncLock.Release();
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
        _mailSyncLock.Dispose();
        _calendarSyncLock.Dispose();
    }

    private void Connect()
    {
        var credentials = CredentialStore.TryLoad(_settings.Server)
            ?? throw new EasException("Пароль не сохранён.");

        _client?.Dispose();
        _client = new EasClient(new EasOptions
        {
            ServerUri = new Uri(_settings.Server),
            User = credentials.User,
            Password = credentials.Password,
            DeviceId = _state.DeviceId,
            DeviceType = "OwaWidget",
            ProtocolVersion = "14.1",
            UserAgent = "OwaWidget/1.0"
        })
        {
            PolicyKey = _state.PolicyKey
        };
    }

    private async Task EnsureProvisionedAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_state.PolicyKey))
        {
            return;
        }

        var options = new EasOptions
        {
            ServerUri = new Uri(_settings.Server),
            User = _settings.User,
            Password = string.Empty,
            DeviceId = _state.DeviceId
        };

        _state.PolicyKey = await ProvisionCommand.AcquirePolicyKeyAsync(Client, options, cancellationToken);
        AppStorage.Save(_state);
    }

    private async Task SynchronizeFoldersAsync(CancellationToken cancellationToken)
    {
        if (_state.InboxId is not null && _state.CalendarId is not null && _state.FolderSyncKey != "0")
        {
            return;
        }

        var folders = await RunWithProvisioningAsync(
            () => FolderSyncCommand.ExecuteAsync(Client, "0", cancellationToken),
            cancellationToken);

        _state.FolderSyncKey = folders.SyncKey;
        _state.InboxId = folders.Changed.FirstOrDefault(f => f.Type == EasFolderType.Inbox)?.ServerId;
        _state.CalendarId = folders.Changed.FirstOrDefault(f => f.Type == EasFolderType.Calendar)?.ServerId;

        if (_state.InboxId is null)
        {
            throw new EasException("Папка Входящие не найдена.");
        }

        AppStorage.Save(_state);
    }

    private async Task BaselineAsync(CancellationToken cancellationToken)
    {
        var freshMail = _state.MailSyncKey == "0";
        if (freshMail)
        {
            _state.MailSyncKey = await RunWithProvisioningAsync(
                () => SyncCommand.InitializeAsync(Client, _state.InboxId!, cancellationToken),
                cancellationToken);
        }

        await DrainMailAsync(notify: !freshMail && _baselineDone, cancellationToken);

        if (_state.CalendarId is not null)
        {
            if (_state.CalendarSyncKey == "0")
            {
                _state.CalendarSyncKey = await RunWithProvisioningAsync(
                    () => SyncCommand.InitializeAsync(Client, _state.CalendarId, cancellationToken),
                    cancellationToken);
            }

            await DrainCalendarAsync(cancellationToken);
        }

        _baselineDone = true;
        AppStorage.Save(_state);

        PersistMailCache();
        PersistCalendarCache();
    }

    private async Task PingLoopAsync(CancellationToken cancellationToken)
    {
        var watched = new List<PingFolder> { new(_state.InboxId!, PingCommand.EmailClass) };
        if (_state.CalendarId is not null)
        {
            watched.Add(new PingFolder(_state.CalendarId, PingCommand.CalendarClass));
        }

        var heartbeat = PingCommand.DefaultHeartbeatSeconds;

        while (!cancellationToken.IsCancellationRequested)
        {
            Report(SessionStatus.Watching, "Подключено");

            var ping = await RunWithProvisioningAsync(
                () => PingCommand.ExecuteAsync(Client, watched, heartbeat, cancellationToken),
                cancellationToken);

            switch (ping.Kind)
            {
                case PingStatus.Expired:
                    continue;

                case PingStatus.Changes:
                    foreach (var folderId in ping.ChangedFolders)
                    {
                        if (folderId == _state.InboxId)
                        {
                            await DrainMailAsync(notify: true, cancellationToken);
                        }
                        else if (folderId == _state.CalendarId)
                        {
                            await DrainCalendarAsync(cancellationToken);
                        }
                    }

                    AppStorage.Save(_state);
                    continue;

                case PingStatus.InvalidHeartbeat:
                    heartbeat = ping.SuggestedHeartbeat ?? 300;
                    continue;

                case PingStatus.FolderHierarchyOutOfDate:
                    _state.FolderSyncKey = "0";
                    await SynchronizeFoldersAsync(cancellationToken);
                    continue;

                default:
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    continue;
            }
        }
    }

    private async Task DrainMailAsync(bool notify, CancellationToken cancellationToken)
    {
        await _mailSyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await DrainMailCoreAsync(notify, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mailSyncLock.Release();
        }
    }

    private async Task DrainMailCoreAsync(bool notify, CancellationToken cancellationToken)
    {
        var arrived = new List<EasMessage>();
        var pages = 0;
        var more = true;
        var touched = false;
        var baseline = !_baselineDone;

        while (more && pages < MaxPages)
        {
            var page = await RunWithTimeoutRetryAsync(
                "mail",
                timeout => RunWithProvisioningAsync(
                    () => SyncCommand.ExecuteAsync(Client, new SyncRequest
                    {
                        CollectionId = _state.InboxId!,
                        SyncKey = _state.MailSyncKey,
                        WindowSize = baseline ? 25 : 50,
                        FilterType = _settings.MailFilterType,
                        BodyType = 1,
                        BodyTruncationSize = baseline ? 1024 : 6000,
                        Timeout = timeout
                    }, cancellationToken),
                    cancellationToken),
                baseline,
                cancellationToken);

            _state.MailSyncKey = page.SyncKey;
            more = page.MoreAvailable;
            pages++;

            foreach (var change in page.Changes)
            {
                touched = true;

                switch (change.Type)
                {
                    case SyncChangeType.Add when change.ApplicationData is not null:
                        var message = EmailParser.Parse(change.ServerId, _state.InboxId!, change.ApplicationData);
                        if (message.IsEmpty)
                        {
                            break;
                        }

                        lock (_gate)
                        {
                            _messages.RemoveAll(m => m.ServerId == message.ServerId);
                            _messages.Add(message);
                        }

                        arrived.Add(message);
                        break;

                    case SyncChangeType.Change when change.ApplicationData is not null:
                        var updated = EmailParser.Parse(change.ServerId, _state.InboxId!, change.ApplicationData);
                        lock (_gate)
                        {
                            _messages.RemoveAll(m => m.ServerId == updated.ServerId);
                            _messages.Add(updated);
                        }

                        break;

                    case SyncChangeType.Delete:
                    case SyncChangeType.SoftDelete:
                        lock (_gate)
                        {
                            _messages.RemoveAll(m => m.ServerId == change.ServerId);
                        }

                        break;
                }
            }
        }

        lock (_gate)
        {
            if (_messages.Count > MaxRetainedMessages)
            {
                var extra = _messages
                    .OrderByDescending(m => m.DateReceived ?? DateTimeOffset.MinValue)
                    .Skip(MaxRetainedMessages)
                    .ToList();

                foreach (var message in extra)
                {
                    _messages.Remove(message);
                }
            }
        }

        if (touched)
        {
            PersistMailCache();
            MailChanged?.Invoke();
        }

        if (notify && arrived.Count > 0)
        {
            var unread = arrived.Where(m => !m.IsRead).ToList();
            Log.Info($"mail delta: arrived={arrived.Count}, unread={unread.Count}, notify={notify}");

            if (unread.Count > 0)
            {
                MailArrived?.Invoke(unread);
            }
        }
        else if (arrived.Count > 0)
        {
            Log.Info($"mail baseline: arrived={arrived.Count}, notify suppressed");
        }
    }

    private async Task DrainCalendarAsync(CancellationToken cancellationToken)
    {
        await _calendarSyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await DrainCalendarCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _calendarSyncLock.Release();
        }
    }

    private async Task DrainCalendarCoreAsync(CancellationToken cancellationToken)
    {
        var pages = 0;
        var more = true;
        var touched = false;
        var baseline = !_baselineDone;

        while (more && pages < MaxPages)
        {
            var page = await RunWithTimeoutRetryAsync(
                "calendar",
                timeout => RunWithProvisioningAsync(
                    () => SyncCommand.ExecuteAsync(Client, new SyncRequest
                    {
                        CollectionId = _state.CalendarId!,
                        SyncKey = _state.CalendarSyncKey,
                        WindowSize = 50,
                        FilterType = _settings.CalendarFilterType,
                        BodyType = 1,
                        BodyTruncationSize = 4096,
                        Timeout = timeout
                    }, cancellationToken),
                    cancellationToken),
                baseline,
                cancellationToken);

            _state.CalendarSyncKey = page.SyncKey;
            more = page.MoreAvailable;
            pages++;

            foreach (var change in page.Changes)
            {
                touched = true;

                lock (_gate)
                {
                    _appointments.RemoveAll(a => a.ServerId == change.ServerId);

                    if (change.ApplicationData is not null &&
                        change.Type is SyncChangeType.Add or SyncChangeType.Change)
                    {
                        _appointments.Add(CalendarParser.Parse(change.ServerId, change.ApplicationData));
                    }
                }
            }
        }

        if (touched)
        {
            PersistCalendarCache();
            CalendarChanged?.Invoke(UpcomingOccurrences());
        }
    }

    private void PersistMailCache()
    {
        List<EasMessage> snapshot;

        lock (_gate)
        {
            snapshot = _messages.ToList();
        }

        CacheStore.SaveMail(_state, snapshot);
    }

    private void PersistCalendarCache()
    {
        List<EasAppointment> snapshot;

        lock (_gate)
        {
            var pruned = CacheStore.Prune(_appointments);
            if (pruned.Count != _appointments.Count)
            {
                _appointments.Clear();
                _appointments.AddRange(pruned);
            }

            snapshot = _appointments.ToList();
        }

        CacheStore.SaveCalendar(_state, snapshot);
    }

    private async Task<T> RunWithTimeoutRetryAsync<T>(
        string label,
        Func<TimeSpan, Task<T>> action,
        bool baseline,
        CancellationToken cancellationToken)
    {
        var timeout = baseline ? BaselineTimeout : DeltaTimeout;

        try
        {
            return await action(timeout).ConfigureAwait(false);
        }
        catch (EasTimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extended = timeout + timeout;
            Log.Info($"{label} sync timed out after {timeout.TotalSeconds:F0}s, retrying with {extended.TotalSeconds:F0}s");

            return await action(extended).ConfigureAwait(false);
        }
    }

    private async Task<T> RunWithProvisioningAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (EasHttpException exception) when (exception.NeedsProvisioning)
        {
            await ReprovisionAsync(cancellationToken);
            return await action();
        }
        catch (EasStatusException exception) when (exception.NeedsProvisioning)
        {
            await ReprovisionAsync(cancellationToken);
            return await action();
        }
    }

    private async Task ReprovisionAsync(CancellationToken cancellationToken)
    {
        var options = new EasOptions
        {
            ServerUri = new Uri(_settings.Server),
            User = _settings.User,
            Password = string.Empty,
            DeviceId = _state.DeviceId
        };

        _state.PolicyKey = await ProvisionCommand.AcquirePolicyKeyAsync(Client, options, cancellationToken);
        AppStorage.Save(_state);
    }

    private void ResetSyncKeys()
    {
        _state.MailSyncKey = "0";
        _state.CalendarSyncKey = "0";
        _baselineDone = false;
        AppStorage.Save(_state);
    }

    private static string Describe(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case EasTimeoutException:
                    return "Сервер не ответил вовремя";
                case System.Net.Sockets.SocketException:
                    return "Сервер недоступен — проверьте VPN";
                case System.Security.Authentication.AuthenticationException:
                    return "Не удалось установить защищённое соединение";
                case EasHttpException http:
                    return $"Сервер ответил {(int)http.StatusCode}";
                case EasStatusException status:
                    return $"Ошибка синхронизации {status.Status}";
            }

            if (current is System.IO.IOException)
            {
                return "Соединение разорвано — проверьте VPN";
            }
        }

        if (exception is System.Net.Http.HttpRequestException)
        {
            return "Не удалось установить защищённое соединение";
        }

        return "Нет связи с сервером";
    }

    private void Report(SessionStatus status, string message)
    {
        if (_lastReported != $"{status}|{message}")
        {
            _lastReported = $"{status}|{message}";
            Log.Info($"status {status}: {message}");
        }

        StatusChanged?.Invoke(status, message);
    }

    private EasClient Client => _client ?? throw new EasException("Клиент не инициализирован.");
}