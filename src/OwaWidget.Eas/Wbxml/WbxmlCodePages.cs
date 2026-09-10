namespace OwaWidget.Eas.Wbxml;

public static class WbxmlCodePages
{
    public const byte AirSync = 0x00;
    public const byte Email = 0x02;
    public const byte Calendar = 0x04;
    public const byte FolderHierarchy = 0x07;
    public const byte Ping = 0x0D;
    public const byte Provision = 0x0E;
    public const byte AirSyncBase = 0x11;
    public const byte Settings = 0x12;
    public const byte ItemOperations = 0x14;
    public const byte ComposeMail = 0x16;

    private const int PageCount = 0x18;

    private static readonly Dictionary<byte, string>[] ByToken;
    private static readonly Dictionary<string, byte>[] ByName;

    static WbxmlCodePages()
    {
        ByToken = new Dictionary<byte, string>[PageCount];
        ByName = new Dictionary<string, byte>[PageCount];
        for (var i = 0; i < PageCount; i++)
        {
            ByToken[i] = new Dictionary<byte, string>();
            ByName[i] = new Dictionary<string, byte>(StringComparer.Ordinal);
        }

        RegisterAirSync();
        RegisterEmail();
        RegisterCalendar();
        RegisterFolderHierarchy();
        RegisterPing();
        RegisterProvision();
        RegisterAirSyncBase();
        RegisterSettings();
        RegisterItemOperations();
        RegisterComposeMail();
    }

    private static void Add(byte page, byte token, string name)
    {
        ByToken[page][token] = name;
        ByName[page][name] = token;
    }

    private static void RegisterAirSync()
    {
        Add(AirSync, 0x05, "Sync");
        Add(AirSync, 0x06, "Responses");
        Add(AirSync, 0x07, "Add");
        Add(AirSync, 0x08, "Change");
        Add(AirSync, 0x09, "Delete");
        Add(AirSync, 0x0A, "Fetch");
        Add(AirSync, 0x0B, "SyncKey");
        Add(AirSync, 0x0C, "ClientId");
        Add(AirSync, 0x0D, "ServerId");
        Add(AirSync, 0x0E, "Status");
        Add(AirSync, 0x0F, "Collection");
        Add(AirSync, 0x10, "Class");
        Add(AirSync, 0x12, "CollectionId");
        Add(AirSync, 0x13, "GetChanges");
        Add(AirSync, 0x14, "MoreAvailable");
        Add(AirSync, 0x15, "WindowSize");
        Add(AirSync, 0x16, "Commands");
        Add(AirSync, 0x17, "Options");
        Add(AirSync, 0x18, "FilterType");
        Add(AirSync, 0x1B, "Conflict");
        Add(AirSync, 0x1C, "Collections");
        Add(AirSync, 0x1D, "ApplicationData");
        Add(AirSync, 0x1E, "DeletesAsMoves");
        Add(AirSync, 0x20, "Supported");
        Add(AirSync, 0x21, "SoftDelete");
        Add(AirSync, 0x22, "MIMESupport");
        Add(AirSync, 0x23, "MIMETruncation");
        Add(AirSync, 0x24, "Wait");
        Add(AirSync, 0x25, "Limit");
        Add(AirSync, 0x26, "Partial");
        Add(AirSync, 0x27, "ConversationMode");
        Add(AirSync, 0x28, "MaxItems");
        Add(AirSync, 0x29, "HeartbeatInterval");
    }

    private static void RegisterEmail()
    {
        Add(Email, 0x05, "Attachment");
        Add(Email, 0x06, "Attachments");
        Add(Email, 0x07, "AttName");
        Add(Email, 0x08, "AttSize");
        Add(Email, 0x09, "Att0Id");
        Add(Email, 0x0A, "AttMethod");
        Add(Email, 0x0B, "AttRemoved");
        Add(Email, 0x0C, "Body");
        Add(Email, 0x0D, "BodySize");
        Add(Email, 0x0E, "BodyTruncated");
        Add(Email, 0x0F, "DateReceived");
        Add(Email, 0x10, "DisplayName");
        Add(Email, 0x11, "DisplayTo");
        Add(Email, 0x12, "Importance");
        Add(Email, 0x13, "MessageClass");
        Add(Email, 0x14, "Subject");
        Add(Email, 0x15, "Read");
        Add(Email, 0x16, "To");
        Add(Email, 0x17, "Cc");
        Add(Email, 0x18, "From");
        Add(Email, 0x19, "ReplyTo");
        Add(Email, 0x1A, "AllDayEvent");
        Add(Email, 0x1B, "Categories");
        Add(Email, 0x1C, "Category");
        Add(Email, 0x1D, "DtStamp");
        Add(Email, 0x1E, "EndTime");
        Add(Email, 0x1F, "InstanceType");
        Add(Email, 0x20, "BusyStatus");
        Add(Email, 0x21, "Location");
        Add(Email, 0x22, "MeetingRequest");
        Add(Email, 0x23, "Organizer");
        Add(Email, 0x24, "RecurrenceId");
        Add(Email, 0x25, "Reminder");
        Add(Email, 0x26, "ResponseRequested");
        Add(Email, 0x27, "Recurrences");
        Add(Email, 0x28, "Recurrence");
        Add(Email, 0x29, "Recurrence_Type");
        Add(Email, 0x2A, "Recurrence_Until");
        Add(Email, 0x2B, "Recurrence_Occurrences");
        Add(Email, 0x2C, "Recurrence_Interval");
        Add(Email, 0x2D, "Recurrence_DayOfWeek");
        Add(Email, 0x2E, "Recurrence_DayOfMonth");
        Add(Email, 0x2F, "Recurrence_WeekOfMonth");
        Add(Email, 0x30, "Recurrence_MonthOfYear");
        Add(Email, 0x31, "StartTime");
        Add(Email, 0x32, "Sensitivity");
        Add(Email, 0x33, "TimeZone");
        Add(Email, 0x34, "GlobalObjId");
        Add(Email, 0x35, "ThreadTopic");
        Add(Email, 0x36, "MIMEData");
        Add(Email, 0x37, "MIMETruncated");
        Add(Email, 0x38, "MIMESize");
        Add(Email, 0x39, "InternetCPID");
        Add(Email, 0x3A, "Flag");
        Add(Email, 0x3B, "Status");
        Add(Email, 0x3C, "ContentClass");
        Add(Email, 0x3D, "FlagType");
        Add(Email, 0x3E, "CompleteTime");
        Add(Email, 0x3F, "DisallowNewTimeProposal");
    }

    private static void RegisterCalendar()
    {
        Add(Calendar, 0x05, "TimeZone");
        Add(Calendar, 0x06, "AllDayEvent");
        Add(Calendar, 0x07, "Attendees");
        Add(Calendar, 0x08, "Attendee");
        Add(Calendar, 0x09, "Attendee_Email");
        Add(Calendar, 0x0A, "Attendee_Name");
        Add(Calendar, 0x0D, "BusyStatus");
        Add(Calendar, 0x0E, "Categories");
        Add(Calendar, 0x0F, "Category");
        Add(Calendar, 0x11, "DtStamp");
        Add(Calendar, 0x12, "EndTime");
        Add(Calendar, 0x13, "Exception");
        Add(Calendar, 0x14, "Exceptions");
        Add(Calendar, 0x15, "Exception_Deleted");
        Add(Calendar, 0x16, "Exception_StartTime");
        Add(Calendar, 0x17, "Location");
        Add(Calendar, 0x18, "MeetingStatus");
        Add(Calendar, 0x19, "Organizer_Email");
        Add(Calendar, 0x1A, "Organizer_Name");
        Add(Calendar, 0x1B, "Recurrence");
        Add(Calendar, 0x1C, "Recurrence_Type");
        Add(Calendar, 0x1D, "Recurrence_Until");
        Add(Calendar, 0x1E, "Recurrence_Occurrences");
        Add(Calendar, 0x1F, "Recurrence_Interval");
        Add(Calendar, 0x20, "Recurrence_DayOfWeek");
        Add(Calendar, 0x21, "Recurrence_DayOfMonth");
        Add(Calendar, 0x22, "Recurrence_WeekOfMonth");
        Add(Calendar, 0x23, "Recurrence_MonthOfYear");
        Add(Calendar, 0x24, "Reminder");
        Add(Calendar, 0x25, "Sensitivity");
        Add(Calendar, 0x26, "Subject");
        Add(Calendar, 0x27, "StartTime");
        Add(Calendar, 0x28, "UID");
        Add(Calendar, 0x29, "Attendee_Status");
        Add(Calendar, 0x2A, "Attendee_Type");
        Add(Calendar, 0x33, "DisallowNewTimeProposal");
        Add(Calendar, 0x34, "ResponseRequested");
        Add(Calendar, 0x35, "AppointmentReplyTime");
        Add(Calendar, 0x36, "ResponseType");
        Add(Calendar, 0x37, "CalendarType");
        Add(Calendar, 0x38, "IsLeapMonth");
        Add(Calendar, 0x39, "FirstDayOfWeek");
        Add(Calendar, 0x3A, "OnlineMeetingConfLink");
        Add(Calendar, 0x3B, "OnlineMeetingExternalLink");
        Add(Calendar, 0x3C, "ClientUid");
    }

    private static void RegisterFolderHierarchy()
    {
        Add(FolderHierarchy, 0x07, "DisplayName");
        Add(FolderHierarchy, 0x08, "ServerId");
        Add(FolderHierarchy, 0x09, "ParentId");
        Add(FolderHierarchy, 0x0A, "Type");
        Add(FolderHierarchy, 0x0C, "Status");
        Add(FolderHierarchy, 0x0E, "Changes");
        Add(FolderHierarchy, 0x0F, "Add");
        Add(FolderHierarchy, 0x10, "Delete");
        Add(FolderHierarchy, 0x11, "Update");
        Add(FolderHierarchy, 0x12, "SyncKey");
        Add(FolderHierarchy, 0x13, "FolderCreate");
        Add(FolderHierarchy, 0x14, "FolderDelete");
        Add(FolderHierarchy, 0x15, "FolderUpdate");
        Add(FolderHierarchy, 0x16, "FolderSync");
        Add(FolderHierarchy, 0x17, "Count");
    }

    private static void RegisterPing()
    {
        Add(Ping, 0x05, "Ping");
        Add(Ping, 0x07, "Status");
        Add(Ping, 0x08, "HeartbeatInterval");
        Add(Ping, 0x09, "Folders");
        Add(Ping, 0x0A, "Folder");
        Add(Ping, 0x0B, "Id");
        Add(Ping, 0x0C, "Class");
        Add(Ping, 0x0D, "MaxFolders");
    }

    private static void RegisterProvision()
    {
        Add(Provision, 0x05, "Provision");
        Add(Provision, 0x06, "Policies");
        Add(Provision, 0x07, "Policy");
        Add(Provision, 0x08, "PolicyType");
        Add(Provision, 0x09, "PolicyKey");
        Add(Provision, 0x0A, "Data");
        Add(Provision, 0x0B, "Status");
        Add(Provision, 0x0C, "RemoteWipe");
        Add(Provision, 0x0D, "EASProvisionDoc");
    }

    private static void RegisterAirSyncBase()
    {
        Add(AirSyncBase, 0x05, "BodyPreference");
        Add(AirSyncBase, 0x06, "Type");
        Add(AirSyncBase, 0x07, "TruncationSize");
        Add(AirSyncBase, 0x08, "AllOrNone");
        Add(AirSyncBase, 0x0A, "Body");
        Add(AirSyncBase, 0x0B, "Data");
        Add(AirSyncBase, 0x0C, "EstimatedDataSize");
        Add(AirSyncBase, 0x0D, "Truncated");
        Add(AirSyncBase, 0x0E, "Attachments");
        Add(AirSyncBase, 0x0F, "Attachment");
        Add(AirSyncBase, 0x10, "DisplayName");
        Add(AirSyncBase, 0x11, "FileReference");
        Add(AirSyncBase, 0x12, "Method");
        Add(AirSyncBase, 0x13, "ContentId");
        Add(AirSyncBase, 0x14, "ContentLocation");
        Add(AirSyncBase, 0x15, "IsInline");
        Add(AirSyncBase, 0x16, "NativeBodyType");
        Add(AirSyncBase, 0x17, "ContentType");
        Add(AirSyncBase, 0x18, "Preview");
        Add(AirSyncBase, 0x19, "BodyPartPreference");
        Add(AirSyncBase, 0x1A, "BodyPart");
        Add(AirSyncBase, 0x1B, "Status");
    }

    private static void RegisterSettings()
    {
        Add(Settings, 0x05, "Settings");
        Add(Settings, 0x06, "Status");
        Add(Settings, 0x07, "Get");
        Add(Settings, 0x08, "Set");
        Add(Settings, 0x09, "Oof");
        Add(Settings, 0x0A, "OofState");
        Add(Settings, 0x0B, "StartTime");
        Add(Settings, 0x0C, "EndTime");
        Add(Settings, 0x0D, "OofMessage");
        Add(Settings, 0x0E, "AppliesToInternal");
        Add(Settings, 0x0F, "AppliesToExternalKnown");
        Add(Settings, 0x10, "AppliesToExternalUnknown");
        Add(Settings, 0x11, "Enabled");
        Add(Settings, 0x12, "ReplyMessage");
        Add(Settings, 0x13, "BodyType");
        Add(Settings, 0x14, "DevicePassword");
        Add(Settings, 0x15, "Password");
        Add(Settings, 0x16, "DeviceInformation");
        Add(Settings, 0x17, "Model");
        Add(Settings, 0x18, "IMEI");
        Add(Settings, 0x19, "FriendlyName");
        Add(Settings, 0x1A, "OS");
        Add(Settings, 0x1B, "OSLanguage");
        Add(Settings, 0x1C, "PhoneNumber");
        Add(Settings, 0x1D, "UserInformation");
        Add(Settings, 0x1E, "EmailAddresses");
        Add(Settings, 0x1F, "SMTPAddress");
        Add(Settings, 0x20, "UserAgent");
        Add(Settings, 0x21, "EnableOutboundSMS");
        Add(Settings, 0x22, "MobileOperator");
        Add(Settings, 0x23, "PrimarySmtpAddress");
        Add(Settings, 0x24, "Accounts");
        Add(Settings, 0x25, "Account");
        Add(Settings, 0x26, "AccountId");
        Add(Settings, 0x27, "AccountName");
        Add(Settings, 0x28, "UserDisplayName");
        Add(Settings, 0x29, "SendDisabled");
    }

    private static void RegisterItemOperations()
    {
        Add(ItemOperations, 0x05, "ItemOperations");
        Add(ItemOperations, 0x06, "Fetch");
        Add(ItemOperations, 0x07, "Store");
        Add(ItemOperations, 0x08, "Options");
        Add(ItemOperations, 0x09, "Range");
        Add(ItemOperations, 0x0A, "Total");
        Add(ItemOperations, 0x0B, "Properties");
        Add(ItemOperations, 0x0C, "Data");
        Add(ItemOperations, 0x0D, "Status");
        Add(ItemOperations, 0x0E, "Response");
        Add(ItemOperations, 0x0F, "Version");
        Add(ItemOperations, 0x10, "Schema");
        Add(ItemOperations, 0x11, "Part");
        Add(ItemOperations, 0x12, "EmptyFolderContents");
        Add(ItemOperations, 0x13, "DeleteSubFolders");
        Add(ItemOperations, 0x16, "Move");
        Add(ItemOperations, 0x17, "DstFldId");
        Add(ItemOperations, 0x18, "ConversationId");
        Add(ItemOperations, 0x19, "MoveAlways");
    }

    private static void RegisterComposeMail()
    {
        Add(ComposeMail, 0x05, "SendMail");
        Add(ComposeMail, 0x06, "SmartForward");
        Add(ComposeMail, 0x07, "SmartReply");
        Add(ComposeMail, 0x08, "SaveInSentItems");
        Add(ComposeMail, 0x09, "ReplaceMime");
        Add(ComposeMail, 0x0B, "Source");
        Add(ComposeMail, 0x0C, "FolderId");
        Add(ComposeMail, 0x0D, "ItemId");
        Add(ComposeMail, 0x0E, "LongId");
        Add(ComposeMail, 0x0F, "InstanceId");
        Add(ComposeMail, 0x10, "Mime");
        Add(ComposeMail, 0x11, "ClientId");
        Add(ComposeMail, 0x12, "Status");
        Add(ComposeMail, 0x13, "AccountId");
    }

    public static string GetName(byte page, byte token)
    {
        if (page < PageCount && ByToken[page].TryGetValue(token, out var name))
        {
            return name;
        }

        return $"Unknown_0x{page:X2}_0x{token:X2}";
    }

    public static byte GetToken(byte page, string name)
    {
        if (page >= PageCount || !ByName[page].TryGetValue(name, out var token))
        {
            throw new ArgumentException($"Unknown element {name} on code page 0x{page:X2}.", nameof(name));
        }

        return token;
    }
}