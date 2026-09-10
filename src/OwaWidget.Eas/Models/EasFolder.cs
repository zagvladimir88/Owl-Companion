namespace OwaWidget.Eas.Models;

public enum EasFolderType
{
    UserFolderGeneric = 1,
    Inbox = 2,
    Drafts = 3,
    DeletedItems = 4,
    SentItems = 5,
    Outbox = 6,
    Tasks = 7,
    Calendar = 8,
    Contacts = 9,
    Notes = 10,
    Journal = 11,
    UserMailFolder = 12,
    UserCalendarFolder = 13,
    UserContactsFolder = 14,
    UserTasksFolder = 15,
    UserJournalFolder = 16,
    UserNotesFolder = 17,
    Unknown = 18,
    RecipientCache = 19
}

public sealed record EasFolder(string ServerId, string ParentId, string DisplayName, EasFolderType Type)
{
    public bool IsMail => Type is EasFolderType.Inbox or EasFolderType.UserMailFolder;

    public bool IsCalendar => Type is EasFolderType.Calendar or EasFolderType.UserCalendarFolder;
}