using System.Windows.Threading;
using OwaWidget.Eas.Recurrence;

namespace OwaWidget.App.Services;

public sealed class ReminderScheduler : IDisposable
{
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<string> _fired = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private IReadOnlyList<EasOccurrence> _occurrences = Array.Empty<EasOccurrence>();

    public ReminderScheduler(AppSettings settings)
    {
        _settings = settings;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        _timer.Tick += (_, _) => Tick();
    }

    public event Action<EasOccurrence, int>? ReminderDue;

    public void Start()
    {
        _timer.Start();
        Tick();
    }

    public void Update(IReadOnlyList<EasOccurrence> occurrences)
    {
        lock (_gate)
        {
            _occurrences = occurrences;

            var live = new HashSet<string>(occurrences.Select(Key), StringComparer.Ordinal);
            _fired.RemoveWhere(key => !live.Contains(key));
        }

        Tick();
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        var due = new List<(EasOccurrence Occurrence, int Minutes)>();

        lock (_gate)
        {
            foreach (var occurrence in _occurrences)
            {
                var lead = LeadMinutes(occurrence);
                var fireAt = occurrence.Start.AddMinutes(-lead);
                var key = Key(occurrence);

                if (now < fireAt || now >= occurrence.Start || _fired.Contains(key))
                {
                    continue;
                }

                _fired.Add(key);
                due.Add((occurrence, (int)Math.Round((occurrence.Start - now).TotalMinutes)));
            }
        }

        foreach (var (occurrence, minutes) in due)
        {
            ReminderDue?.Invoke(occurrence, Math.Max(minutes, 1));
        }
    }

    private int LeadMinutes(EasOccurrence occurrence)
    {
        if (_settings.RespectAppointmentReminder &&
            occurrence.ReminderMinutes is > 0 and <= 180)
        {
            return occurrence.ReminderMinutes.Value;
        }

        return Math.Max(1, _settings.ReminderMinutes);
    }

    private static string Key(EasOccurrence occurrence)
    {
        return $"{occurrence.ServerId}|{occurrence.Start.UtcDateTime:yyyyMMddHHmm}";
    }
}