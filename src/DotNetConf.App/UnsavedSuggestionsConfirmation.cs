namespace DotNetConf.App;

public sealed record UnsavedSuggestionsConfirmation(string Title, string Message, string Accept)
{
    public const string Cancel = "Keep choosing";

    public static UnsavedSuggestionsConfirmation? ForOnboarding(bool hasSelections, bool hasAddedSessions, bool continuing) =>
        For(hasSelections) ?? (continuing && !hasAddedSessions
            ? new("Continue without sessions?",
                "You haven't added any sessions during setup. Was that intentional? You can discover and save talks in My Schedule later.",
                "Continue without sessions")
            : null);

    public static UnsavedSuggestionsConfirmation? For(bool hasSelections)
    {
        if (hasSelections)
            return new("Leave without adding?",
                "Your checked suggestions haven't been added to My Schedule. Choose Keep choosing, then Add to save them, or leave without adding them.",
                "Leave without adding");

        return null;
    }
}
