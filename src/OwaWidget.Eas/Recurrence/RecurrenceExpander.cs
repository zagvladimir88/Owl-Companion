using OwaWidget.Eas.Models;

namespace OwaWidget.Eas.Recurrence;

public sealed record EasOccurrence
{
    public required string ServerId { get; init; }

    public required DateTimeOffset Start { get; init; }

    public required DateTimeOffset End { get; init; }

    public required string Subject { get; init; }

    public string? Location { get; init; }

    public string? OnlineMeetingLink { get; init; }

    public int? ReminderMinutes { get; init; }

    public bool IsRecurring { get; init; }

    public bool IsException { get; init; }
}

public static class RecurrenceExpander
{
    private const int MaxIterations = 10000;

    public static IReadOnlyList<EasOccurrence> Expand(
        EasAppointment appointment,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        if (appointment.Start is null || appointment.IsCancelled)
        {
            return Array.Empty<EasOccurrence>();
        }

        var duration = (appointment.End ?? appointment.Start.Value) - appointment.Start.Value;
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (appointment.Recurrence is null)
        {
            var single = Create(appointment, appointment.Start.Value, duration, recurring: false, isException: false);
            return Overlaps(single, from, to) ? new[] { single } : Array.Empty<EasOccurrence>();
        }

        return ExpandSeries(appointment, duration, from, to);
    }

    public static IReadOnlyList<EasOccurrence> ExpandAll(
        IEnumerable<EasAppointment> appointments,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var result = new List<EasOccurrence>();

        foreach (var appointment in appointments)
        {
            result.AddRange(Expand(appointment, from, to));
        }

        return result.OrderBy(occurrence => occurrence.Start).ToList();
    }

    private static IReadOnlyList<EasOccurrence> ExpandSeries(
        EasAppointment appointment,
        TimeSpan duration,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var recurrence = appointment.Recurrence!;
        var seriesStart = appointment.Start!.Value.ToLocalTime();
        var timeOfDay = seriesStart.TimeOfDay;

        var deleted = new HashSet<DateTime>();
        var overrides = new Dictionary<DateTime, EasAppointmentException>();

        foreach (var exception in appointment.Exceptions)
        {
            if (exception.ExceptionStartTime is null)
            {
                continue;
            }

            var key = Normalize(exception.ExceptionStartTime.Value);

            if (exception.Deleted)
            {
                deleted.Add(key);
            }
            else
            {
                overrides[key] = exception;
            }
        }

        var until = recurrence.Until?.ToLocalTime();
        var occurrenceLimit = recurrence.Occurrences;
        var result = new List<EasOccurrence>();
        var emitted = 0;

        foreach (var date in GenerateDates(recurrence, seriesStart.Date, timeOfDay))
        {
            if (emitted >= MaxIterations)
            {
                break;
            }

            var localStart = date + timeOfDay;
            var start = new DateTimeOffset(localStart, TimeZoneInfo.Local.GetUtcOffset(localStart));

            if (until is not null && start > until.Value)
            {
                break;
            }

            emitted++;

            if (occurrenceLimit is not null && emitted > occurrenceLimit.Value)
            {
                break;
            }

            if (start > to)
            {
                break;
            }

            var key = Normalize(start);

            if (deleted.Contains(key))
            {
                continue;
            }

            var occurrence = overrides.TryGetValue(key, out var replacement)
                ? CreateFromException(appointment, replacement, start, duration)
                : Create(appointment, start, duration, recurring: true, isException: false);

            if (Overlaps(occurrence, from, to))
            {
                result.Add(occurrence);
            }
        }

        return result;
    }

    private static IEnumerable<DateTime> GenerateDates(EasRecurrence recurrence, DateTime seriesStartDate, TimeSpan timeOfDay)
    {
        var interval = Math.Max(1, recurrence.Interval);

        switch (recurrence.Type)
        {
            case EasRecurrenceType.Daily:
                for (var date = seriesStartDate; ; date = date.AddDays(interval))
                {
                    yield return date;
                }

            case EasRecurrenceType.Weekly:
                var mask = recurrence.DayOfWeek ?? ToMask(seriesStartDate.DayOfWeek);
                var weekStart = seriesStartDate.AddDays(-(int)seriesStartDate.DayOfWeek);

                for (var week = weekStart; ; week = week.AddDays(7 * interval))
                {
                    for (var offset = 0; offset < 7; offset++)
                    {
                        var candidate = week.AddDays(offset);
                        if (candidate >= seriesStartDate && (mask & ToMask(candidate.DayOfWeek)) != 0)
                        {
                            yield return candidate;
                        }
                    }
                }

            case EasRecurrenceType.Monthly:
                var dayOfMonth = recurrence.DayOfMonth ?? seriesStartDate.Day;

                for (var month = new DateTime(seriesStartDate.Year, seriesStartDate.Month, 1); ; month = month.AddMonths(interval))
                {
                    var day = Math.Min(dayOfMonth, DateTime.DaysInMonth(month.Year, month.Month));
                    var candidate = new DateTime(month.Year, month.Month, day);

                    if (candidate >= seriesStartDate)
                    {
                        yield return candidate;
                    }
                }

            case EasRecurrenceType.MonthlyByDayOfWeek:
                for (var month = new DateTime(seriesStartDate.Year, seriesStartDate.Month, 1); ; month = month.AddMonths(interval))
                {
                    var candidate = NthWeekday(month, recurrence.DayOfWeek ?? ToMask(seriesStartDate.DayOfWeek), recurrence.WeekOfMonth ?? 1);

                    if (candidate is not null && candidate.Value >= seriesStartDate)
                    {
                        yield return candidate.Value;
                    }
                }

            case EasRecurrenceType.Yearly:
                var month0 = recurrence.MonthOfYear ?? seriesStartDate.Month;
                var day0 = recurrence.DayOfMonth ?? seriesStartDate.Day;

                for (var year = seriesStartDate.Year; ; year += interval)
                {
                    var day = Math.Min(day0, DateTime.DaysInMonth(year, month0));
                    var candidate = new DateTime(year, month0, day);

                    if (candidate >= seriesStartDate)
                    {
                        yield return candidate;
                    }
                }

            case EasRecurrenceType.YearlyByDayOfWeek:
                var yearMonth = recurrence.MonthOfYear ?? seriesStartDate.Month;

                for (var year = seriesStartDate.Year; ; year += interval)
                {
                    var candidate = NthWeekday(
                        new DateTime(year, yearMonth, 1),
                        recurrence.DayOfWeek ?? ToMask(seriesStartDate.DayOfWeek),
                        recurrence.WeekOfMonth ?? 1);

                    if (candidate is not null && candidate.Value >= seriesStartDate)
                    {
                        yield return candidate.Value;
                    }
                }

            default:
                yield return seriesStartDate;
                break;
        }
    }

    private static DateTime? NthWeekday(DateTime monthStart, int dayMask, int weekOfMonth)
    {
        var days = new List<DateTime>();
        var daysInMonth = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);

        for (var day = 1; day <= daysInMonth; day++)
        {
            var candidate = new DateTime(monthStart.Year, monthStart.Month, day);
            if ((dayMask & ToMask(candidate.DayOfWeek)) != 0)
            {
                days.Add(candidate);
            }
        }

        if (days.Count == 0)
        {
            return null;
        }

        return weekOfMonth >= 5 ? days[^1] : days[Math.Min(weekOfMonth - 1, days.Count - 1)];
    }

    private static int ToMask(DayOfWeek dayOfWeek)
    {
        return 1 << (int)dayOfWeek;
    }

    private static DateTime Normalize(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
    }

    private static bool Overlaps(EasOccurrence occurrence, DateTimeOffset from, DateTimeOffset to)
    {
        return occurrence.End >= from && occurrence.Start <= to;
    }

    private static EasOccurrence Create(
        EasAppointment appointment,
        DateTimeOffset start,
        TimeSpan duration,
        bool recurring,
        bool isException)
    {
        return new EasOccurrence
        {
            ServerId = appointment.ServerId,
            Start = start,
            End = start + duration,
            Subject = appointment.DisplaySubject,
            Location = appointment.Location,
            OnlineMeetingLink = appointment.OnlineMeetingLink,
            ReminderMinutes = appointment.ReminderMinutes,
            IsRecurring = recurring,
            IsException = isException
        };
    }

    private static EasOccurrence CreateFromException(
        EasAppointment appointment,
        EasAppointmentException exception,
        DateTimeOffset originalStart,
        TimeSpan duration)
    {
        var start = exception.Start ?? originalStart;
        var end = exception.End ?? start + duration;

        return new EasOccurrence
        {
            ServerId = appointment.ServerId,
            Start = start,
            End = end,
            Subject = string.IsNullOrWhiteSpace(exception.Subject) ? appointment.DisplaySubject : exception.Subject,
            Location = string.IsNullOrWhiteSpace(exception.Location) ? appointment.Location : exception.Location,
            OnlineMeetingLink = appointment.OnlineMeetingLink,
            ReminderMinutes = appointment.ReminderMinutes,
            IsRecurring = true,
            IsException = true
        };
    }
}