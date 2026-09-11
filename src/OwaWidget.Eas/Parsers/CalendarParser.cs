using System.Text.RegularExpressions;
using OwaWidget.Eas.Models;
using OwaWidget.Eas.Wbxml;

namespace OwaWidget.Eas.Parsers;

public static partial class CalendarParser
{
    public static EasAppointment Parse(string serverId, WbxmlElement data)
    {
        var body = data.Child("Body")?.ChildText("Data");

        return new EasAppointment
        {
            ServerId = serverId,
            Uid = data.ChildText("UID"),
            Subject = data.ChildText("Subject"),
            Location = data.ChildText("Location"),
            Start = EmailParser.ParseDate(data.ChildText("StartTime")),
            End = EmailParser.ParseDate(data.ChildText("EndTime")),
            AllDay = data.ChildText("AllDayEvent") == "1",
            OrganizerName = data.ChildText("Organizer_Name"),
            OrganizerEmail = data.ChildText("Organizer_Email"),
            ReminderMinutes = data.ChildInt("Reminder"),
            MeetingStatus = data.ChildInt("MeetingStatus") ?? 0,
            Body = body,
            OnlineMeetingLink = ExtractMeetingLink(data, body),
            Recurrence = ParseRecurrence(data.Child("Recurrence")),
            Exceptions = ParseExceptions(data.Child("Exceptions")),
            Attendees = ParseAttendees(data.Child("Attendees")),
            Categories = ParseCategories(data.Child("Categories")),
            BusyStatus = data.ChildInt("BusyStatus") is { } busy ? (EasBusyStatus)busy : null,
            ResponseType = data.ChildInt("ResponseType") is { } response ? (EasResponseType)response : null,
            ResponseRequested = data.ChildText("ResponseRequested") == "1",
            AppointmentReplyTime = EmailParser.ParseDate(data.ChildText("AppointmentReplyTime")),
            Sensitivity = data.ChildInt("Sensitivity"),
            DtStamp = EmailParser.ParseDate(data.ChildText("DtStamp")),
            TimeZoneRaw = data.ChildText("TimeZone")
        };
    }

    private static IReadOnlyList<EasAttendee> ParseAttendees(WbxmlElement? element)
    {
        if (element is null)
        {
            return Array.Empty<EasAttendee>();
        }

        var result = new List<EasAttendee>();

        foreach (var attendee in element.ChildrenNamed("Attendee"))
        {
            var name = attendee.ChildText("Attendee_Name");
            var email = attendee.ChildText("Attendee_Email");

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(email))
            {
                continue;
            }

            result.Add(new EasAttendee
            {
                Name = name,
                Email = email,
                Status = (EasAttendeeStatus)(attendee.ChildInt("Attendee_Status") ?? 0),
                Type = (EasAttendeeType)(attendee.ChildInt("Attendee_Type") ?? 0)
            });
        }

        return result;
    }

    private static IReadOnlyList<string> ParseCategories(WbxmlElement? element)
    {
        if (element is null)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();

        foreach (var category in element.ChildrenNamed("Category"))
        {
            if (!string.IsNullOrWhiteSpace(category.Text))
            {
                result.Add(category.Text.Trim());
            }
        }

        return result;
    }

    public static string? ExtractMeetingLink(WbxmlElement data, string? body)
    {
        var direct = data.ChildText("OnlineMeetingExternalLink") ?? data.ChildText("OnlineMeetingConfLink");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return Clean(direct);
        }

        foreach (var source in new[] { data.ChildText("Location"), body })
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var trimmed = source.Trim();

            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var firstSpace = trimmed.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
                return Clean(firstSpace > 0 ? trimmed[..firstSpace] : trimmed);
            }

            var match = MeetingLink().Match(trimmed);
            if (match.Success)
            {
                return Clean(match.Value);
            }
        }

        return null;
    }

    private static string Clean(string value)
    {
        return value.Trim().TrimEnd('.', ',', ')', '>', ']', ';');
    }

    private static EasRecurrence? ParseRecurrence(WbxmlElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return new EasRecurrence
        {
            Type = (EasRecurrenceType)(element.ChildInt("Recurrence_Type") ?? 0),
            Interval = element.ChildInt("Recurrence_Interval") ?? 1,
            Until = EmailParser.ParseDate(element.ChildText("Recurrence_Until")),
            Occurrences = element.ChildInt("Recurrence_Occurrences"),
            DayOfWeek = element.ChildInt("Recurrence_DayOfWeek"),
            DayOfMonth = element.ChildInt("Recurrence_DayOfMonth"),
            WeekOfMonth = element.ChildInt("Recurrence_WeekOfMonth"),
            MonthOfYear = element.ChildInt("Recurrence_MonthOfYear")
        };
    }

    private static IReadOnlyList<EasAppointmentException> ParseExceptions(WbxmlElement? element)
    {
        if (element is null)
        {
            return Array.Empty<EasAppointmentException>();
        }

        var result = new List<EasAppointmentException>();

        foreach (var exception in element.ChildrenNamed("Exception"))
        {
            result.Add(new EasAppointmentException
            {
                ExceptionStartTime = EmailParser.ParseDate(exception.ChildText("Exception_StartTime")),
                Deleted = exception.ChildText("Exception_Deleted") == "1",
                Start = EmailParser.ParseDate(exception.ChildText("StartTime")),
                End = EmailParser.ParseDate(exception.ChildText("EndTime")),
                Subject = exception.ChildText("Subject"),
                Location = exception.ChildText("Location")
            });
        }

        return result;
    }

    [GeneratedRegex(
        @"https?://(?:[\w.-]*(?:ktalk\.ru|teams\.microsoft\.com|vinteo[\w.-]*|zoom\.us|meet\.google\.com|webinar\.ru|telemost\.yandex\.ru)|[\w.-]*\.alfaintra\.net/(?:call|meet|conf)[\w./?=&%#-]*)[\w./?=&%#-]*",
        RegexOptions.IgnoreCase)]
    private static partial Regex MeetingLink();
}