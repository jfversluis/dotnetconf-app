using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DotNetConf.Core;

public sealed record SessionReminder(
    int NotificationId, string SessionId, string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public static class SessionReminders
{
    public const int MaximumPending = 60;

    public static SessionReminder[] Plan(ConferenceDocument conference, Func<string, bool> isSaved, DateTimeOffset now)
    {
        var reminders = conference.Sessions.Where(session => isSaved(session.Id) && session.StartsAt > now)
            .OrderBy(session => session.StartsAt).ThenBy(session => session.Id, StringComparer.Ordinal)
            .Take(MaximumPending).Select(session => new SessionReminder(
                BinaryPrimitives.ReadInt32BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"session-reminder:{session.Id}")))
                    & int.MaxValue, session.Id, session.Title, session.StartsAt, session.EndsAt)).ToArray();
        if (reminders.Select(reminder => reminder.NotificationId).Distinct().Count() != reminders.Length)
            throw new InvalidDataException("Session reminder identifiers collided.");
        return reminders;
    }
}
