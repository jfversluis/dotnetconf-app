using DotNetConf.Core;

namespace DotNetConf.App.Services;

public static class SessionRoutes
{
    public const string Template = "session/{sessionId}";

    public static string ForSession(string id)
    {
        SessionAppLinks.ValidateId(id);
        return $"//sessions/session/{Uri.EscapeDataString(id)}";
    }
}
