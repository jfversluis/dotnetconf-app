using DotNetConf.Core;

namespace DotNetConf.App.Services;

public sealed class SchedulePresentationException(string message, Exception innerException) : Exception(message, innerException);

public interface IAskApplication
{
    ConferenceDocument? AskDocument { get; }
    TimeZoneInfo AskTimeZone { get; }
    Task OpenAskSearchAsync(AskIntent intent);
    Task OpenSessionAsync(string id);
    Task OpenSpeakerAsync(string id);
    Task OpenAskScheduleAsync(bool remindersUpdated);
    Task<bool> SaveAskSessionsAsync(string[] ids, CancellationToken token);
    Task<bool?> RemoveAskSessionsAsync(string[] ids, string[] expectedSavedIds,
        ConferenceDocument expectedDocument, CancellationToken token);
}
