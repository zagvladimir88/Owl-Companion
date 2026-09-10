namespace OwaWidget.Eas.Models;

public sealed record EasMessage
{
    public required string ServerId { get; init; }

    public required string CollectionId { get; init; }

    public string? From { get; init; }

    public string? FromName { get; init; }

    public string? FromAddress { get; init; }

    public string? Subject { get; init; }

    public DateTimeOffset? DateReceived { get; init; }

    public bool IsRead { get; init; }

    public string? Preview { get; init; }

    public string? Body { get; init; }

    public string? ThreadTopic { get; init; }

    public string? ConversationId { get; init; }

    public string? MessageClass { get; init; }

    public int Importance { get; init; } = 1;

    public bool IsMeetingRequest =>
        MessageClass is not null &&
        MessageClass.StartsWith("IPM.Schedule.Meeting", StringComparison.OrdinalIgnoreCase);

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(From) &&
        string.IsNullOrWhiteSpace(Subject) &&
        DateReceived is null;

    public string DisplaySender => FromName ?? FromAddress ?? From ?? "(без отправителя)";

    public string DisplaySubject => string.IsNullOrWhiteSpace(Subject) ? "(без темы)" : Subject;
}