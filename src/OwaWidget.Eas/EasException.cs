using System.Net;

namespace OwaWidget.Eas;

public class EasException : Exception
{
    public EasException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class EasTimeoutException : EasException
{
    public EasTimeoutException(string command)
        : base($"Command {command} timed out.")
    {
        Command = command;
    }

    public string Command { get; }
}

public sealed class EasHttpException : EasException
{
    public EasHttpException(HttpStatusCode statusCode, string command, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Command = command;
    }

    public HttpStatusCode StatusCode { get; }

    public string Command { get; }

    public bool NeedsProvisioning => (int)StatusCode == 449;

    public bool IsAuthFailure => StatusCode is HttpStatusCode.Unauthorized;

    public TimeSpan? RetryAfter { get; init; }

    public string? RedirectTo { get; init; }
}

public sealed class EasStatusException : EasException
{
    public EasStatusException(string command, int status, string message)
        : base(message)
    {
        Command = command;
        Status = status;
    }

    public string Command { get; }

    public int Status { get; }

    public bool NeedsProvisioning => Status is 141 or 142 or 143 or 144;

    public bool InvalidSyncKey => Status is 3;
}