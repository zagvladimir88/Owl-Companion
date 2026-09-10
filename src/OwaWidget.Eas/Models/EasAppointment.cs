namespace OwaWidget.Eas.Models;

public enum EasRecurrenceType
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    MonthlyByDayOfWeek = 3,
    Yearly = 5,
    YearlyByDayOfWeek = 6
}

public sealed record EasRecurrence
{
    public EasRecurrenceType Type { get; init; }

    public int Interval { get; init; } = 1;

    public DateTimeOffset? Until { get; init; }

    public int? Occurrences { get; init; }

    public int? DayOfWeek { get; init; }

    public int? DayOfMonth { get; init; }

    public int? WeekOfMonth { get; init; }

    public int? MonthOfYear { get; init; }
}

public sealed record EasAppointmentException
{
    public DateTimeOffset? ExceptionStartTime { get; init; }

    public bool Deleted { get; init; }

    public DateTimeOffset? Start { get; init; }

    public DateTimeOffset? End { get; init; }

    public string? Subject { get; init; }

    public string? Location { get; init; }
}

public sealed record EasAppointment
{
    public required string ServerId { get; init; }

    public string? Uid { get; init; }

    public string? Subject { get; init; }

    public string? Location { get; init; }

    public DateTimeOffset? Start { get; init; }

    public DateTimeOffset? End { get; init; }

    public bool AllDay { get; init; }

    public string? OrganizerName { get; init; }

    public string? OrganizerEmail { get; init; }

    public int? ReminderMinutes { get; init; }

    public int MeetingStatus { get; init; }

    public string? Body { get; init; }

    public string? OnlineMeetingLink { get; init; }

    public EasRecurrence? Recurrence { get; init; }

    public IReadOnlyList<EasAppointmentException> Exceptions { get; init; } = Array.Empty<EasAppointmentException>();

    public bool IsCancelled => MeetingStatus is 5 or 7;

    public string DisplaySubject => string.IsNullOrWhiteSpace(Subject) ? "(без темы)" : Subject;
}