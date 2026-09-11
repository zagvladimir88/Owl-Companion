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

public enum EasAttendeeStatus
{
    Unknown = 0,
    Tentative = 2,
    Accepted = 3,
    Declined = 4,
    NotResponded = 5
}

public enum EasAttendeeType
{
    Unknown = 0,
    Required = 1,
    Optional = 2,
    Resource = 3
}

public enum EasResponseType
{
    None = 0,
    Organizer = 1,
    Tentative = 2,
    Accepted = 3,
    Declined = 4,
    NotResponded = 5
}

public enum EasBusyStatus
{
    Free = 0,
    Tentative = 1,
    Busy = 2,
    OutOfOffice = 3,
    WorkingElsewhere = 4
}

public sealed record EasAttendee
{
    public string? Name { get; init; }

    public string? Email { get; init; }

    public EasAttendeeStatus Status { get; init; }

    public EasAttendeeType Type { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Email ?? "(без имени)" : Name;
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

    public IReadOnlyList<EasAttendee> Attendees { get; init; } = Array.Empty<EasAttendee>();

    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();

    public EasBusyStatus? BusyStatus { get; init; }

    public EasResponseType? ResponseType { get; init; }

    public bool ResponseRequested { get; init; }

    public DateTimeOffset? AppointmentReplyTime { get; init; }

    public int? Sensitivity { get; init; }

    public DateTimeOffset? DtStamp { get; init; }

    public string? TimeZoneRaw { get; init; }

    public bool IsMeeting => (MeetingStatus & 1) != 0;

    public bool IsOrganizer => IsMeeting && (MeetingStatus & 2) == 0;

    public bool IsCancelled => (MeetingStatus & 4) != 0;

    public bool NeedsResponse =>
        IsMeeting &&
        !IsOrganizer &&
        !IsCancelled &&
        ResponseType is null or EasResponseType.None or EasResponseType.NotResponded;

    public string DisplaySubject => string.IsNullOrWhiteSpace(Subject) ? "(без темы)" : Subject;
}