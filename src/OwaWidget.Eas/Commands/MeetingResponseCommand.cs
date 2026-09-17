using System.Globalization;
using OwaWidget.Eas.Wbxml;

namespace OwaWidget.Eas.Commands;

public enum MeetingUserResponse
{
    Accept = 1,
    Tentative = 2,
    Decline = 3
}

public sealed class MeetingResponseRequest
{
    public required string CollectionId { get; init; }

    public required string RequestId { get; init; }

    public MeetingUserResponse Response { get; init; }

    public DateTimeOffset? InstanceId { get; init; }

    public bool SendResponse { get; init; } = true;
}

public sealed record MeetingResponseResult(int Status, string? CalendarId);

public static class MeetingResponseCommand
{
    public const string SilentProtocolVersion = "16.1";

    public static async Task<MeetingResponseResult> ExecuteAsync(
        EasClient client,
        MeetingResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        var writer = new WbxmlWriter();
        writer.Start(WbxmlCodePages.MeetingResponse, "MeetingResponse");
        writer.Start(WbxmlCodePages.MeetingResponse, "Request");
        writer.Element(WbxmlCodePages.MeetingResponse, "UserResponse", (int)request.Response);
        writer.Element(WbxmlCodePages.MeetingResponse, "CollectionId", request.CollectionId);
        writer.Element(WbxmlCodePages.MeetingResponse, "RequestId", request.RequestId);

        if (request.InstanceId is { } instance)
        {
            writer.Element(
                WbxmlCodePages.MeetingResponse,
                "InstanceId",
                instance.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
        }

        writer.EndElement();
        writer.EndElement();

        var root = await client.SendAsync(
                "MeetingResponse",
                writer,
                protocolVersion: request.SendResponse ? null : SilentProtocolVersion,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (root is null)
        {
            throw new EasException("MeetingResponse returned an empty body.");
        }

        var result = root.Child("Result");
        if (result is null)
        {
            var globalStatus = root.ChildInt("Status") ?? 0;
            throw new EasStatusException("MeetingResponse", globalStatus,
                $"MeetingResponse failed with status {globalStatus}.");
        }

        var status = result.ChildInt("Status") ?? 0;

        if (status != 1)
        {
            throw new EasStatusException("MeetingResponse", status, Describe(status));
        }

        return new MeetingResponseResult(status, result.ChildText("CalendarId"));
    }

    private static string Describe(int status)
    {
        return status switch
        {
            2 => "Сервер отклонил ответ на приглашение (неверный запрос).",
            3 => "Ошибка почтового ящика при ответе на приглашение.",
            4 => "Сервер не смог обработать ответ на приглашение.",
            5 => "Приглашение не найдено — возможно, оно уже обработано.",
            _ => $"Ответ на приглашение не принят, статус {status}."
        };
    }
}