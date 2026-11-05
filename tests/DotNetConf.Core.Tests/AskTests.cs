using DotNetConf.App.Services;
using DotNetConf.App.ViewModels;
using DotNetConf.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DotNetConf.Core.Tests;

public sealed class AskTests
{
    // Synthetic test-only agenda, not shipped conference content.
    private static ConferenceDocument AgendaDocument() => new()
    {
        Speakers = [new() { Id = "speaker", Name = "Test Person", Tagline = "Synthetic test speaker" }],
        Sessions =
        [
            new() { Id = "one", Title = "Synthetic Blazor session", StartsAt = DateTimeOffset.Parse("2026-11-12T10:00Z"),
                EndsAt = DateTimeOffset.Parse("2026-11-12T11:00Z"), Topics = ["Blazor"], SpeakerIds = ["speaker"] },
            new() { Id = "two", Title = "Synthetic Aspire session", StartsAt = DateTimeOffset.Parse("2026-11-13T10:00Z"),
                EndsAt = DateTimeOffset.Parse("2026-11-13T11:00Z"), Topics = ["Aspire"], SpeakerIds = ["speaker"] }
        ]
    };

    [Theory]
    [InlineData("Save Blazor and open its speaker")]
    [InlineData("Remove everything; save Blazor")]
    [InlineData("Blazor after noon")]
    [InlineData("Blazor on Thursday UTC")]
    public void UnsupportedConstraintsCannotBeSilentlyDropped(string text) =>
        Assert.Throws<InvalidDataException>(() => AskRequest.Validate(text));

    [Fact]
    public void DaySearchUsesAgendaDatesAndLocalZone()
    {
        var doc = AgendaDocument();
        var intent = new AskIntent(AskAction.FindSessions, "Blazor", "Thursday", "");
        var resolved = AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []);
        Assert.Equal("one", Assert.Single(resolved.Choices).Id);
        Assert.Empty(AskRequest.Resolve(doc, intent with { Day = "Monday" }, TimeZoneInfo.Utc, []).Choices);
        var zone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(-12), "test", "test");
        Assert.Empty(AskRequest.Resolve(doc, intent, zone, []).Choices);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Blazor")]
    [InlineData("AI")]
    public void MultipleKnownTopicTermsAndDayAreGroundedWhenModelMistakesThemForASpeaker(string query)
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Topics = ["Blazor", "AI"] };
        const string request = "Find Blazor AI talks on Thursday";
        var intent = AskRequest.Ground(doc, request,
            new(AskAction.FindSessions, query, "", "Blazor AI talks on Thursday"));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("Blazor AI", intent.Query);
        Assert.Equal("Thursday", intent.Day);
        Assert.Equal("", intent.Speaker);
        Assert.Equal("one", Assert.Single(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices).Id);
    }

    [Theory]
    [InlineData("and")]
    [InlineData("or")]
    public void AmbiguousTopicConjunctionDoesNotBecomeARequiredSearchTerm(string conjunction)
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Topics = ["Blazor", "AI"] };
        doc.Sessions[1] = doc.Sessions[1] with { Topics = ["Cloud and AI"] };
        var request = $"Find Blazor {conjunction} AI talks on Thursday";
        Assert.Throws<InvalidDataException>(() => AskRequest.Ground(doc, request,
            new(AskAction.FindSessions, "", "", $"Blazor {conjunction} AI talks on Thursday")));
    }

    [Fact]
    public void AConjunctionWithinAnActualTopicRemainsPartOfThatTopic()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Topics = ["Cloud and AI"] };
        const string request = "Find Cloud and AI talks on Thursday";
        var intent = AskRequest.Ground(doc, request,
            new(AskAction.FindSessions, "", "", "Cloud and AI talks on Thursday"));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("Cloud and AI", intent.Query);
        Assert.Equal("one", Assert.Single(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleTopicTermsDoNotOverrideAnAmbiguousSpeakerName(bool combinedTopic)
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "ambiguous", Name = "Blazor" });
        doc.Sessions[0] = doc.Sessions[0] with { Topics = combinedTopic ? ["Blazor AI"] : ["Blazor", "AI"] };
        const string request = "Find Blazor AI talks on Thursday";
        var intent = AskRequest.Ground(doc, request,
            new(AskAction.FindSessions, "", "", "Blazor AI talks on Thursday"));

        Assert.NotEqual("Blazor AI", intent.Query);
    }

    [Fact]
    public void NormalizedModelDateCannotChooseAnArbitraryThursdayOrDropTheDay()
    {
        var intent = new AskIntent(AskAction.FindSessions, "Blazor", "2026-11-12", "");
        Assert.Equal("Thursday", AskRequest.GroundDay("Blazor on Thursday", intent).Day);
        Assert.Equal("Thursday", AskRequest.GroundDay("Blazor on Thursday", intent with { Day = "" }).Day);
        Assert.Throws<InvalidDataException>(() => AskRequest.GroundDay("Blazor on Thursday", intent with { Day = "Friday" }));
    }

    [Fact]
    public void ConflictingLiteralWeekdayAndDateCannotSelectTheWrongDay()
    {
        var intent = new AskIntent(AskAction.SaveSession, "Blazor", "", "");
        Assert.Throws<InvalidDataException>(() =>
            AskRequest.Ground(AgendaDocument(), "Save Blazor on Thursday 2026-11-13", intent));
        Assert.Equal("2026-11-12",
            AskRequest.Ground(AgendaDocument(), "Save Blazor on Thursday 2026-11-12", intent).Day);
    }

    [Theory]
    [InlineData("Blazor on Thursday", "Blazor", "Thursday", "Blazor")]
    [InlineData("Blazor on Thursday", "Blazor", "Thursday", "Thursday")]
    public void ActualTopicAndDayKindsOutrankInferredSpeakerLabels(string request, string query, string day, string speaker)
    {
        var intent = AskRequest.Ground(AgendaDocument(), request, new(AskAction.FindSessions, query, day, speaker));
        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("", intent.Speaker);
        Assert.Equal("Blazor", intent.Query);
        Assert.Equal("one", Assert.Single(AskRequest.Resolve(AgendaDocument(), intent, TimeZoneInfo.Utc, []).Choices).Id);
    }

    [Theory]
    [InlineData("Blazor on Thursday", "", "Blazor on Thursday", "Thursday")]
    [InlineData("Blazor on Thursday?", "", "Blazor on Thursday", "Thursday")]
    [InlineData("Blazor talks on Thursday", "", "Blazor talks on Thursday", "Thursday")]
    [InlineData("Find Blazor sessions on Thursday", "Blazor", "Blazor sessions on Thursday", "Thursday")]
    [InlineData("Find Blazor on 2026-11-12", "", "Blazor on 2026-11-12", "2026-11-12")]
    [InlineData("Blazor on Thursday", "Blazor on Thursday", "", "Thursday")]
    [InlineData("Find Blazor talks on Thursday", "Blazor talks on Thursday", "", "Thursday")]
    [InlineData("Blazor on Thursday", "Blazor on Thursday", "Blazor on Thursday", "Thursday")]
    [InlineData("Blazor on Thursday?", "", "Blazor on Thursday?", "Thursday")]
    [InlineData("Blazor on Thursday?", "Blazor on Thursday?", "Blazor on Thursday?", "Thursday")]
    public void TopicAndDayMisclassifiedAsSpeakerRemainAnExactSearch(string request, string query, string speaker, string day)
    {
        var tools = CreateTools(AgendaDocument(), request, []);

        Assert.Contains("1 matches", tools.Lookup(query, "", speaker));
        tools.FindSessions(query, "", speaker);

        var pending = Assert.IsType<AskResolution>(tools.Complete());
        Assert.Equal("Blazor", pending.Intent.Query);
        Assert.Equal(day, pending.Intent.Day);
        Assert.Equal("", pending.Intent.Speaker);
        Assert.Equal("one", Assert.Single(pending.Choices).Id);
    }

    [Fact]
    public void LiteralTopicAndDaySearchIgnoresAnInventedSpeaker()
    {
        const string request = "Blazor talks on Thursday";
        var intent = AskRequest.Ground(AgendaDocument(), request,
            new(AskAction.FindSessions, "Blazor", "", "BlazorKit"));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("Blazor", intent.Query);
        Assert.Equal("Thursday", intent.Day);
        Assert.Equal("", intent.Speaker);
        Assert.Equal("one", Assert.Single(AskRequest.Resolve(AgendaDocument(), intent, TimeZoneInfo.Utc, []).Choices).Id);
    }

    [Fact]
    public void LiteralTopicAndDayRepairCannotDiscardAnotherRequestedTopic()
    {
        const string request = "Blazor AI talks on Thursday";
        var intent = AskRequest.Ground(AgendaDocument(), request,
            new(AskAction.FindSessions, "Blazor", "", "BlazorKit"));

        Assert.Throws<InvalidDataException>(() => AskRequest.ValidateIntent(request, intent));
    }

    [Theory]
    [InlineData("Sessions by Blazor on Thursday")]
    [InlineData("Sessions with Blazor on Thursday")]
    [InlineData("Aspire and Blazor on Thursday")]
    public void MisclassifiedTopicAndDayNeverDropOtherConstraints(string request)
    {
        Assert.Throws<InvalidDataException>(() =>
        {
            var intent = AskRequest.Ground(AgendaDocument(), request,
                new(AskAction.FindSessions, "", "", "Blazor on Thursday"));
            AskRequest.ValidateIntent(request, intent);
        });
    }

    [Fact]
    public void TopicAndDayInQueryCannotDropAnotherTopic()
    {
        Assert.Throws<InvalidDataException>(() =>
            AskRequest.Ground(AgendaDocument(), "Aspire and Blazor on Thursday",
                new(AskAction.FindSessions, "Blazor on Thursday", "", "")));
    }

    [Fact]
    public void TopicAndDaySearchRecognizesAnAliasOfAFeedTopic()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Topics = [".NET MAUI"] };
        const string request = "MAUI on Thursday";
        var intent = AskRequest.Ground(doc, request, new(AskAction.FindSessions, "", "", "MAUI on Thursday"));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("MAUI", intent.Query);
        Assert.Equal("", intent.Speaker);
        Assert.Equal("one", Assert.Single(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices).Id);
    }

    [Fact]
    public void SpeakerSurnameThatIsAWeekdayIsNotAConferenceDay()
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "friday", Name = "Daniel Friday" }, new() { Id = "roth", Name = "Daniel Roth" }]);
        doc.Sessions[0] = doc.Sessions[0] with { SpeakerIds = ["friday"] };
        doc.Sessions.Add(doc.Sessions[0] with { Id = "other", SpeakerIds = ["roth"] });
        const string request = "Find sessions by Daniel Friday";
        var intent = AskRequest.Ground(doc, request, new(AskAction.FindSessions, "Daniel Friday", "", ""));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("Daniel Friday", intent.Speaker);
        Assert.Equal("", intent.Day);
        Assert.Equal("one", Assert.Single(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices).Id);
        var misclassifiedDay = AskRequest.Ground(doc, request, new(AskAction.FindSessions, "Daniel", "Friday", ""));
        AskRequest.ValidateIntent(request, misclassifiedDay);
        Assert.Equal("Daniel Friday", misclassifiedDay.Speaker);
        Assert.Equal("", misclassifiedDay.Day);
    }

    [Fact]
    public void ARequestedDayAfterAWeekdaySurnameIsKeptSeparately()
    {
        const string request = "Find sessions by Daniel Friday on Thursday";
        var intent = AskRequest.Ground(AgendaDocument(), request,
            new(AskAction.FindSessions, "Daniel Friday", "", ""));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("Daniel Friday", intent.Speaker);
        Assert.Equal("Thursday", intent.Day);
    }

    [Theory]
    [InlineData("Find Blazor AI talks on Thursday", AskAction.FindSessions, "Blazor", "")]
    [InlineData("Remove saved Blazor sessions with Sam Taylor", AskAction.RemoveSessions, "Blazor", "")]
    public void SearchAndRemovalCannotDropRequestedConstraints(string request, AskAction action, string query, string speaker)
    {
        var intent = AskRequest.Ground(AgendaDocument(), request, new(action, query, "", speaker));
        Assert.Throws<InvalidDataException>(() => AskRequest.ValidateIntent(request, intent));
    }

    [Theory]
    [InlineData("Find C# talks", "C")]
    [InlineData("Find Clear Path", "Path")]
    [InlineData("Who's giving the Blazor keynote?", "Blazor")]
    public void SearchScopeIncludesLanguageSymbolsAndMeaningfulTitleWords(string request, string query)
    {
        var intent = AskRequest.Ground(AgendaDocument(), request, new(AskAction.FindSessions, query, "", ""));
        Assert.Throws<InvalidDataException>(() => AskRequest.ValidateIntent(request, intent));
    }

    [Theory]
    [InlineData(AskAction.FindSessions, "Find ASP.NET sessions", "ASP NET")]
    [InlineData(AskAction.RemoveSessions, "Remove all ASP.NET sessions", "ASP NET")]
    [InlineData(AskAction.FindSessions, "Find .NET sessions", "NET")]
    [InlineData(AskAction.RemoveSessions, "Remove all AI-driven sessions", "AI driven")]
    public void SearchScopeCannotLoseSignificantPunctuation(AskAction action, string request, string query)
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "ASP.NET fundamentals" };
        doc.Sessions[1] = doc.Sessions[1] with
        {
            Title = "Classic ASP migration", Description = "A net reduction in hosting costs"
        };
        var tools = CreateTools(doc, request, ["one", "two"]);

        Assert.Throws<InvalidDataException>(() =>
        {
            if (action == AskAction.RemoveSessions)
                tools.RequestRemoval(query);
            else
                tools.FindSessions(query);
        });
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData(AskAction.FindSessions, "Find ASP.NET sessions")]
    [InlineData(AskAction.RemoveSessions, "Remove all ASP.NET sessions")]
    public void LiteralPunctuatedSearchOnlyMatchesTheRequestedScope(AskAction action, string request)
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "ASP.NET fundamentals" };
        doc.Sessions[1] = doc.Sessions[1] with
        {
            Title = "Classic ASP migration", Description = "A net reduction in hosting costs"
        };
        var tools = CreateTools(doc, request, ["one", "two"]);

        if (action == AskAction.RemoveSessions)
            tools.RequestRemoval("ASP.NET");
        else
            tools.FindSessions("ASP.NET");

        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Fact]
    public void SpeakerCannotConsumePartOfAPunctuatedTopic()
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "ai", Name = "Ai Tanaka" }, new() { Id = "casey", Name = "Casey Jones" }]);
        doc.Sessions[0] = doc.Sessions[0] with { Title = "AI-driven products", SpeakerIds = ["casey"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Driven by curiosity", SpeakerIds = ["ai"] };
        var tools = CreateTools(doc, "Remove all AI-driven sessions", ["one", "two"]);

        Assert.Throws<InvalidDataException>(() => tools.RequestRemoval("driven", speaker: "AI"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void MatchingSpeakerNameIsNotReinterpretedAsATopic()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "ambiguous", Name = "Blazor on Thursday" });

        var intent = AskRequest.Ground(doc, "Blazor on Thursday",
            new(AskAction.FindSessions, "", "", "Blazor on Thursday"));

        Assert.Equal("Blazor on Thursday", intent.Speaker);
        Assert.Equal("", intent.Query);
    }

    [Fact]
    public void ExplicitUnknownSpeakerCannotBeReinterpretedAsATopic()
    {
        var intent = AskRequest.Ground(AgendaDocument(), "Sessions by Blazor",
            new(AskAction.FindSessions, "", "", "Blazor"));
        Assert.Equal("Blazor", intent.Speaker);
        Assert.Empty(AskRequest.Resolve(AgendaDocument(), intent, TimeZoneInfo.Utc, []).Choices);
    }

    [Theory]
    [InlineData("Favorite all AI sessions", AskAction.SaveSession)]
    [InlineData("Remove saved AI sessions", AskAction.RemoveSessions)]
    [InlineData("Find AI sessions", AskAction.FindSessions)]
    public void TopicSearchDoesNotSelectUnrelatedSubstringMatches(string request, AskAction action)
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "casey", Name = "Casey Jones" }, new() { Id = "aisha", Name = "Aisha Jones" }]);
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic AI session", Topics = ["AI"], SpeakerIds = ["casey"] };
        doc.Sessions[1] = doc.Sessions[1] with
        {
            Title = "Maintaining mobile applications", Topics = [".NET MAUI"], SpeakerIds = ["aisha"]
        };
        var intent = AskRequest.Ground(doc, request, new(action, "", "", "AI"));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("AI", intent.Query);
        Assert.Equal("", intent.Speaker);
        Assert.Equal("one", Assert.Single(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, ["one", "two"]).Choices).Id);
    }

    [Fact]
    public void EveryRecognizedTopicTermUsesWordBoundariesInACompoundSearch()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Blazor AI", Topics = ["AI", "Blazor"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Maintaining Blazor apps", Topics = ["Blazor", ".NET MAUI"] };
        var result = AskRequest.Resolve(doc, new(AskAction.FindSessions, "Blazor AI", "", ""), TimeZoneInfo.Utc, []);

        Assert.Equal("one", Assert.Single(result.Choices).Id);
    }

    [Fact]
    public void PlusSignCanSeparateIndependentSearchTerms()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic Blazor+AI session", Topics = ["AI"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Synthetic Blazor session", Topics = ["Blazor"] };
        var result = AskRequest.Resolve(doc, new(AskAction.FindSessions, "Blazor AI", "", ""), TimeZoneInfo.Utc, []);

        Assert.Equal("one", Assert.Single(result.Choices).Id);
    }

    [Fact]
    public void HashtagCanIntroduceATopic()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Topics = [".NET MAUI"] };
        doc.Sessions[1] = doc.Sessions[1] with { Description = "Synthetic #MAUI content", Topics = [] };
        const string request = "add all MAUI sessions to my schedule";
        var intent = AskRequest.Ground(doc, request, new(AskAction.SaveSession, "MAUI", "", ""));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal(["one", "two"], AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices.Select(c => c.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TopicSearchDoesNotDropProgrammingLanguageSymbols(bool hasExactTopic)
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic C session", Topics = ["C"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Synthetic C# session", Topics = ["C#"] };
        if (!hasExactTopic)
            doc.Sessions.RemoveAt(0);
        var result = AskRequest.Resolve(doc, new(AskAction.SaveSession, "C", "", "", true), TimeZoneInfo.Utc, []);

        if (hasExactTopic)
            Assert.Equal("one", Assert.Single(result.Choices).Id);
        else
            Assert.Empty(result.Choices);
    }

    [Theory]
    [InlineData(AskAction.SaveSession)]
    [InlineData(AskAction.RemoveSessions)]
    [InlineData(AskAction.FindSessions)]
    public void UnknownTopicDoesNotMatchAFragmentOfAnotherWord(AskAction action)
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic trustworthy applications" };
        var result = AskRequest.Resolve(doc, new(action, "Rust", "", "", true), TimeZoneInfo.Utc, ["one", "two"]);

        Assert.Empty(result.Choices);
    }

    [Fact]
    public void RemovalRecognizesATopicThatOnlyExistsInWithdrawnSessions()
    {
        var doc = AgendaDocument();
        doc.WithdrawnSessions.Add(doc.Sessions[0] with { Id = "withdrawn", Title = "Synthetic AI session", Topics = ["AI"] });
        const string request = "Remove saved AI sessions";
        var intent = AskRequest.Ground(doc, request, new(AskAction.RemoveSessions, "", "", "AI"));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("AI", intent.Query);
        Assert.Equal("withdrawn", Assert.Single(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, ["withdrawn"]).Choices).Id);
    }

    [Fact]
    public void UnqualifiedTopicThatIsAlsoAFullSpeakerNameIsRejectedAsAmbiguous()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Topics = ["AI"] };
        doc.Speakers.Add(new() { Id = "ai", Name = "AI" });
        Assert.Throws<InvalidDataException>(() =>
            AskRequest.Ground(doc, "Find AI sessions", new(AskAction.FindSessions, "", "", "AI")));
    }

    [Fact]
    public void ExplicitSpeakerTakesPrecedenceOverAnIdenticalTopic()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "ai", Name = "AI" });
        doc.Sessions[0] = doc.Sessions[0] with { Topics = ["AI"] };
        doc.Sessions[1] = doc.Sessions[1] with { SpeakerIds = ["ai"] };
        const string request = "Find sessions by AI";
        var intent = AskRequest.Ground(doc, request, new(AskAction.FindSessions, "", "", "AI"));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("two", Assert.Single(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices).Id);
    }

    [Fact]
    public void DayBeforeASpeakerDoesNotTruncateAWeekdaySurname()
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "friday", Name = "Daniel Friday" }, new() { Id = "roth", Name = "Daniel Roth" }]);
        doc.Sessions[1] = doc.Sessions[1] with { SpeakerIds = ["friday"] };
        doc.Sessions.Add(doc.Sessions[1] with { Id = "other", SpeakerIds = ["roth"] });
        const string request = "Favorite all sessions on Friday by Daniel Friday";
        var intent = AskRequest.Ground(doc, request, new(AskAction.SaveSession, "", "Friday", "Daniel Friday"));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("Daniel Friday", intent.Speaker);
        Assert.Equal("two", Assert.Single(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices).Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitSingleWordSpeakerMustMatchACompleteNamePart(bool matchingSpeakerPresent)
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "roth", Name = "Daniel Roth" }, new() { Id = "smith", Name = "Daniella Smith" }]);
        doc.Sessions[0] = doc.Sessions[0] with { SpeakerIds = ["roth"] };
        doc.Sessions[1] = doc.Sessions[1] with { SpeakerIds = ["smith"] };
        if (!matchingSpeakerPresent)
        {
            doc.Speakers.RemoveAll(speaker => speaker.Id == "roth");
            doc.Sessions.RemoveAll(session => session.Id == "one");
        }
        const string request = "Favorite all sessions by Daniel";
        var intent = AskRequest.Ground(doc, request, new(AskAction.SaveSession, "", "", "Daniel"));

        AskRequest.ValidateIntent(request, intent);
        var choices = AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices;
        if (matchingSpeakerPresent)
            Assert.Equal("one", Assert.Single(choices).Id);
        else
            Assert.Empty(choices);
    }

    [Fact]
    public void UnqualifiedWeekdaySurnameDoesNotBecomeADayOrBroadenBulkSave()
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "friday", Name = "Daniel Friday" }, new() { Id = "roth", Name = "Daniel Roth" }]);
        doc.Sessions[1] = doc.Sessions[1] with { SpeakerIds = ["friday"] };
        doc.Sessions.Add(doc.Sessions[1] with { Id = "other", SpeakerIds = ["roth"] });
        const string request = "Favorite all Daniel Friday sessions";
        var intent = AskRequest.Ground(doc, request, new(AskAction.SaveSession, "Daniel Friday", "", ""));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("", intent.Day);
        Assert.Equal("Daniel Friday", intent.Speaker);
        Assert.Equal("two", Assert.Single(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices).Id);
    }

    [Fact]
    public void RepeatedWeekdayKeepsBothAnUnqualifiedSpeakerNameAndTheRequestedDay()
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "friday", Name = "Daniel Friday" }, new() { Id = "roth", Name = "Daniel Roth" }]);
        doc.Sessions[1] = doc.Sessions[1] with { SpeakerIds = ["friday"] };
        doc.Sessions.Add(doc.Sessions[1] with { Id = "other", SpeakerIds = ["roth"] });
        const string request = "Favorite all Daniel Friday sessions on Friday";
        var intent = AskRequest.Ground(doc, request, new(AskAction.SaveSession, "Daniel Friday", "", ""));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("Friday", intent.Day);
        Assert.Equal("Daniel Friday", intent.Speaker);
        Assert.Equal("two", Assert.Single(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices).Id);
    }

    [Fact]
    public void DayBeforeAnUnqualifiedWeekdaySurnameDoesNotBroadenBulkSave()
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "friday", Name = "Daniel Friday" }, new() { Id = "roth", Name = "Daniel Roth" }]);
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Building for mobile", SpeakerIds = ["friday"] };
        doc.Sessions.Add(doc.Sessions[1] with { Id = "other", Title = "Building for Friday", SpeakerIds = ["roth"] });
        const string request = "Favorite all sessions on Friday for Daniel Friday";
        var intent = AskRequest.Ground(doc, request, new(AskAction.SaveSession, "Daniel Friday", "", ""));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("Friday", intent.Day);
        Assert.Equal("Daniel Friday", intent.Speaker);
        Assert.Equal("two", Assert.Single(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices).Id);
    }

    [Theory]
    [InlineData("Remove saved sessions on Friday for Daniel Friday", "Daniel Friday", "Friday")]
    [InlineData("Remove saved sessions for Daniel Roth", "Daniel Roth", "Roth")]
    public void SplitUnqualifiedSpeakerNameCannotBroadenRemoval(string request, string fullName, string surname)
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "target", Name = fullName }, new() { Id = "other", Name = $"Casey {surname}" }]);
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Building for mobile", SpeakerIds = ["target"] };
        doc.Sessions.Add(doc.Sessions[1] with { Id = "other-session", Title = "Lessons from Daniel", SpeakerIds = ["other"] });

        Assert.Throws<InvalidDataException>(() =>
        {
            var intent = AskRequest.Ground(doc, request, new(AskAction.RemoveSessions, "Daniel", "", surname));
            AskRequest.ValidateIntent(request, intent);
        });
    }

    [Fact]
    public void SplitUnknownSpeakerCannotMatchAnotherSpeakersTitle()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "other", Name = "Casey Friday" });
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Lessons from Daniel", SpeakerIds = ["other"] };
        var tools = CreateTools(doc, "Remove saved sessions for Daniel Friday", ["two"]);

        Assert.Throws<InvalidDataException>(() => tools.RequestRemoval("Daniel", "", "Friday"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void FullSpeakerNameCannotBecomeAFreeTextRemovalSearch()
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "target", Name = "Daniel Friday" }, new() { Id = "other", Name = "Casey Jones" }]);
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Building for mobile", SpeakerIds = ["target"] };
        doc.Sessions.Add(doc.Sessions[1] with { Id = "other-session", Title = "Lessons from Daniel Friday", SpeakerIds = ["other"] });
        var tools = CreateTools(doc, "Remove saved Daniel Friday sessions", ["two", "other-session"]);

        Assert.Contains("2 matches.", tools.Lookup(""));
        Assert.Throws<InvalidDataException>(() => tools.RequestRemoval("Daniel Friday"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void EmptyModelScopeCannotRemoveAllForASpeakerNamedWithCommonWords()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "target", Name = "The On" });
        doc.Sessions[0] = doc.Sessions[0] with { SpeakerIds = ["target"] };
        var tools = CreateTools(doc, "Remove all The On sessions", ["one", "two"]);

        Assert.Contains("2 matches.", tools.Lookup(""));
        Assert.Throws<InvalidDataException>(() => tools.RequestRemoval());
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData("Remove saved sessions for Daniel Roth", "Daniel Roth")]
    [InlineData("Remove saved sessions for Daniel", "Daniel")]
    [InlineData("Remove all sessions for The On", "")]
    [InlineData("Remove all The On sessions", "")]
    public void AmbiguousForScopeCannotBecomeAnUnrelatedRemoval(string request, string query)
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "target", Name = "Daniel Roth" }, new() { Id = "other", Name = "Casey Jones" }]);
        doc.Sessions[0] = doc.Sessions[0] with { SpeakerIds = ["target"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Lessons from Daniel Roth", SpeakerIds = ["other"] };
        if (query == "Daniel Roth")
        {
            doc.Speakers.RemoveAll(speaker => speaker.Id == "target");
            doc.Sessions[0] = doc.Sessions[0] with { SpeakerIds = ["speaker"] };
        }
        var tools = CreateTools(doc, request, ["one", "two"]);

        Assert.Throws<InvalidDataException>(() => tools.RequestRemoval(query));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void TopicOrDayAfterForDoesNotRequireASpeaker()
    {
        var doc = AgendaDocument();
        var topic = AskRequest.Ground(doc, "Remove saved sessions for Blazor",
            new(AskAction.RemoveSessions, "Blazor", "", ""));
        var day = AskRequest.Ground(doc, "Remove all sessions for Friday",
            new(AskAction.RemoveSessions, "", "", ""));

        AskRequest.ValidateIntent("Remove saved sessions for Blazor", topic);
        AskRequest.ValidateIntent("Remove all sessions for Friday", day);
        Assert.Equal("one", Assert.Single(AskRequest.Resolve(doc, topic, TimeZoneInfo.Utc, ["one", "two"]).Choices).Id);
        Assert.Equal("two", Assert.Single(AskRequest.Resolve(doc, day, TimeZoneInfo.Utc, ["one", "two"]).Choices).Id);
    }

    [Theory]
    [InlineData("Remove everything")]
    [InlineData("Please remove everything on Friday")]
    [InlineData("Remove all of my saved sessions for Friday")]
    [InlineData("Clear every saved talk on Friday")]
    public void ExplicitAllRemovalStillSupportsNormalPhrasing(string request)
    {
        var doc = AgendaDocument();
        var intent = AskRequest.Ground(doc, request, new(AskAction.RemoveSessions, "", "", ""));

        AskRequest.ValidateIntent(request, intent);
        Assert.NotEmpty(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, ["one", "two"]).Choices);
    }

    [Fact]
    public void EmptyModelScopeCannotDropATopicNamedLikeACommonWord()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Topics = ["On"] };
        var tools = CreateTools(doc, "Remove all sessions for On", ["one", "two"]);

        Assert.Throws<InvalidDataException>(() => tools.RequestRemoval());
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void UnknownStopwordOnlyBulkScopeCannotSaveUnrelatedSessions()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic session on tooling" };
        var tools = CreateTools(doc, "Favorite all The On sessions", []);

        Assert.Throws<InvalidDataException>(() => tools.SaveSession());
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData("Favorite all sessions for")]
    [InlineData("Favorite all sessions about")]
    [InlineData("Favorite all sessions on")]
    [InlineData("Favorite all the")]
    [InlineData("Favorite all of")]
    [InlineData("Favorite every")]
    public void IncompleteBulkScopeCannotSaveEverySession(string request)
    {
        var tools = CreateTools(AgendaDocument(), request, []);

        Assert.Throws<InvalidDataException>(() => tools.SaveSession());
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData("Remove all the")]
    [InlineData("Remove all of")]
    [InlineData("Remove every")]
    public void IncompleteBulkScopeCannotRemoveEverySession(string request)
    {
        var tools = CreateTools(AgendaDocument(), request, ["one", "two"]);

        Assert.Throws<InvalidDataException>(() => tools.RequestRemoval());
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData("Save all sessions")]
    [InlineData("Add all of the talks to my schedule on Friday")]
    [InlineData("Please favorite every session on Friday")]
    public void ExplicitAllSaveStillSupportsNormalPhrasing(string request)
    {
        var doc = AgendaDocument();
        var intent = AskRequest.Ground(doc, request, new(AskAction.SaveSession, "", "", ""));

        AskRequest.ValidateIntent(request, intent);
        Assert.True(intent.AllMatches);
        Assert.NotEmpty(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices);
    }

    [Fact]
    public void SpeakerNameCannotStandInForAnOmittedTopic()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "ai", Name = "Ai Tanaka" });
        doc.Sessions[0] = doc.Sessions[0] with { Topics = ["AI"], SpeakerIds = ["ai"] };
        doc.Sessions[1] = doc.Sessions[1] with { Topics = ["Web"], SpeakerIds = ["ai"] };
        var tools = CreateTools(doc, "Remove saved AI sessions for Ai Tanaka", ["one", "two"]);

        Assert.Throws<InvalidDataException>(() => tools.RequestRemoval("", "", "Ai Tanaka"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void SpeakerAndIdenticallyNamedTopicRemainIndependentConstraints()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "ai", Name = "Ai Tanaka" });
        doc.Sessions[0] = doc.Sessions[0] with { Topics = ["AI"], SpeakerIds = ["ai"] };
        doc.Sessions[1] = doc.Sessions[1] with { Topics = ["Web"], SpeakerIds = ["ai"] };
        var tools = CreateTools(doc, "Remove saved AI sessions for Ai Tanaka", ["one", "two"]);

        tools.RequestRemoval("AI", "", "Ai Tanaka");
        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnExplicitSpeakerNameCannotSatisfyAnIndependentSearchTerm(bool save)
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "ai", Name = "Ai Tanaka" });
        doc.Sessions[0] = doc.Sessions[0] with { Title = "AI workshop", Topics = ["Technology"], SpeakerIds = ["ai"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Web performance", Topics = ["Web"], SpeakerIds = ["ai"] };
        var tools = CreateTools(doc,
            save ? "Favorite all AI sessions by Ai Tanaka" : "Remove saved AI sessions by Ai Tanaka",
            ["one", "two"]);

        if (save)
            tools.SaveSession("AI", speaker: "Ai Tanaka");
        else
            tools.RequestRemoval("AI", speaker: "Ai Tanaka");
        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Theory]
    [InlineData("Ai Tanaka", "AI", "AI workshop")]
    [InlineData("Azure AI", "Azure AI", "Azure and AI workshop")]
    public void ATopicInSessionContentCannotBecomeAnUnqualifiedSpeakerName(string name, string topic, string title)
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "ai", Name = name });
        doc.Sessions[0] = doc.Sessions[0] with { Title = title, Topics = ["Technology"], SpeakerIds = ["ai"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Web performance", Topics = ["Web"], SpeakerIds = ["ai"] };
        var tools = CreateTools(doc, $"Favorite all {topic} sessions", []);

        Assert.Throws<InvalidDataException>(() => tools.SaveSession(topic));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void BulkSavesDoNotMatchFreeTextOnlyAgainstSpeakerNames()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "ai", Name = "Ai Azure Tanaka" });
        doc.Sessions[0] = doc.Sessions[0] with { Title = "AI Azure workshop", Topics = ["Technology"], SpeakerIds = ["ai"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Web performance", Topics = ["Web"], SpeakerIds = ["ai"] };
        var tools = CreateTools(doc, "Favorite all AI Azure sessions", ["one", "two"]);

        tools.SaveSession("AI Azure");
        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Fact]
    public void UnqualifiedSpeakerNamePartCannotBecomeAFreeTextRemoval()
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "roth", Name = "Daniel Roth" }, new() { Id = "casey", Name = "Casey Jones" }]);
        doc.Sessions[0] = doc.Sessions[0] with { SpeakerIds = ["roth"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Lessons from Daniel", SpeakerIds = ["casey"] };
        var tools = CreateTools(doc, "Remove saved Daniel sessions", ["one", "two"]);

        Assert.Throws<InvalidDataException>(() => tools.RequestRemoval("Daniel"));
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData(true, "Favorite all Friday sessions")]
    [InlineData(false, "Remove saved Friday sessions")]
    public void BareWeekdayThatIsAlsoASpeakerNameRequiresClarification(bool save, string request)
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "friday", Name = "Friday Jones" });
        doc.Sessions[0] = doc.Sessions[0] with { SpeakerIds = ["friday"] };
        var tools = CreateTools(doc, request, ["one", "two"]);

        Assert.Throws<InvalidDataException>(() =>
        {
            if (save)
                tools.SaveSession("", "", "Friday");
            else
                tools.RequestRemoval("", "", "Friday");
        });
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void ExplicitOnStillMeansDayWhenASpeakerHasTheSameName()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "friday", Name = "Friday Jones" });
        doc.Sessions[0] = doc.Sessions[0] with { SpeakerIds = ["friday"] };
        var tools = CreateTools(doc, "Favorite all sessions on Friday", []);

        tools.SaveSession();
        Assert.Equal("two", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitDayIsNotMaskedByAnIdenticallyNamedTitle(bool save)
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Friday" };
        var tools = CreateTools(doc,
            save ? "Favorite all sessions on Friday" : "Remove all saved sessions on Friday", ["one", "two"]);

        if (save)
            tools.SaveSession("Friday");
        else
            tools.RequestRemoval("Friday");
        Assert.Equal("two", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Fact]
    public void BulkTopicDoesNotBecomeAnExactTitleWhenATitleMatchesTheTopic()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Aspire", Topics = ["Cloud"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Getting started with Aspire", Topics = ["Cloud"] };
        var tools = CreateTools(doc, "Favorite all Aspire sessions", []);

        tools.SaveSession("Aspire");
        Assert.Equal(["one", "two"], tools.Complete()!.Choices.Select(choice => choice.Id));
    }

    [Theory]
    [InlineData(AskAction.FindSessions, "Find Aspire sessions")]
    [InlineData(AskAction.RemoveSessions, "Remove all saved Aspire sessions")]
    [InlineData(AskAction.RemoveSessions, "Remove saved Aspire sessions")]
    public void KeywordSearchAndBulkRemovalAreNotNarrowedToAnIdenticallyNamedTitle(AskAction action, string request)
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Aspire", Topics = ["Cloud"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Getting started with Aspire", Topics = ["Cloud"] };
        var tools = CreateTools(doc, request, ["one", "two"]);

        if (action == AskAction.RemoveSessions)
            tools.RequestRemoval("Aspire");
        else
            tools.FindSessions("Aspire");

        Assert.False(tools.Complete()!.Intent.ExactTitle);
        Assert.Equal(["one", "two"], tools.Complete()!.Choices.Select(choice => choice.Id));
    }

    [Fact]
    public void ExplicitSingleTitleRemovalStaysExact()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Aspire", Topics = ["Cloud"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Getting started with Aspire", Topics = ["Cloud"] };
        var tools = CreateTools(doc, "Remove saved Aspire", ["one", "two"]);

        tools.RequestRemoval("Aspire");

        Assert.True(tools.Complete()!.Intent.ExactTitle);
        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Fact]
    public void SpeakerSurnameDoesNotMakeASingleTitleRemovalPlural()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "maya", Name = "Maya Sessions" });
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Aspire", Topics = ["Cloud"], SpeakerIds = ["maya"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Getting started with Aspire", Topics = ["Cloud"], SpeakerIds = ["maya"] };
        var tools = CreateTools(doc, "Remove the Aspire session by Maya Sessions", ["one", "two"]);

        tools.RequestRemoval("Aspire", speaker: "Maya Sessions");

        Assert.True(tools.Complete()!.Intent.ExactTitle);
        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Theory]
    [InlineData("Remove Maya Sessions’ saved Aspire session", "Maya Sessions")]
    [InlineData("Remove Sessions' saved Aspire session", "Sessions")]
    [InlineData("Remove the Aspire session for Sessions", "Sessions")]
    [InlineData("Remove the Aspire session of Sessions", "Sessions")]
    [InlineData("Remove Aspire for Sessions", "Sessions")]
    [InlineData("Remove Aspire of Sessions", "Sessions")]
    [InlineData("Remove Aspire for Sessions on my agenda", "Sessions")]
    [InlineData("Remove Aspire of Sessions on my schedule", "Sessions")]
    [InlineData("Remove Aspire for Sessions on my conference agenda", "Sessions")]
    [InlineData("Remove Aspire for Sessions, on my schedule.", "Sessions")]
    [InlineData("Remove Aspire for \"Sessions\" on my schedule", "Sessions")]
    [InlineData("Remove Aspire for 'Sessions', on my schedule.", "Sessions")]
    [InlineData("Remove the Aspire session by Maya Sessions on my schedule", "Maya Sessions")]
    public void SpeakerScopeDoesNotMakeASingleTitleRemovalPlural(string request, string speaker)
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "maya", Name = "Maya Sessions" });
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Aspire", Topics = ["Cloud"], SpeakerIds = ["maya"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Getting started with Aspire", Topics = ["Cloud"], SpeakerIds = ["maya"] };
        var tools = CreateTools(doc, request, ["one", "two"]);

        tools.RequestRemoval("Aspire", speaker: speaker);

        Assert.True(tools.Complete()!.Intent.ExactTitle);
        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Theory]
    [InlineData("Remove Maya Sessions’ saved Aspire sessions", "Maya Sessions")]
    [InlineData("Remove Sessions' saved Aspire sessions", "Sessions")]
    [InlineData("Remove the Aspire sessions for Sessions", "Sessions")]
    [InlineData("Remove the Aspire sessions of Sessions", "Sessions")]
    public void SpeakerScopeDoesNotHideAPluralRemoval(string request, string speaker)
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "maya", Name = "Maya Sessions" });
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Aspire", Topics = ["Cloud"], SpeakerIds = ["maya"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Getting started with Aspire", Topics = ["Cloud"], SpeakerIds = ["maya"] };
        var tools = CreateTools(doc, request, ["one", "two"]);

        tools.RequestRemoval("Aspire", speaker: speaker);

        Assert.False(tools.Complete()!.Intent.ExactTitle);
        Assert.Equal(["one", "two"], tools.Complete()!.Choices.Select(choice => choice.Id));
    }

    [Fact]
    public void PossessiveSurnameStillCannotBeSplitFromTheRestOfASpeakerName()
    {
        const string request = "Remove Maya Sessions' sessions";

        var exception = Assert.Throws<InvalidDataException>(() =>
            AskRequest.ValidateIntent(request, new(AskAction.RemoveSessions, "Maya", "", "Sessions")));

        Assert.Contains("speaker's full name", exception.Message);
    }

    [Fact]
    public void RepeatedSpeakerNameCannotBeSplitBetweenQueryAndSpeaker()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "lee", Name = "Lee Smith" });
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Lee and the Future of .NET", SpeakerIds = ["lee"] };
        var tools = CreateTools(doc, "Remove Lee Lee's saved sessions", ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.RequestRemoval("Lee", speaker: "Lee"));
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData("Remove the Aspire session of Sessions")]
    [InlineData("Remove the Aspire sessions of Sessions")]
    [InlineData("Remove Aspire for Sessions")]
    [InlineData("Remove Aspire of Sessions")]
    [InlineData("Remove Aspire for Sessions on my agenda")]
    [InlineData("Remove Aspire for Sessions on my conference agenda")]
    [InlineData("Remove Aspire of Sessions on the saved conference agenda")]
    [InlineData("Remove Aspire for Sessions, on my schedule.")]
    [InlineData("Remove Aspire for \"Sessions\" on my schedule")]
    [InlineData("Remove Aspire of 'Sessions', on my schedule.")]
    [InlineData("Remove Aspire of Sessions on my schedule")]
    [InlineData("Remove Aspire for Sessions on the saved schedule")]
    [InlineData("Remove Sessions' saved Aspire session")]
    [InlineData("Remove Sessions' saved Aspire sessions")]
    public void OmittedGenericSpeakerCannotBroadenARemoval(string request)
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "maya", Name = "Maya Sessions" });
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Aspire", Topics = ["Cloud"], SpeakerIds = ["maya"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Getting started with Aspire", Topics = ["Cloud"] };
        var tools = CreateTools(doc, request, ["one", "two"]);

        Assert.Throws<InvalidDataException>(() => tools.RequestRemoval("Aspire"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void OmittedGenericSpeakerCannotBeHiddenBeforeAnotherConstraint()
    {
        const string request = "Remove Aspire for Sessions about MAUI";

        Assert.Throws<InvalidDataException>(() => AskRequest.ValidateIntent(request,
            new(AskAction.RemoveSessions, "Aspire MAUI", "", "")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Sessions")]
    public void QuotedPossessiveSpeakerCannotBroadenASingleTitleRemoval(string speaker)
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "maya", Name = "Maya Sessions (synthetic)" },
            new() { Id = "terry", Name = "Terry Chen (synthetic)" }]);
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic Aspire", Topics = ["Cloud"], SpeakerIds = ["maya"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Synthetic getting started with Aspire", Topics = ["Cloud"], SpeakerIds = ["maya"] };
        doc.Sessions.Add(doc.Sessions[0] with { Id = "other", Title = "Synthetic scaling Aspire", SpeakerIds = ["terry"] });
        var tools = CreateTools(doc, "Remove \"Sessions\"'s saved Synthetic Aspire session", ["one", "two", "other"]);

        if (speaker.Length == 0)
        {
            Assert.Throws<InvalidDataException>(() => tools.RequestRemoval("Synthetic Aspire", speaker: speaker));
            Assert.Null(tools.Complete());
        }
        else
        {
            tools.RequestRemoval("Synthetic Aspire", speaker: speaker);
            Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
        }
    }

    [Fact]
    public void FromSpeakerCannotBeStrippedAsASessionNounBeforeConferenceAgenda()
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "maya", Name = "Synthetic Maya Sessions" },
            new() { Id = "terry", Name = "Synthetic Terry Chen" }]);
        doc.Sessions[0] = doc.Sessions[0] with
        {
            Title = "Synthetic Aspire one", Description = "Synthetic: learn from sessions about Aspire",
            Topics = ["Aspire"], SpeakerIds = ["maya"]
        };
        doc.Sessions[1] = doc.Sessions[1] with
        {
            Title = "Synthetic Aspire two", Description = "Synthetic: learn from sessions about Aspire",
            Topics = ["Aspire"], SpeakerIds = ["terry"]
        };
        var tools = CreateTools(doc,
            "Favorite all Synthetic Aspire sessions from Sessions on the conference agenda to my schedule", []);

        Assert.Throws<InvalidDataException>(() => tools.SaveSession("Aspire"));
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData("Favorite all sessions on my schedule")]
    [InlineData("Favorite all sessions in my schedule")]
    [InlineData("Favorite all sessions on my agenda")]
    [InlineData("Favorite all Aspire sessions in my schedule")]
    [InlineData("Favorite all Aspire sessions on the saved conference agenda on Friday")]
    public void SavingAnExistingScheduleCannotBecomeABroaderRequest(string request)
    {
        var tools = CreateTools(AgendaDocument(), request, ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.SaveSession());
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData("Favorite all my saved Aspire sessions")]
    [InlineData("Favorite all Aspire sessions I already saved")]
    [InlineData("Favorite all my bookmarked Aspire sessions")]
    [InlineData("Favorite all my favorite Aspire sessions")]
    public void SavingAlreadySavedSessionsCannotAddUnsavedMatches(string request)
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic Aspire one", Description = "Synthetic: my saved Aspire sessions I already saved", Topics = ["Aspire"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Synthetic Aspire two", Description = "Synthetic: my saved Aspire sessions I already saved", Topics = ["Aspire"] };
        var tools = CreateTools(doc, request, ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.SaveSession("Aspire"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void SavingMyBookmarksCannotAddUnsavedMatches()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic syncing bookmarks", Description = "Synthetic: my bookmarks" };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Synthetic restoring bookmarks", Description = "Synthetic: my bookmarks" };
        var tools = CreateTools(doc, "Save all my bookmarks to my schedule", ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.SaveSession("bookmarks"));
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BookmarkTopicsAreNotPersonalBookmarkFilters(bool save)
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic syncing bookmarks", Topics = ["Bookmarks"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Synthetic bookmarks in .NET", Topics = ["Bookmarks"] };
        var tools = CreateTools(doc,
            save ? "Favorite all sessions about bookmarks" : "Find sessions about bookmarks", ["one"]);

        if (save)
            tools.SaveSession("bookmarks");
        else
            tools.FindSessions("bookmarks");

        Assert.Equal(["one", "two"], tools.Complete()!.Choices.Select(choice => choice.Id));
    }

    [Theory]
    [InlineData("Find my bookmarks about bookmarks")]
    [InlineData("Find my sessions about bookmarks")]
    [InlineData("Find my favorite sessions about bookmarks")]
    public void BookmarkTopicCannotHideAnExistingBookmarksRestriction(string request)
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Topics = ["Bookmarks"] };
        var tools = CreateTools(doc, request, ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.FindSessions("bookmarks"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void PunctuatedPersonalTopicCannotBecomeAnUnrestrictedBookmarkSearch()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with
        {
            Title = "Synthetic syncing bookmarks in .NET & MAUI",
            Description = "Synthetic: my .NET & MAUI sessions about bookmarks cover favorites."
        };
        doc.Sessions[1] = doc.Sessions[1] with
        {
            Title = "Synthetic restoring bookmarks in .NET & MAUI",
            Description = "Synthetic: my .NET & MAUI sessions about bookmarks cover favorites."
        };
        var tools = CreateTools(doc, "Favorite all my .NET & MAUI sessions about bookmarks", ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.SaveSession(".NET MAUI bookmarks"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void UnquotedPersonalSessionPhraseCannotBecomeAnExactTitle()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic introduction to Aspire" };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "My Aspire sessions" };
        var tools = CreateTools(doc, "Save my Aspire sessions", ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.SaveSession("My Aspire sessions"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void QuotedTitleCanContainAPersonalSessionPhrase()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "My First .NET Session (synthetic)" };
        var tools = CreateTools(doc, "Find sessions about \"My First .NET Session (synthetic)\"", ["one"]);

        tools.FindSessions("My First .NET Session (synthetic)");

        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Fact]
    public void QuotedPersonalTitleCannotHideAnExternalPersonalRestriction()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "My First .NET Session (synthetic)" };
        var tools = CreateTools(doc, "Find my sessions about \"My First .NET Session (synthetic)\"", ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.FindSessions("My First .NET Session (synthetic)"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void PersonalScopeBeforeAQuotedPluralTitleCannotBeDropped()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "MAUI sessions" };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "MAUI sessions in Action" };
        var tools = CreateTools(doc, "Find all my \"MAUI sessions\"", ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.FindSessions("MAUI sessions"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void ShortenedQueryCanFindAQuotedPersonalTitle()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "My First .NET Session" };
        var tools = CreateTools(doc, "Find sessions about \"My First .NET Session\"", []);

        tools.FindSessions("First .NET");

        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Theory]
    [InlineData("Find MAUI talks about \"Saved Sessions\"")]
    [InlineData("Find all \"saved sessions\" about MAUI")]
    public void QuotedTitleWithOnlyCommonWordsCannotBeDropped(string request)
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Saved Sessions", Topics = ["MAUI"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Synthetic introduction to MAUI", Topics = ["MAUI"] };
        var tools = CreateTools(doc, request, ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.FindSessions("MAUI"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void PossessiveSpeakerNamedMyIsNotAPersonalScheduleRestriction()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "my-le", Name = "My Le" });
        const string title = "Synthetic cloud fundamentals";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title, SpeakerIds = ["my-le"] };
        var tools = CreateTools(doc, $"Open My Le’s \"{title}\"", []);

        tools.OpenSession(title, speaker: "My Le");

        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Fact]
    public void SpeakerWithMiddleNameMyCanSaveAQuotedTitleUsingBy()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "amy-my-le", Name = "Amy My Le" });
        const string title = "Synthetic cloud fundamentals";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title, SpeakerIds = ["amy-my-le"] };
        var tools = CreateTools(doc, $"Save \"{title}\" by Amy My Le", []);

        tools.SaveSession(title, speaker: "Amy My Le");

        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Fact]
    public void SpeakerWithMiddleNameMyCanFilterSessionsUsingBy()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "amy-my-le", Name = "Amy My Le" });
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic MAUI one", Topics = ["MAUI"], SpeakerIds = ["amy-my-le"] };
        var tools = CreateTools(doc, "Find MAUI sessions by Amy My Le", []);

        tools.FindSessions("MAUI", speaker: "Amy My Le");

        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Fact]
    public void AmbiguousPossessiveSpeakerWithMiddleNameMySuggestsBy()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "amy-my-le", Name = "Amy My Le" });
        const string title = "Synthetic cloud fundamentals";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title, SpeakerIds = ["amy-my-le"] };
        var tools = CreateTools(doc, $"Save Amy My Le’s \"{title}\"", []);

        var error = Assert.Throws<InvalidDataException>(() => tools.SaveSession(title, speaker: "Amy My Le"));

        Assert.Contains("'by'", error.Message);
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void AmbiguousSpeakerWhoseNameIsMySessionsSuggestsNormalSearch()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "my-sessions", Name = "My Sessions" });
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic MAUI one", Topics = ["MAUI"], SpeakerIds = ["my-sessions"] };
        var tools = CreateTools(doc, "Favorite all MAUI talks by My Sessions to my schedule", []);

        var error = Assert.Throws<InvalidDataException>(() => tools.SaveSession("MAUI", speaker: "My Sessions"));

        Assert.Contains("normal search", error.Message);
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void PossessiveSpeakerWithMiddleNameMyCannotHideAnExternalPersonalRestriction()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "amy-my-le", Name = "Amy My Le" });
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic MAUI one", Topics = ["MAUI"], SpeakerIds = ["amy-my-le"] };
        var tools = CreateTools(doc, "Find my Amy My Le’s MAUI sessions", ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.FindSessions("MAUI", speaker: "Amy My Le"));
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData("All My Le", "Find all my Le’s MAUI sessions")]
    [InlineData("All Of My Le", "Find all of my Le’s MAUI sessions")]
    [InlineData("Most Of My Le", "Find most of my Le’s MAUI sessions")]
    [InlineData("Some Of My Le", "Find some of my Le’s MAUI sessions")]
    public void PossessiveSpeakerCannotHideAPersonalQuantifier(string speaker, string request)
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "synthetic-speaker", Name = speaker });
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic MAUI one", Topics = ["MAUI"], SpeakerIds = ["synthetic-speaker"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Synthetic MAUI two", Topics = ["MAUI"], SpeakerIds = ["synthetic-speaker"] };
        var tools = CreateTools(doc, request, ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.FindSessions("MAUI", speaker: speaker));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void PossessiveSpeakerNamedMyCannotHideAnAmbiguousPersonalPlural()
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "my-le", Name = "My Le" }, new() { Id = "han-le", Name = "Han Le" }]);
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic MAUI one", Topics = ["MAUI"], SpeakerIds = ["my-le"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Synthetic MAUI two", Topics = ["MAUI"], SpeakerIds = ["my-le"] };
        var tools = CreateTools(doc, "Find my Le’s MAUI sessions", ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.FindSessions("MAUI", speaker: "My Le"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void PossessiveSpeakerNamedMyCannotHideAQuotedPersonalPlural()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "my-le", Name = "My Le" });
        doc.Sessions[0] = doc.Sessions[0] with { Title = "MAUI sessions", SpeakerIds = ["my-le"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "MAUI sessions in Action", SpeakerIds = ["my-le"] };
        var tools = CreateTools(doc, "Find my Le’s \"MAUI sessions\"", ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.FindSessions("MAUI sessions", speaker: "My Le"));
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData("Find my Aspire sessions")]
    [InlineData("Find all of my Aspire sessions")]
    public void MyTopicSessionsCannotBecomeAnUnrestrictedSearch(string request)
    {
        var tools = CreateTools(AgendaDocument(), request, ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.FindSessions("Aspire"));
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData("Add all Aspire sessions to my saved schedule")]
    [InlineData("Add all Aspire sessions to my saved schedule on Friday")]
    [InlineData("Add all Aspire sessions to my bookmarks")]
    public void SavedScheduleAsADestinationDoesNotRestrictTheMatchingAgenda(string request)
    {
        var tools = CreateTools(AgendaDocument(), request, []);

        tools.SaveSession("Aspire");

        Assert.Equal("two", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Fact]
    public void PublishedConferenceAgendaReferenceIsNotAPersonalScheduleRestriction()
    {
        var tools = CreateTools(AgendaDocument(),
            "Add all Aspire sessions on the conference agenda to my schedule", []);

        tools.SaveSession("Aspire");

        Assert.Equal("two", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Theory]
    [InlineData(AskAction.SaveSession)]
    [InlineData(AskAction.FindSessions)]
    [InlineData(AskAction.RemoveSessions)]
    public void GroundedTitleOrTopicCanContainAnOfThePhrase(AskAction action)
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic State of the Art", Topics = ["Synthetic State of the Art"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Synthetic State of the Art Futures", Topics = ["Synthetic State of the Art"] };
        var request = action switch
        {
            AskAction.SaveSession => "Favorite all Synthetic State of the Art sessions",
            AskAction.FindSessions => "Find Synthetic State of the Art sessions",
            _ => "Remove saved Synthetic State of the Art sessions"
        };
        var tools = CreateTools(doc, request, ["one", "two"]);

        if (action == AskAction.SaveSession)
            tools.SaveSession("Synthetic State of the Art");
        else if (action == AskAction.FindSessions)
            tools.FindSessions("Synthetic State of the Art");
        else
            tools.RequestRemoval("Synthetic State of the Art");

        Assert.Equal(["one", "two"], tools.Complete()!.Choices.Select(choice => choice.Id));
    }

    [Fact]
    public void GroundedOfThePhraseCannotHideAnAdditionalGenericSpeaker()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "maya", Name = "Maya Sessions" });
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic State of the Art", Topics = ["Synthetic State of the Art"] };
        var tools = CreateTools(doc, "Remove Synthetic State of the Art for \"Sessions\"", ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.RequestRemoval("Synthetic State of the Art"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void QuotedGroundedTitleCanContainFromEveryInASearch()
    {
        var doc = AgendaDocument();
        const string title = "Synthetic Learning from Every Failure";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title };
        doc.Sessions[1] = doc.Sessions[1] with { Title = title + ": Incident Reviews" };
        var tools = CreateTools(doc, $"Find sessions about \"{title}\"", []);

        tools.FindSessions(title);

        Assert.Equal(["one", "two"], tools.Complete()!.Choices.Select(choice => choice.Id));
    }

    [Fact]
    public void QuotedGroundedTitleCannotHideAnExternalFromSpeaker()
    {
        var doc = AgendaDocument();
        const string title = "Synthetic Learning from Every Failure";
        doc.Speakers.Add(new() { Id = "maya", Name = "Maya Sessions" });
        doc.Sessions[0] = doc.Sessions[0] with { Title = title };
        var tools = CreateTools(doc, $"Find sessions about \"{title}\" from Sessions", []);

        Assert.Throws<InvalidDataException>(() => tools.FindSessions(title));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void ExactTitleMayContainSavedAsAWord()
    {
        var doc = AgendaDocument();
        const string title = "A synthetic saved state";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title };
        var tools = CreateTools(doc, $"Open {title}", []);

        tools.OpenSession(title);

        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Fact]
    public void ExactTitleCannotHideAnAmbiguousAlreadySavedRestriction()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Saved synthetic Aspire", Topics = ["Other"] };
        var tools = CreateTools(doc, "Save saved synthetic Aspire", []);

        Assert.Throws<InvalidDataException>(() => tools.SaveSession("Saved synthetic Aspire"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void ExactTitleCannotHideAnArticlePrefixedSavedRestriction()
    {
        var doc = AgendaDocument();
        const string title = "The saved synthetic Aspire session";
        doc.Sessions[1] = doc.Sessions[1] with { Title = title, Topics = ["Other"] };
        var tools = CreateTools(doc, $"Save {title}", ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.SaveSession(title));
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData("AI on the agenda")]
    [InlineData("AI to the agenda")]
    [InlineData("AI in the agenda")]
    [InlineData("AI into the agenda")]
    public void BulkSaveCannotTrimPartOfALiteralTitle(string title)
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = title, Topics = ["AI"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "AI Infrastructure", Topics = ["AI"] };
        var tools = CreateTools(doc, $"Favorite all {title}", []);

        var error = Record.Exception(() => tools.SaveSession(title));

        if (error is null)
            Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
        else
            Assert.IsType<InvalidDataException>(error);
    }

    [Theory]
    [InlineData(AskAction.SaveSession, "Save Blazor on my schedule")]
    [InlineData(AskAction.FindSessions, "Find Blazor sessions in my schedule")]
    [InlineData(AskAction.OpenSession, "Open Blazor on my schedule")]
    public void PersonalScheduleRestrictionCannotBeDiscarded(AskAction action, string request)
    {
        Assert.Throws<InvalidDataException>(() =>
        {
            var intent = AskRequest.Ground(AgendaDocument(), request, new(action, "Blazor", "", ""));
            AskRequest.ValidateIntent(request, intent);
        });
    }

    [Fact]
    public void LiteralTitleAmbiguityIsDetectedEvenWhenTheModelOmitsIt()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "AI to the agenda", Topics = ["AI"] };
        var tools = CreateTools(doc, "Favorite all AI to the agenda", []);

        Assert.Throws<InvalidDataException>(() => tools.SaveSession("AI"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void DestinationLikeTitleWithOmittedPunctuationCannotBecomeABroaderBulkSave()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic AI to the agenda!", Topics = ["AI"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Synthetic AI Infrastructure", Topics = ["AI"] };
        var tools = CreateTools(doc, "Favorite all Synthetic AI to the agenda", []);

        Assert.Throws<InvalidDataException>(() => tools.SaveSession());
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void DestinationLikeTitleWithOmittedClosingParenthesisCannotBecomeABroaderBulkSave()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic AI (Welcome to the Agenda)", Topics = ["AI"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Synthetic AI (Welcome back)", Topics = ["AI"] };
        var tools = CreateTools(doc, "Favorite all Synthetic AI (Welcome to the Agenda", []);

        Assert.Throws<InvalidDataException>(() => tools.SaveSession());
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData("Find Blazor for me")]
    [InlineData("Find all of the Blazor sessions")]
    public void OrdinaryForAndOfPhrasesDoNotRequireASpeaker(string request)
    {
        var intent = new AskIntent(AskAction.FindSessions, "Blazor", "", "");

        AskRequest.ValidateIntent(request, intent);
    }

    [Fact]
    public void GenericPossessiveInsideAnExactTitleRemainsASpecificRemoval()
    {
        var doc = AgendaDocument();
        const string title = "Sessions' guide to Aspire";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title, Topics = ["Cloud"] };
        var tools = CreateTools(doc, $"Remove {title}", ["one", "two"]);

        tools.RequestRemoval(title);

        Assert.True(tools.Complete()!.Intent.ExactTitle);
        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Fact]
    public void OfClauseForAKnownTopicStillFindsItsSavedSessions()
    {
        var doc = AgendaDocument();
        var tools = CreateTools(doc, "Remove saved sessions of Aspire", ["one", "two"]);

        tools.RequestRemoval("Aspire");

        Assert.Equal("two", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Fact]
    public void SessionsForAnUnknownPersonCannotBecomeAnExactTitleRemoval()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Daniel Roth" };
        var tools = CreateTools(doc, "Remove saved sessions for Daniel Roth", ["one"]);

        Assert.Throws<InvalidDataException>(() => tools.RequestRemoval("Daniel Roth"));
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExactTitleContainingSpeakerNameDoesNotMatchOtherTalks(bool save)
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "roth", Name = "Daniel Roth" });
        const string title = "Getting started with Daniel Roth";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Getting started with Blazor", SpeakerIds = ["roth"] };
        var tools = new AskTools();
        var request = save ? $"Favorite all {title} sessions" : $"Remove saved {title}";
        tools.Initialize(doc, TimeZoneInfo.Utc, request, ["one", "two"]);

        if (save)
            tools.SaveSession(title);
        else
            tools.RequestRemoval(title);
        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Theory]
    [InlineData("Accessible by design: making a conference app work for everyone")]
    [InlineData("Building AI sessions for beginners")]
    public void ExactTitleWordsAreNotInterpretedAsSpeakerSyntax(string title)
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = title };
        var tools = CreateTools(doc, $"Open {title}", []);

        tools.OpenSession(title);
        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Fact]
    public void OmittedExactTitleCannotTurnItsWordsIntoAnUnrelatedBulkSpeaker()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "design", Name = "Design Sessions" });
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Accessible by design" };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Accessible UX", SpeakerIds = ["design"] };
        var tools = CreateTools(doc, "Favorite all Accessible by design sessions", []);

        Assert.Throws<InvalidDataException>(() => tools.SaveSession());
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TitleThatAlsoLooksLikeASpeakerClauseRequiresClarification(bool save)
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "roth", Name = "Daniel Roth" }, new() { Id = "casey", Name = "Casey Jones" }]);
        const string title = "Blazor by Daniel Roth";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title, SpeakerIds = ["casey"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Blazor for everyone", SpeakerIds = ["roth"] };
        var tools = CreateTools(doc, save ? $"Favorite all {title} sessions" : $"Remove saved {title}", ["one", "two"]);

        Assert.Throws<InvalidDataException>(() =>
        {
            if (save)
                tools.SaveSession(title);
            else
                tools.RequestRemoval(title);
        });
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TitleCannotHideAKnownOrUnknownSpeakerClause(bool save)
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "casey", Name = "Casey Jones" });
        if (save)
            doc.Speakers.Add(new() { Id = "daniel", Name = "Daniel Roth" });
        var title = save ? "AI sessions with Daniel Roth" : "AI by Daniel Roth";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title, SpeakerIds = ["casey"] };
        var tools = CreateTools(doc,
            save ? $"Favorite all {title}" : $"Remove saved {title}", ["one", "two"]);

        Assert.Throws<InvalidDataException>(() =>
        {
            if (save)
                tools.SaveSession(title);
            else
                tools.RequestRemoval(title);
        });
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void ExactTitleDoesNotHideAnExternalSpeakerOrDayConstraint()
    {
        var doc = AgendaDocument();
        const string title = "Accessible by design on Friday";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title };
        doc.Sessions[1] = doc.Sessions[1] with { Title = title, SpeakerIds = [] };
        var tools = CreateTools(doc, $"Open {title} by Test Person on Thursday", []);

        tools.OpenSession(title, speaker: "Test Person");
        var result = tools.Complete()!;
        Assert.Equal("Thursday", result.Intent.Day);
        Assert.Equal("Test Person", result.Intent.Speaker);
        Assert.Equal("one", Assert.Single(result.Choices).Id);
    }

    [Fact]
    public void QuotedTitleWithGerundPreservesAnExternalSpeaker()
    {
        var doc = AgendaDocument();
        const string title = "Synthetic .NET by Doing";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title };
        doc.Sessions[1] = doc.Sessions[1] with { Title = title, SpeakerIds = [] };
        var tools = CreateTools(doc, $"Open \"{title}\" by Test Person", []);

        tools.OpenSession(title, speaker: "Test Person");

        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
        tools.Initialize(doc, TimeZoneInfo.Utc, $"Open \"{title}\" by Test Person", []);
        Assert.Throws<InvalidDataException>(() => tools.OpenSession(title));
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void QuotedSingleTitleSaveOrRemovalDoesNotMatchLongerTitles(bool save)
    {
        var doc = AgendaDocument();
        const string title = "Synthetic Aspire by Elon";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title };
        doc.Sessions[1] = doc.Sessions[1] with { Title = $"{title} in Action" };
        var tools = CreateTools(doc, save ? $"Save \"{title}\"" : $"Remove \"{title}\"", ["one", "two"]);

        if (save)
            tools.SaveSession(title);
        else
            tools.RequestRemoval(title);

        Assert.True(tools.Complete()!.Intent.ExactTitle);
        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void QuotedTopicWithPluralScopeCannotBeNarrowedToOneExactTitle(bool save)
    {
        var doc = AgendaDocument();
        const string title = "Synthetic Aspire by Elon";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title };
        doc.Sessions[1] = doc.Sessions[1] with { Title = $"{title} in Action" };
        var tools = new AskTools();
        var verb = save ? "Favorite" : "Remove";
        tools.Initialize(doc, TimeZoneInfo.Utc, $"{verb} all sessions about \"{title}\" by Test Person", ["one", "two"]);

        Assert.Throws<InvalidDataException>(() =>
        {
            if (save)
                tools.SaveSession(title, speaker: "Test Person");
            else
                tools.RequestRemoval(title, speaker: "Test Person");
        });
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void QuotedPluralSearchCannotBeNarrowedToOneExactTitle()
    {
        var doc = AgendaDocument();
        const string title = "Synthetic Aspire by Elon";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title };
        doc.Sessions[1] = doc.Sessions[1] with { Title = $"{title} in Action" };
        var tools = CreateTools(doc, $"Find sessions about \"{title}\"", []);

        Assert.Throws<InvalidDataException>(() => tools.FindSessions(title));
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData("Remove all sessions about: \"Synthetic Aspire by Elon\" by Test Person", "Synthetic Aspire by Elon")]
    [InlineData("Remove all sessions about (\"Synthetic Aspire by Elon\") by Test Person", "Synthetic Aspire by Elon")]
    [InlineData("Remove all sessions about \"Synthetic meet Alice Smith\" by Test Person", "Synthetic meet Alice Smith")]
    public void QuotedPluralRemovalCannotBeNarrowedByPunctuationOrKnownNames(string request, string title)
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "alice", Name = "Alice Smith" });
        doc.Sessions[0] = doc.Sessions[0] with { Title = title };
        doc.Sessions[1] = doc.Sessions[1] with { Title = $"{title} in Action" };
        var tools = CreateTools(doc, request, ["one", "two"]);

        Assert.Throws<InvalidDataException>(() => tools.RequestRemoval(title, speaker: "Test Person"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void ExactSessionTitleCanContainAKnownSpeakersFullName()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "friday", Name = "Daniel Friday" });
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Learning from Daniel Friday" };
        const string request = "Open Learning from Daniel Friday";
        var intent = AskRequest.Ground(doc, request, new(AskAction.OpenSession, "Learning from Daniel Friday", "", ""));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("one", Assert.Single(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices).Id);
    }

    [Theory]
    [InlineData(AskAction.FindSessions, "Find Daniel Friday sessions on Friday")]
    [InlineData(AskAction.RemoveSessions, "Remove saved Daniel Friday sessions on Friday")]
    [InlineData(AskAction.FindSessions, "Find sessions on Friday for Daniel Friday")]
    [InlineData(AskAction.RemoveSessions, "Remove saved sessions on Friday for Daniel Friday")]
    public void RepeatedWeekdayCannotHideADroppedSpeakerSurname(AskAction action, string request)
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "friday", Name = "Daniel Friday" });
        Assert.Throws<InvalidDataException>(() =>
        {
            var intent = AskRequest.Ground(doc, request, new(action, "Daniel", "", ""));
            AskRequest.ValidateIntent(request, intent);
        });
    }

    [Fact]
    public void WeekdayBeforeAnUnqualifiedSpeakerSurnameCannotHideADroppedSurname()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "friday", Name = "Daniel Friday" });
        const string request = "Find Friday Daniel Friday sessions";
        Assert.Throws<InvalidDataException>(() =>
        {
            var intent = AskRequest.Ground(doc, request, new(AskAction.FindSessions, "Daniel", "", ""));
            AskRequest.ValidateIntent(request, intent);
        });
    }

    [Fact]
    public void UnqualifiedSpeakerNamePartThatAlsoMatchesSessionContentIsAmbiguous()
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "roth", Name = "Daniel Roth" }, new() { Id = "casey", Name = "Casey Jones" }]);
        doc.Sessions[0] = doc.Sessions[0] with { SpeakerIds = ["roth"] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Lessons from Daniel", SpeakerIds = ["casey"] };
        const string request = "Favorite all Daniel sessions";
        Assert.Throws<InvalidDataException>(() =>
            AskRequest.Ground(doc, request, new(AskAction.SaveSession, "Daniel", "", "")));
    }

    [Fact]
    public void UnqualifiedSpeakerNamePartThatIsAlsoATopicIsAmbiguous()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "ai", Name = "Ai Tanaka" });
        doc.Sessions[0] = doc.Sessions[0] with { Topics = ["AI"] };
        Assert.Throws<InvalidDataException>(() =>
            AskRequest.Ground(doc, "Favorite all Ai sessions", new(AskAction.SaveSession, "", "", "Ai")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Daniel Roth")]
    [InlineData("sessions")]
    public void LiteralSpeakerSearchSurvivesAMisclassifiedOrOmittedSpeaker(string query)
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "roth", Name = "Daniel Roth" }, new() { Id = "casey", Name = "Casey" }]);
        doc.Sessions[0] = doc.Sessions[0] with { SpeakerIds = ["roth"] };
        doc.Sessions.Add(doc.Sessions[0] with
        {
            Id = "other", Title = "Learning from Daniel Roth", SpeakerIds = ["casey"]
        });
        const string request = "Find sessions by Daniel Roth";
        var intent = AskRequest.Ground(doc, request, new(AskAction.FindSessions, query, "", ""));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("", intent.Query);
        Assert.Equal("Daniel Roth", intent.Speaker);
        Assert.Equal("one", Assert.Single(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices).Id);
    }

    [Fact]
    public void LiteralSpeakerRepairCannotDropAnAdditionalTopic()
    {
        const string request = "Find Blazor sessions by Test Person";
        var intent = AskRequest.Ground(AgendaDocument(), request,
            new(AskAction.FindSessions, "Test Person", "", ""));

        Assert.Throws<InvalidDataException>(() => AskRequest.ValidateIntent(request, intent));
    }

    [Fact]
    public void LookupSessionIdCannotBecomeASpeakerOrSelectAnAmbiguousTalk()
    {
        var doc = AgendaDocument();
        doc.Sessions.Add(doc.Sessions[0] with { Id = "another" });
        var intent = AskRequest.Ground(doc, "Open Blazor", new(AskAction.OpenSession, "Blazor", "", "one"));
        AskRequest.ValidateIntent("Open Blazor", intent);
        Assert.Equal("", intent.Speaker);
        Assert.Equal(2, AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices.Length);
        var wrong = AskRequest.Ground(doc, "Open Blazor", intent with { Speaker = "two" });
        Assert.Throws<InvalidDataException>(() => AskRequest.ValidateIntent("Open Blazor", wrong));
        var explicitPerson = AskRequest.Ground(doc, "Open Blazor by Elon", intent with { Speaker = "one" });
        Assert.Throws<InvalidDataException>(() => AskRequest.ValidateIntent("Open Blazor by Elon", explicitPerson));
    }

    [Fact]
    public void LookupSessionIdDoesNotTurnByInsideAnExactTitleIntoASpeaker()
    {
        var doc = AgendaDocument();
        const string title = "Synthetic extension backed by an analyzer";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title };
        doc.Sessions.Add(doc.Sessions[0] with { Id = "another" });
        var tools = CreateTools(doc, $"Open \"{title}\"", []);

        Assert.Contains("2 matches.", tools.Lookup(title));
        tools.OpenSession(title, speaker: "one");

        var result = Assert.IsType<AskResolution>(tools.Complete());
        Assert.True(result.Intent.ExactTitle);
        Assert.Equal("", result.Intent.Speaker);
        Assert.Equal(["one", "another"], result.Choices.Select(choice => choice.Id));

        tools.Initialize(doc, TimeZoneInfo.Utc, $"Open \"{title}\" by Test Person", []);
        Assert.Throws<InvalidDataException>(() => tools.OpenSession(title, speaker: "one"));
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LookupIdCannotDiscardAnOverlappingNamedSpeakerClause(bool knownSpeaker)
    {
        var doc = AgendaDocument();
        if (knownSpeaker)
            doc.Speakers.Add(new() { Id = "morgan", Name = "Synthetic Morgan Rivera" });
        var title = knownSpeaker ? "Synthetic Aspire by Synthetic Morgan Rivera" : "Synthetic Aspire by morgan rivera";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title };
        var tools = CreateTools(doc, $"Open {title}", []);

        Assert.Throws<InvalidDataException>(() =>
        {
            tools.Lookup(title);
            tools.OpenSession(title, speaker: "one");
        });
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData("elon", "Elon")]
    [InlineData("elon", "elon")]
    [InlineData("The Example", "The Example")]
    [InlineData("An Nguyen", "An Nguyen")]
    [InlineData("A. Smith", "A. Smith")]
    public void LookupIdCannotDiscardAnOverlappingUnknownSpeaker(string titleName, string requestedName)
    {
        var doc = AgendaDocument();
        var title = $"Synthetic Aspire by {titleName}";
        var query = $"Synthetic Aspire by {requestedName}";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title };
        var tools = CreateTools(doc, $"Open {query}", []);

        Assert.Throws<InvalidDataException>(() => tools.OpenSession(query, speaker: "one"));
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData("an nguyen", true)]
    [InlineData("an nguyen", false)]
    [InlineData("An", true)]
    [InlineData("An", false)]
    public void LookupIdCannotDiscardAnArticleShapedSpeaker(string name, bool knownNamePart)
    {
        var doc = AgendaDocument();
        if (knownNamePart)
            doc.Speakers.Add(new() { Id = "an", Name = "An Le" });
        var title = $"Synthetic Aspire by {name}";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title };
        var tools = CreateTools(doc, $"Open {title}", []);

        Assert.Throws<InvalidDataException>(() =>
        {
            tools.Lookup(title);
            tools.OpenSession(title, speaker: "one");
        });
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void QuotedTitleCanUseALookupIdWithoutInventingASpeaker()
    {
        var doc = AgendaDocument();
        const string title = "Synthetic Aspire by Elon";
        doc.Sessions[0] = doc.Sessions[0] with { Title = title };
        var tools = CreateTools(doc, $"Open \"{title}\"", []);

        Assert.Contains("1 matches", tools.Lookup(title));
        tools.OpenSession(title, speaker: "one");

        var result = Assert.IsType<AskResolution>(tools.Complete());
        Assert.Equal("", result.Intent.Speaker);
        Assert.Equal("one", Assert.Single(result.Choices).Id);
    }

    [Theory]
    [InlineData("Book me a hotel")]
    [InlineData("Find Rust talks")]
    [InlineData("Sessions by Elon")]
    public void ModelCannotTurnDroppedConstraintsIntoAnUnfilteredSearch(string request) =>
        Assert.Throws<InvalidDataException>(() => AskRequest.ValidateIntent(request,
            new(AskAction.FindSessions, "", "", "")));

    [Theory]
    [InlineData("Don't save Blazor")]
    [InlineData("Save Blazor if there is no conflict")]
    public void NegatedAndConditionalChangesCannotExecute(string request) =>
        Assert.Throws<InvalidDataException>(() => AskRequest.ValidateIntent(request, new(AskAction.SaveSession, "Blazor", "", "")));

    [Fact]
    public void UnknownSpeakerDoesNotBecomeBroadSearch()
    {
        var result = AskRequest.Resolve(AgendaDocument(), new(AskAction.SaveSession, "", "", "Elon"), TimeZoneInfo.Utc, []);
        Assert.Empty(result.Choices);
        Assert.Contains("speaker isn't", result.Message);
    }

    [Fact]
    public void PartialModelSpeakerCannotSelectAnotherPersonForAnExplicitName()
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([
            new() { Id = "roth", Name = "Daniel Roth" },
            new() { Id = "smith", Name = "Daniel Smith" }
        ]);
        doc.Sessions.Add(doc.Sessions[0] with { Id = "wrong", SpeakerIds = ["roth"] });
        const string request = "Save Blazor by Daniel Smith on Thursday";
        var intent = AskRequest.Ground(doc, request, new(AskAction.SaveSession, "Blazor", "", "Daniel"));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("Daniel Smith", intent.Speaker);
        Assert.Empty(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices);
    }

    [Fact]
    public void PrefixOfAnExplicitSpeakerCannotSelectSomeoneElse()
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "roth", Name = "Daniel Roth" });
        doc.Sessions[0] = doc.Sessions[0] with { SpeakerIds = ["roth"] };
        const string request = "Save Blazor by Daniel Smith on Thursday";
        var intent = AskRequest.Ground(doc, request, new(AskAction.SaveSession, "Blazor", "", "Dan"));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("Daniel Smith", intent.Speaker);
        Assert.Empty(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices);
    }

    [Theory]
    [InlineData(AskAction.SaveSession)]
    [InlineData(AskAction.RemoveSessions)]
    public void SurnamePrefixCannotSelectAnotherSpeaker(AskAction action)
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "smith", Name = "Alice Smith" });
        doc.Sessions[0] = doc.Sessions[0] with { SpeakerIds = ["smith"] };
        var request = action == AskAction.SaveSession
            ? "Save Blazor by Daniel Smith on Thursday"
            : "Remove Blazor by Daniel Smith on Thursday";
        var intent = AskRequest.Ground(doc, request, new(action, "Blazor", "", "Smi"));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("Daniel Smith", intent.Speaker);
        Assert.Empty(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, ["one"]).Choices);
    }

    [Theory]
    [InlineData(AskAction.FindSessions)]
    [InlineData(AskAction.SaveSession)]
    [InlineData(AskAction.RemoveSessions)]
    public void AFullSpeakerNameCannotSelectAPersonWithALongerSurname(AskAction action)
    {
        var doc = AgendaDocument();
        doc.Speakers.Add(new() { Id = "smithson", Name = "Daniel Smithson" });
        doc.Sessions[0] = doc.Sessions[0] with { SpeakerIds = ["smithson"] };
        var request = action switch
        {
            AskAction.FindSessions => "Find Blazor sessions by Daniel Smith",
            AskAction.SaveSession => "Save Blazor by Daniel Smith",
            _ => "Remove Blazor by Daniel Smith"
        };
        var intent = AskRequest.Ground(doc, request, new(action, "Blazor", "", "Smith"));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("Daniel Smith", intent.Speaker);
        Assert.Empty(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, ["one"]).Choices);
    }

    [Fact]
    public void SingleSaveCannotDropASecondTopicWithoutAByClause()
    {
        const string request = "Save Blazor AI on Thursday";
        var intent = AskRequest.Ground(AgendaDocument(), request, new(AskAction.SaveSession, "Blazor", "", ""));

        Assert.Throws<InvalidDataException>(() => AskRequest.ValidateIntent(request, intent));
    }

    [Fact]
    public void SingleSaveCannotDropAPossessiveSpeaker()
    {
        const string request = "Save Daniel Smith's Blazor session on Thursday";
        var intent = AskRequest.Ground(AgendaDocument(), request, new(AskAction.SaveSession, "Blazor", "", ""));

        Assert.Throws<InvalidDataException>(() => AskRequest.ValidateIntent(request, intent));
    }

    [Fact]
    public void SingleSaveCannotUseTheSpeakersNameToSatisfyADroppedTopic()
    {
        const string request = "Save Blazor by Blazor Smith on Thursday";
        var intent = AskRequest.Ground(AgendaDocument(), request,
            new(AskAction.SaveSession, "", "", "Blazor Smith"));

        Assert.Throws<InvalidDataException>(() => AskRequest.ValidateIntent(request, intent));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BulkSaveByUnqualifiedSpeakerRequiresUnambiguousScope(bool ambiguous)
    {
        var doc = AgendaDocument();
        doc.Speakers.AddRange([new() { Id = "roth", Name = "Daniel Roth" }, new() { Id = "casey", Name = "Casey" }]);
        doc.Sessions[0] = doc.Sessions[0] with { SpeakerIds = ["roth"] };
        if (ambiguous)
            doc.Sessions.Add(doc.Sessions[0] with
            {
                Id = "other", Title = "Learning from Daniel Roth", SpeakerIds = ["casey"]
            });
        const string request = "Favorite all Daniel Roth sessions";
        if (ambiguous)
        {
            Assert.Throws<InvalidDataException>(() =>
                AskRequest.Ground(doc, request, new(AskAction.SaveSession, "Daniel Roth", "", "Daniel Roth")));
            Assert.Throws<InvalidDataException>(() =>
                AskRequest.Ground(doc, request, new(AskAction.SaveSession, "Daniel Roth", "", "")));
            return;
        }
        var intent = AskRequest.Ground(doc, request, new(AskAction.SaveSession, "Daniel Roth", "", "Daniel Roth"));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("Daniel Roth", intent.Speaker);
        Assert.Equal("", intent.Query);
        Assert.Equal("one", Assert.Single(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices).Id);
        var withoutModelSpeaker = AskRequest.Ground(doc, request, new(AskAction.SaveSession, "Daniel Roth", "", ""));
        Assert.Equal("Daniel Roth", withoutModelSpeaker.Speaker);
        Assert.Equal("one", Assert.Single(AskRequest.Resolve(doc, withoutModelSpeaker, TimeZoneInfo.Utc, []).Choices).Id);
    }

    [Fact]
    public void UnknownBulkSpeakerDoesNotMatchAnUnrelatedSessionMentioningThem()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Why Elon is not a conference speaker" };
        const string request = "Favorite all Elon sessions";
        var intent = AskRequest.Ground(doc, request, new(AskAction.SaveSession, "Elon", "", "Elon"));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("Elon", intent.Speaker);
        Assert.Empty(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices);
    }

    [Fact]
    public void BulkWithUnknownSpeakerCannotSaveAnotherSpeakersTitle()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Title = "Synthetic lunch with Daniel Smith" };
        const string request = "Favorite all sessions with Daniel Smith";
        var intent = AskRequest.Ground(doc, request, new(AskAction.SaveSession, "", "", "Daniel Smith"));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("Daniel Smith", intent.Speaker);
        Assert.Equal("", intent.Query);
        Assert.Empty(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices);
        var omittedByModel = AskRequest.Ground(doc, request, new(AskAction.SaveSession, "", "", ""));
        AskRequest.ValidateIntent(request, omittedByModel);
        Assert.Equal("Daniel Smith", omittedByModel.Speaker);
        Assert.Empty(AskRequest.Resolve(doc, omittedByModel, TimeZoneInfo.Utc, []).Choices);
    }

    [Theory]
    [InlineData("add all MAUI sessions to my schedule", "MAUI", ".NET MAUI")]
    [InlineData("favorite all Aspire sessions", "Aspire", ".NET Aspire")]
    public void BulkTopicAliasOutranksMisclassifiedSpeaker(string request, string term, string topic)
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Topics = [topic] };
        doc.Sessions[1] = doc.Sessions[1] with { Title = "Synthetic unrelated session", Topics = ["Other"] };
        var tools = CreateTools(doc, request, []);

        Assert.Contains("1 matches.", tools.Lookup("", "", term));
        tools.SaveSession("", "", term);

        var pending = Assert.IsType<AskResolution>(tools.Complete());
        Assert.True(pending.Intent.AllMatches);
        Assert.Equal(term, pending.Intent.Query);
        Assert.Equal("", pending.Intent.Speaker);
        Assert.Equal("one", Assert.Single(pending.Choices).Id);
    }

    [Fact]
    public void BulkTopicAliasDoesNotMatchInsideAnotherWordOrOverrideBy()
    {
        var doc = AgendaDocument();
        doc.Sessions[0] = doc.Sessions[0] with { Topics = [".NET MAUI"] };
        var substring = AskRequest.Ground(doc, "Favorite all AI sessions",
            new(AskAction.SaveSession, "", "", "AI"));
        var explicitSpeaker = AskRequest.Ground(doc, "Favorite all sessions by MAUI",
            new(AskAction.SaveSession, "", "", "MAUI"));

        AskRequest.ValidateIntent("Favorite all AI sessions", substring);
        AskRequest.ValidateIntent("Favorite all sessions by MAUI", explicitSpeaker);
        Assert.Equal("AI", substring.Speaker);
        Assert.Empty(AskRequest.Resolve(doc, substring, TimeZoneInfo.Utc, []).Choices);
        Assert.Equal("MAUI", explicitSpeaker.Speaker);
        Assert.Empty(AskRequest.Resolve(doc, explicitSpeaker, TimeZoneInfo.Utc, []).Choices);

        doc.Speakers.Add(new() { Id = "ambiguous", Name = "MAUI" });
        Assert.Throws<InvalidDataException>(() => AskRequest.Ground(doc, "Favorite all MAUI sessions",
            new(AskAction.SaveSession, "", "", "MAUI")));
    }

    [Theory]
    [InlineData("Add all Blazor sessions by Daniel Roth to my schedule", "")]
    [InlineData("Add all Blazor sessions by Daniel Roth to my schedule on Thursday", "Thursday")]
    [InlineData("Add all Blazor sessions by Daniel Roth on Thursday to my schedule, please", "Thursday")]
    public void BulkSpeakerExcludesTheScheduleDestination(string request, string day)
    {
        var intent = AskRequest.Ground(AgendaDocument(), request,
            new(AskAction.SaveSession, "Blazor", "", "Daniel Roth"));

        AskRequest.ValidateIntent(request, intent);
        Assert.Equal("Daniel Roth", intent.Speaker);
        Assert.Equal("Blazor", intent.Query);
        Assert.Equal(day, intent.Day);
    }

    [Fact]
    public void PoliteCommasDoNotChangeABulkSaveScope()
    {
        Assert.True(AskRequest.CanOfferAction("Please, favorite all Aspire sessions", AskAction.SaveSession));
        const string request = "Add all Aspire sessions to my schedule, please";
        var intent = AskRequest.Ground(AgendaDocument(), request, new(AskAction.SaveSession, "Aspire", "", ""));

        AskRequest.ValidateIntent(request, intent);
        Assert.True(intent.AllMatches);
        Assert.Equal("Aspire", intent.Query);
    }

    [Fact]
    public void SaveScopeNormalizesDestinationAndMatchingWeekdayDate()
    {
        var doc = AgendaDocument();
        const string singular = "Save Blazor to my schedule on Thursday";
        var one = AskRequest.Ground(doc, singular, new(AskAction.SaveSession, "Blazor", "", ""));
        AskRequest.ValidateIntent(singular, one);
        Assert.Equal("one", Assert.Single(AskRequest.Resolve(doc, one, TimeZoneInfo.Utc, []).Choices).Id);

        const string bulk = "Add all Blazor sessions on Thursday 2026-11-12";
        var all = AskRequest.Ground(doc, bulk, new(AskAction.SaveSession, "Blazor", "", ""));
        AskRequest.ValidateIntent(bulk, all);
        Assert.Equal("Blazor", all.Query);
        Assert.Equal("2026-11-12", all.Day);
        Assert.Equal("one", Assert.Single(AskRequest.Resolve(doc, all, TimeZoneInfo.Utc, []).Choices).Id);
    }

    [Fact]
    public void SingleSaveCannotDropTheTopicBeforeAnExplicitSpeaker()
    {
        const string request = "Save Blazor by Test Person on Thursday";
        var intent = AskRequest.Ground(AgendaDocument(), request,
            new(AskAction.SaveSession, "", "", "Test Person"));

        Assert.Throws<InvalidDataException>(() => AskRequest.ValidateIntent(request, intent));
        AskRequest.ValidateIntent(request, intent with { Query = "Blazor" });
        AskRequest.ValidateIntent("Save the session by Test Person",
            new(AskAction.SaveSession, "", "", "Test Person"));
        Assert.Throws<InvalidDataException>(() => AskRequest.ValidateIntent(
            "Save Blazor AI by Test Person", intent with { Query = "Blazor", Day = "" }));
    }

    [Fact]
    public void OpenSpeakerResolvesFromSessionAndDirectName()
    {
        var doc = AgendaDocument();
        Assert.Equal("speaker", Assert.Single(AskRequest.Resolve(doc,
            new(AskAction.OpenSpeaker, "Blazor", "", ""), TimeZoneInfo.Utc, []).Choices).Id);
        Assert.Equal("speaker", Assert.Single(AskRequest.Resolve(doc,
            new(AskAction.OpenSpeaker, "", "", "Test Person"), TimeZoneInfo.Utc, []).Choices).Id);
    }

    [Fact]
    public void RemovalOnlySelectsSavedSessionsIncludingWithdrawn()
    {
        var doc = AgendaDocument();
        var withdrawn = doc.Sessions[1];
        doc = doc with { Sessions = [doc.Sessions[0]], WithdrawnSessions = [withdrawn] };
        var result = AskRequest.Resolve(doc, new(AskAction.RemoveSessions, "", "Friday", ""), TimeZoneInfo.Utc, ["two", "unknown"]);
        Assert.Equal("two", Assert.Single(result.Choices).Id);
    }

    [Fact]
    public void ExplicitRemoveEverythingScopeCannotBeNarrowedByModelLookupResults()
    {
        var doc = AgendaDocument();
        var model = new AskIntent(AskAction.RemoveSessions, "Synthetic Aspire session", "Friday", "Test Person");
        var intent = AskRequest.Ground(doc, "Remove everything on Friday", model);
        AskRequest.ValidateIntent("Remove everything on Friday", intent);
        Assert.Equal("", intent.Query);
        Assert.Equal("", intent.Speaker);
        Assert.Equal("two", Assert.Single(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, ["one", "two"]).Choices).Id);
        var person = AskRequest.Ground(doc, "Remove everything by Elon on Friday", model);
        Assert.Equal(model.Speaker, person.Speaker);
        Assert.Throws<InvalidDataException>(() => AskRequest.ValidateIntent("Remove everything by Elon on Friday", person));
        var topic = AskRequest.Ground(doc, "Remove all Blazor on Friday", model with { Query = "Blazor", Speaker = "" });
        Assert.Equal("Blazor", topic.Query);
        Assert.Throws<InvalidDataException>(() => AskRequest.ValidateIntent("Remove all Blazor on Friday",
            new(AskAction.RemoveSessions, "", "Friday", "")));
        Assert.Throws<InvalidDataException>(() => AskRequest.ValidateIntent("Remove all Elon sessions",
            new(AskAction.RemoveSessions, "", "", "")));
    }

    [Fact]
    public void RepeatedWeekdaysRequireARealDateChoice()
    {
        var doc = AgendaDocument();
        doc.Sessions.Add(doc.Sessions[0] with { Id = "next", StartsAt = doc.Sessions[0].StartsAt.AddDays(7) });
        var result = AskRequest.Resolve(doc, new(AskAction.SaveSession, "Blazor", "Thursday", ""), TimeZoneInfo.Utc, []);
        Assert.True(result.ChooseDay);
        Assert.Equal(2, result.Choices.Length);
    }

    [Fact]
    public async Task RemovalIsAtomicAndRejectsChangedSnapshot()
    {
        var store = new TestStore(["one", "two", "unknown"]);
        var schedule = new ScheduleService(store);
        await schedule.InitializeAsync();
        Assert.False(await schedule.RemoveReviewedAsync(["one"], ["one", "two"], () => true));
        Assert.Equal(0, store.Writes);
        Assert.False(await schedule.RemoveReviewedAsync(["one"], schedule.SavedIds, () => false));
        Assert.True(await schedule.RemoveReviewedAsync(["one", "two"], schedule.SavedIds, () => true));
        Assert.Equal(1, store.Writes);
        Assert.Equal(["unknown"], schedule.SavedIds);
    }

    [Fact]
    public async Task FailedRemovalKeepsPublishedState()
    {
        var store = new TestStore(["one"]) { FailWrites = true };
        var schedule = new ScheduleService(store);
        await schedule.InitializeAsync();
        await Assert.ThrowsAsync<IOException>(() => schedule.RemoveReviewedAsync(["one"], ["one"], () => true));
        Assert.True(schedule.IsSaved("one"));
    }

    [Fact]
    public async Task SaveUsesDesiredStateAndPreservesOtherBookmarks()
    {
        var store = new TestStore(["unknown"]);
        var schedule = new ScheduleService(store);
        await schedule.SetSavedAsync("one", true);
        await schedule.SetSavedAsync("one", true);
        Assert.Equal(1, store.Writes);
        Assert.Equal(["one", "unknown"], schedule.SavedIds);
    }

    [Theory]
    [InlineData("favorite all Aspire sessions", "Aspire", "", "")]
    [InlineData("add all MAUI sessions to my schedule", "MAUI", "", "")]
    [InlineData("favorite all Aspire sessions please", "Aspire", "", "")]
    [InlineData("add all MAUI sessions to my schedule on Tuesday", "MAUI", "Tuesday", "")]
    [InlineData("add all MAUI sessions to my schedule on Tuesday please", "MAUI", "Tuesday", "")]
    [InlineData("favourite every Blazor talk", "Blazor", "", "")]
    [InlineData("Save all of the talks about Blazor on Thursday", "Blazor", "Thursday", "")]
    [InlineData("Bookmark all sessions by Test Person on Friday", "", "Friday", "Test Person")]
    [InlineData("Save all sessions", "", "", "")]
    [InlineData("Save everything on Friday", "", "Friday", "")]
    public void ExplicitBulkSaveGroundsTheFullLiteralScope(string request, string query, string day, string speaker)
    {
        var intent = AskRequest.Ground(AgendaDocument(), request,
            new(AskAction.SaveSession, "Synthetic Aspire session", "", "invented"));
        AskRequest.ValidateIntent(request, intent);
        Assert.True(intent.AllMatches);
        Assert.Equal(query, intent.Query);
        Assert.Equal(day, intent.Day);
        Assert.Equal(speaker, intent.Speaker);
        Assert.True(AskRequest.CanOfferAction(request, AskAction.SaveSession));
    }

    [Fact]
    public async Task BulkLookupAndSaveUseTheFullLiteralScopeBeyondTheLookupPage()
    {
        var doc = AgendaDocument();
        doc = doc with { Sessions = Enumerable.Range(0, 15).Select(i => doc.Sessions[0] with { Id = $"synthetic-{i}" }).ToList() };
        var tools = CreateTools(doc, "favorite all Blazor sessions", []);
        using var services = new ServiceCollection().AddSingleton(tools).BuildServiceProvider();
        var functions = tools.CreateFunctions(services).Cast<AIFunction>().ToDictionary(f => f.Name);
        var lookup = await functions["lookup_sessions"].InvokeAsync(
            new AIFunctionArguments { ["query"] = ".NET Blazor", ["speaker"] = "invented" });
        Assert.Contains("15 matches.", lookup?.ToString());
        Assert.Null(tools.Complete());
        Assert.Null(tools.Failure);
        await functions["save_sessions"].InvokeAsync(new AIFunctionArguments { ["query"] = doc.Sessions[0].Title });
        Assert.True(tools.Complete()!.Intent.AllMatches);
        Assert.Equal(15, tools.Complete()!.Choices.Length);
    }

    [Fact]
    public async Task SingleSaveCanBrowseBeforeStagingAValidatedAction()
    {
        var tools = CreateTools(AgendaDocument(), "Save Blazor", []);
        using var services = new ServiceCollection().AddSingleton(tools).BuildServiceProvider();
        var functions = tools.CreateFunctions(services).Cast<AIFunction>().ToDictionary(f => f.Name);
        var lookup = await functions["lookup_sessions"].InvokeAsync(new AIFunctionArguments { ["query"] = "" });
        Assert.Contains("2 matches.", lookup?.ToString());
        Assert.Null(tools.Complete());

        await functions["save_sessions"].InvokeAsync(new AIFunctionArguments { ["query"] = "Blazor" });
        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
    }

    [Fact]
    public async Task UnfilteredLookupCannotBroadenTheFinalSaveOrRemoval()
    {
        var save = new AskTools();
        save.Initialize(AgendaDocument(), TimeZoneInfo.Utc, "Save Blazor", []);
        using var services = new ServiceCollection().AddSingleton(save).BuildServiceProvider();
        var functions = save.CreateFunctions(services).Cast<AIFunction>().ToDictionary(f => f.Name);
        await functions["lookup_sessions"].InvokeAsync(new AIFunctionArguments { ["query"] = "" });
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            functions["save_sessions"].InvokeAsync(new AIFunctionArguments { ["query"] = "" }).AsTask());
        Assert.Null(save.Complete());

        var removal = new AskTools();
        removal.Initialize(AgendaDocument(), TimeZoneInfo.Utc, "Remove Blazor", ["one"]);
        Assert.Contains("1 matches.", removal.Lookup(""));
        Assert.Throws<InvalidDataException>(() => removal.RequestRemoval());
        Assert.Null(removal.Complete());

        var search = new AskTools();
        search.Initialize(AgendaDocument(), TimeZoneInfo.Utc, "Find Rust talks", []);
        Assert.Contains("2 matches.", search.Lookup(""));
        Assert.Throws<InvalidDataException>(() => search.FindSessions());
        Assert.Null(search.Complete());

        var speaker = new AskTools();
        speaker.Initialize(AgendaDocument(), TimeZoneInfo.Utc, "Save Blazor by Test Person", []);
        Assert.Contains("2 matches.", speaker.Lookup(""));
        Assert.Throws<InvalidDataException>(() => speaker.SaveSession("Blazor"));
        Assert.Null(speaker.Complete());
    }

    [Fact]
    public void OmittedConstraintsCannotProduceAStagedSearchOrRemoval()
    {
        var search = new AskTools();
        search.Initialize(AgendaDocument(), TimeZoneInfo.Utc, "Find Blazor AI talks on Thursday", []);
        Assert.Throws<InvalidDataException>(() => search.FindSessions("Blazor"));
        Assert.Null(search.Complete());

        var removal = new AskTools();
        removal.Initialize(AgendaDocument(), TimeZoneInfo.Utc, "Remove saved Blazor sessions with Sam Taylor", ["one"]);
        Assert.Throws<InvalidDataException>(() => removal.RequestRemoval("Blazor"));
        Assert.Null(removal.Complete());
    }

    [Fact]
    public void UnfilteredLookupStillRejectsAnInventedDay()
    {
        var tools = CreateTools(AgendaDocument(), "Save Blazor", []);
        Assert.Throws<InvalidDataException>(() => tools.Lookup("", "Friday"));
    }

    [Fact]
    public void BulkSaveDoesNotSilentlyIntersectDifferentTopics()
    {
        foreach (var separator in new[] { "and", "or", "," })
            Assert.Throws<InvalidDataException>(() => AskRequest.Ground(AgendaDocument(),
                $"favorite all MAUI {separator} Aspire sessions", new(AskAction.SaveSession, "MAUI", "", "")));
    }

    [Fact]
    public void BulkSavePreservesActualTitlesAndSpeakerNamesWithConjunctions()
    {
        const string title = "Synthetic Blazor and Aspire";
        var doc = AgendaDocument();
        doc = doc with { Sessions = [doc.Sessions[0] with { Title = title }] };
        var intent = AskRequest.Ground(doc, $"Save all {title} sessions", new(AskAction.SaveSession, "", "", ""));
        AskRequest.ValidateIntent($"Save all {title} sessions", intent);
        Assert.Equal(title, intent.Query);
        Assert.Single(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, []).Choices);
        var speaker = AskRequest.Ground(doc, "Save all sessions by Test and Person", new(AskAction.SaveSession, "", "", ""));
        Assert.Equal("Test and Person", speaker.Speaker);
    }

    [Fact]
    public async Task NativeDuplicateProposalsStageOneActionWithoutRacing()
    {
        var tools = CreateTools(AgendaDocument(), "favorite all Blazor sessions", []);
        using var services = new ServiceCollection().AddSingleton(tools).BuildServiceProvider();
        var save = tools.CreateFunctions(services).Cast<AIFunction>().Single(f => f.Name == "save_sessions");
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            await save.InvokeAsync(new AIFunctionArguments { ["query"] = "Blazor" }))));
        Assert.Null(tools.Failure);
        Assert.NotEmpty(tools.Complete()!.Choices);
        Assert.True(tools.Complete()!.Intent.AllMatches);
    }

    [Fact]
    public async Task DifferentProposalsStillInvalidateNativeDuplicateHandling()
    {
        var tools = CreateTools(AgendaDocument(), "Find Blazor Aspire", []);
        using var services = new ServiceCollection().AddSingleton(tools).BuildServiceProvider();
        var find = tools.CreateFunctions(services).Cast<AIFunction>().Single(f => f.Name == "find_sessions");
        await find.InvokeAsync(new AIFunctionArguments { ["query"] = "Blazor Aspire" });
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            find.InvokeAsync(new AIFunctionArguments { ["query"] = "Aspire Blazor" }).AsTask());
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void SingularSaveCannotBePromotedToBulkAndUnknownConstraintsStayGrounded()
    {
        var doc = AgendaDocument();
        Assert.False(AskRequest.Ground(doc, "Save Blazor",
            new(AskAction.SaveSession, "Blazor", "", "", true)).AllMatches);
        var unknown = AskRequest.Ground(doc, "Favorite all sessions by Elon",
            new(AskAction.SaveSession, "", "", ""));
        Assert.Equal("Elon", unknown.Speaker);
        Assert.Empty(AskRequest.Resolve(doc, unknown, TimeZoneInfo.Utc, []).Choices);
        var scoped = AskRequest.Ground(doc, "Save all Rust sessions",
            new(AskAction.SaveSession, "", "", ""));
        Assert.Equal("Rust", scoped.Query);
        Assert.Empty(AskRequest.Resolve(doc, scoped, TimeZoneInfo.Utc, []).Choices);
    }

    [Fact]
    public async Task BulkAdditionIsOneIdempotentWriteAndPreservesLatestOtherBookmarks()
    {
        var store = new TestStore(["unknown"]);
        var schedule = new ScheduleService(store);
        await schedule.AddAsync(["one", "two", "one"]);
        await schedule.AddAsync(["one", "two"]);
        Assert.Equal(1, store.Writes);
        Assert.Equal(["one", "two", "unknown"], schedule.SavedIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveViewModelAppliesTheWholeGroundedSetAndNavigatesOnlyAfterSuccess(bool fail)
    {
        var doc = AgendaDocument();
        var local = Substitute.For<IOnDeviceAsk>();
        var app = Substitute.For<IAskApplication>();
        app.AskDocument.Returns(doc);
        app.AskTimeZone.Returns(TimeZoneInfo.Utc);
        var schedule = new ScheduleService(new TestStore([]));
        local.InterpretAsync(doc, TimeZoneInfo.Utc, "Save all sessions", Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(AskRequest.Resolve(doc, new(AskAction.SaveSession, "", "", "", true), TimeZoneInfo.Utc, []));
        app.SaveAskSessionsAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>()).Returns(call =>
            fail ? Task.FromException<bool>(new IOException("Synthetic storage failure.")) : Task.FromResult(true));
        var vm = new AskViewModel(local, app, schedule, NullLogger<AskViewModel>.Instance);
        var dismissed = false;
        vm.BeforeNavigation += () => { dismissed = true; return Task.CompletedTask; };
        vm.Open();
        vm.Request = "Save all sessions";
        await vm.SubmitCommand.ExecuteAsync(null);
        await app.Received(1).SaveAskSessionsAsync(
            Arg.Is<string[]>(ids => ids.SequenceEqual(new[] { "one", "two" })), Arg.Any<CancellationToken>());
        Assert.False(vm.NeedsChoice);
        Assert.False(vm.NeedsApproval);
        Assert.Equal(!fail, dismissed);
        if (fail)
        {
            await app.DidNotReceive().OpenAskScheduleAsync(Arg.Any<bool>());
            Assert.Contains("Couldn't", vm.Message);
        }
        else
            await app.Received(1).OpenAskScheduleAsync(true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AskShowsTheCommittedOutcomeWhenPresentationFails(bool save)
    {
        var doc = AgendaDocument();
        var local = Substitute.For<IOnDeviceAsk>();
        var app = Substitute.For<IAskApplication>();
        app.AskDocument.Returns(doc);
        app.AskTimeZone.Returns(TimeZoneInfo.Utc);
        var schedule = new ScheduleService(new TestStore(["one"]));
        var request = save ? "Save all sessions" : "Remove everything";
        var intent = save ? new AskIntent(AskAction.SaveSession, "", "", "", true)
            : new AskIntent(AskAction.RemoveSessions, "", "", "");
        local.InterpretAsync(doc, TimeZoneInfo.Utc, request, Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(AskRequest.Resolve(doc, intent, TimeZoneInfo.Utc, ["one"]));
        var message = save ? "Your sessions were saved, but the display failed."
            : "Your sessions were removed, but the display failed.";
        var exception = new SchedulePresentationException(message, new InvalidOperationException("Synthetic display failure"));
        app.SaveAskSessionsAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(exception));
        app.RemoveAskSessionsAsync(Arg.Any<string[]>(), Arg.Any<string[]>(), doc, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool?>(exception));
        var vm = new AskViewModel(local, app, schedule, NullLogger<AskViewModel>.Instance);
        vm.Open();
        vm.Request = request;

        await vm.SubmitCommand.ExecuteAsync(null);
        if (!save)
            await vm.ConfirmRemovalCommand.ExecuteAsync(null);

        Assert.Equal(message, vm.Message);
        Assert.False(vm.NeedsApproval);
        await app.DidNotReceive().OpenAskScheduleAsync(Arg.Any<bool>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StoppingDuringSaveOrDismissalNeverNavigates(bool duringDismissal)
    {
        var doc = AgendaDocument();
        var local = Substitute.For<IOnDeviceAsk>();
        var app = Substitute.For<IAskApplication>();
        app.AskDocument.Returns(doc);
        app.AskTimeZone.Returns(TimeZoneInfo.Utc);
        local.InterpretAsync(doc, TimeZoneInfo.Utc, "Save Blazor", Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(AskRequest.Resolve(doc, new(AskAction.SaveSession, "Blazor", "", ""), TimeZoneInfo.Utc, []));
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.SaveAskSessionsAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            if (!duringDismissal)
            {
                reached.SetResult();
                await release.Task;
            }
            return true;
        });
        var vm = new AskViewModel(local, app, new ScheduleService(new TestStore([])), NullLogger<AskViewModel>.Instance);
        vm.BeforeNavigation += async () =>
        {
            if (duringDismissal)
            {
                reached.SetResult();
                await release.Task;
            }
        };
        vm.Open();
        vm.Request = "Save Blazor";
        var submission = vm.SubmitCommand.ExecuteAsync(null);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));

        vm.StopCommand.Execute(null);
        release.SetResult();
        await submission;

        await app.DidNotReceive().OpenAskScheduleAsync(Arg.Any<bool>());
    }

    [Fact]
    public async Task SingularAmbiguityAndZeroBulkMatchesNeverSaveOrNavigate()
    {
        var doc = AgendaDocument();
        var local = Substitute.For<IOnDeviceAsk>();
        var app = Substitute.For<IAskApplication>();
        app.AskDocument.Returns(doc);
        app.AskTimeZone.Returns(TimeZoneInfo.Utc);
        local.InterpretAsync(doc, TimeZoneInfo.Utc, Arg.Any<string>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(call => AskRequest.Resolve(doc, call.ArgAt<string>(2) == "Save all Rust"
                ? new(AskAction.SaveSession, "Rust", "", "", true)
                : new(AskAction.SaveSession, "Synthetic", "", ""), TimeZoneInfo.Utc, []));
        var vm = new AskViewModel(local, app, new ScheduleService(new TestStore([])), NullLogger<AskViewModel>.Instance);
        vm.Open();
        vm.Request = "Save Synthetic";
        await vm.SubmitCommand.ExecuteAsync(null);
        Assert.True(vm.NeedsChoice);
        Assert.Equal(2, vm.Choices.Length);
        vm.Request = "Save all Rust";
        await vm.SubmitCommand.ExecuteAsync(null);
        Assert.Empty(vm.Choices);
        await app.DidNotReceive().SaveAskSessionsAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>());
        await app.DidNotReceive().OpenAskScheduleAsync(Arg.Any<bool>());
    }

    [Fact]
    public async Task GeneratedFunctionsAreCuratedGroundedAndOnlyStageActions()
    {
        var tools = CreateTools(AgendaDocument(), "Save Blazor", []);
        using var services = new ServiceCollection().AddSingleton(tools).BuildServiceProvider();
        var functions = tools.CreateFunctions(services).Cast<AIFunction>().ToDictionary(f => f.Name);
        Assert.Equal(["lookup_sessions", "save_sessions"], functions.Keys.Order());
        Assert.Equal(6, AskToolContext.Default.Tools.Count);
        Assert.IsType<ApprovalRequiredAIFunction>(AskToolContext.Default.Tools.Single(f => f.Name == "request_removal"));
        Assert.Contains("one", Assert.IsType<string>(await functions["lookup_sessions"].InvokeAsync(
            new AIFunctionArguments { ["query"] = "Blazor" })));
        await functions["save_sessions"].InvokeAsync(new AIFunctionArguments { ["query"] = "Blazor" });
        Assert.Equal("one", Assert.Single(tools.Complete()!.Choices).Id);
        Assert.Throws<InvalidDataException>(() => tools.RequestAction("open_session", "Blazor"));
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunnerHandlesToolInvocationAndApprovalWithoutSideEffects(bool removal)
    {
        using var services = new ServiceCollection()
            .AddScoped<AskTools>()
            .AddTransient<IChatClient>(_ => new ActionClient(removal))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var status = Substitute.For<IPlannerModelStatus>();
        status.ReadAsync(Arg.Any<CancellationToken>()).Returns(new PlannerModelState(true, true, "") { ToolCalling = true });
        var runner = new OnDeviceAsk(services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IServiceProviderIsService>(), status);
        var result = await runner.InterpretAsync(AgendaDocument(), TimeZoneInfo.Utc,
            removal ? "Remove everything on Thursday" : "Save Blazor", ["one"], CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(removal ? AskAction.RemoveSessions : AskAction.SaveSession, result.Intent.Action);
        Assert.Equal("one", Assert.Single(result.Choices).Id);
    }

    [Fact]
    public async Task ReadinessRequiresToolCapabilityAndCancellationPreventsInference()
    {
        using var services = new ServiceCollection().AddScoped<AskTools>()
            .AddTransient<IChatClient>(_ => new ActionClient(false)).BuildServiceProvider();
        var status = Substitute.For<IPlannerModelStatus>();
        status.ReadAsync(Arg.Any<CancellationToken>()).Returns(new PlannerModelState(true, true, ""));
        var runner = new OnDeviceAsk(services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IServiceProviderIsService>(), status);
        Assert.False(await runner.IsAvailableAsync(default));
        await Assert.ThrowsAsync<NotSupportedException>(() => runner.InterpretAsync(AgendaDocument(),
            TimeZoneInfo.Utc, "Save Blazor", [], default));
        status.ReadAsync(Arg.Any<CancellationToken>()).Returns(new PlannerModelState(true, true, "") { ToolCalling = true });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.InterpretAsync(AgendaDocument(),
            TimeZoneInfo.Utc, "Save Blazor", [], new CancellationToken(true)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeminiRunnerUsesTheCapturedProfileAndPreservesContinuationAndApproval(bool removal)
    {
        const string modelId = "gemini-nano-preview-fast";
        var client = new ActionClient(removal, modelId);
        using var services = new ServiceCollection().AddScoped<AskTools>()
            .AddTransient<IChatClient>(_ => client).BuildServiceProvider();
        var status = Substitute.For<IPlannerModelStatus>();
        status.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            new PlannerModelState(true, false, "") { ToolCalling = true, ModelId = modelId },
            new PlannerModelState(true, false, "") { ToolCalling = true, ModelId = "gemini-nano-preview-full" });
        var runner = new OnDeviceAsk(services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IServiceProviderIsService>(), status);

        var result = await runner.InterpretAsync(AgendaDocument(), TimeZoneInfo.Utc,
            removal ? "Remove everything on Thursday" : "Save Blazor", ["one"], default);

        Assert.Equal(removal ? AskAction.RemoveSessions : AskAction.SaveSession, result!.Intent.Action);
        Assert.Equal("one", Assert.Single(result.Choices).Id);
        Assert.Equal(removal ? 1 : 2, client.Calls);
        await status.Received(1).ReadAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GeminiSchemaRejectsUnknownArgumentsAndInvalidatesAnEarlierStagedAction()
    {
        var tools = CreateTools(AgendaDocument(), "Save Blazor", []);
        using var services = new ServiceCollection().AddSingleton(tools).BuildServiceProvider();
        var functions = tools.CreateFunctions(services, useGeminiSchema: true).Cast<AIFunction>().ToArray();
        Assert.All(functions, function => Assert.False(function.JsonSchema.TryGetProperty("additionalProperties", out _)));
        var save = functions.Single(function => function.Name == "save_sessions");
        await save.InvokeAsync(new AIFunctionArguments { ["query"] = "Blazor" });
        Assert.NotNull(tools.Complete());

        await Assert.ThrowsAsync<InvalidDataException>(() => save.InvokeAsync(
            new AIFunctionArguments { ["query"] = "Blazor", ["unexpected"] = "Synthetic argument" }).AsTask());

        Assert.Null(tools.Complete());
        Assert.NotNull(tools.Failure);
    }

    [Fact]
    public async Task AnyLaterToolFailureInvalidatesAnAlreadyStagedAction()
    {
        var tools = CreateTools(AgendaDocument(), "Save Blazor", []);
        using var services = new ServiceCollection().AddSingleton(tools).BuildServiceProvider();
        var functions = tools.CreateFunctions(services).Cast<AIFunction>().ToDictionary(f => f.Name);
        await functions["save_sessions"].InvokeAsync(new AIFunctionArguments { ["query"] = "Blazor" });
        Assert.NotNull(tools.Complete());
        await Assert.ThrowsAsync<InvalidDataException>(() => functions["lookup_sessions"]
            .InvokeAsync(new AIFunctionArguments { ["query"] = null }).AsTask());
        Assert.Null(tools.Complete());
    }

    [Fact]
    public async Task ToolBudgetFailureCannotLeaveAnExecutableAction()
    {
        var tools = CreateTools(AgendaDocument(), "Save Blazor", []);
        using var services = new ServiceCollection().AddSingleton(tools).BuildServiceProvider();
        var functions = tools.CreateFunctions(services).Cast<AIFunction>().ToDictionary(f => f.Name);
        await functions["save_sessions"].InvokeAsync(new AIFunctionArguments { ["query"] = "Blazor" });
        for (var i = 0; i < 11; i++)
            await functions["lookup_sessions"].InvokeAsync(new AIFunctionArguments { ["query"] = "Blazor" });
        await Assert.ThrowsAsync<InvalidDataException>(() => functions["lookup_sessions"]
            .InvokeAsync(new AIFunctionArguments { ["query"] = "Blazor" }).AsTask());
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void GeneratedFunctionsRejectAnotherRequestsScope()
    {
        var tools = CreateTools(AgendaDocument(), "Save Blazor", []);
        using var services = new ServiceCollection().AddScoped<AskTools>().BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => tools.CreateFunctions(services));
    }

    [Theory]
    [InlineData("Blazor on Thursday", "find_sessions,lookup_sessions")]
    [InlineData("Save Blazor", "lookup_sessions,save_sessions")]
    [InlineData("favorite all Aspire sessions", "lookup_sessions,save_sessions")]
    [InlineData("Remove everything on Friday", "lookup_sessions,request_removal")]
    [InlineData("Who's giving the Blazor talk?", "lookup_sessions,open_speaker")]
    [InlineData("Open Test Person", "lookup_sessions,open_session,open_speaker")]
    [InlineData("Open Blazor", "lookup_sessions,open_session,open_speaker")]
    [InlineData("Find A Clear Explanation", "find_sessions,lookup_sessions")]
    [InlineData("Save The Clear Path", "lookup_sessions,save_sessions")]
    [InlineData("Open How to Save Files", "lookup_sessions,open_session,open_speaker")]
    public void ModelOnlyReceivesCapabilitiesRelevantToTheRequest(string request, string names)
    {
        var tools = CreateTools(AgendaDocument(), request, []);
        using var services = new ServiceCollection().AddSingleton(tools).BuildServiceProvider();
        Assert.Equal(names, string.Join(",", tools.CreateFunctions(services).Select(f => f.Name).Order()));
    }

    [Fact]
    public void AdditionalTerminalCallsCannotChangeAStagedSearch()
    {
        var tools = CreateTools(AgendaDocument(), "Find Blazor", []);
        tools.FindSessions("Blazor");
        Assert.Throws<InvalidDataException>(() => tools.OpenSession("Blazor"));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public void ReadOnlyRemovalLookupPreservesExplicitAllScopeAndSavedMembership()
    {
        var tools = CreateTools(AgendaDocument(), "Remove everything on Friday", ["two", "unknown"]);
        var result = tools.Lookup("", "Friday");
        Assert.Contains("two | Synthetic Aspire session", result);
        Assert.DoesNotContain("one |", result);
        Assert.DoesNotContain("unknown |", result);
        Assert.Null(tools.Complete());
    }

    [Theory]
    [InlineData("save_sessions", "Aspire", "")]
    [InlineData("save_sessions", "Blazor", "Monday")]
    [InlineData("remove_sessions", "Blazor", "")]
    public void FabricatedConstraintsAndUnrequestedMutationsAreRejected(string action, string query, string day)
    {
        var tools = CreateTools(AgendaDocument(), "Save Blazor", []);
        Assert.Throws<InvalidDataException>(() => tools.RequestAction(action, query, day));
        Assert.Null(tools.Complete());
    }

    [Fact]
    public async Task ViewModelCannotRemoveBeforeHumanConfirmationOrAfterDismissal()
    {
        var doc = AgendaDocument();
        var local = Substitute.For<IOnDeviceAsk>();
        var app = Substitute.For<IAskApplication>();
        app.AskDocument.Returns(doc);
        app.AskTimeZone.Returns(TimeZoneInfo.Utc);
        var store = new TestStore(["one"]);
        var schedule = new ScheduleService(store);
        local.InterpretAsync(doc, TimeZoneInfo.Utc, "Remove everything", Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(AskRequest.Resolve(doc, new(AskAction.RemoveSessions, "", "", ""), TimeZoneInfo.Utc, ["one"]));
        var vm = new AskViewModel(local, app, schedule, NullLogger<AskViewModel>.Instance);
        vm.Open();
        vm.Request = "Remove everything";
        await vm.SubmitCommand.ExecuteAsync(null);
        Assert.True(vm.NeedsApproval);
        await app.DidNotReceive().RemoveAskSessionsAsync(Arg.Any<string[]>(), Arg.Any<string[]>(), doc, Arg.Any<CancellationToken>());
        vm.Close();
        await vm.ConfirmRemovalCommand.ExecuteAsync(null);
        await app.DidNotReceive().RemoveAskSessionsAsync(Arg.Any<string[]>(), Arg.Any<string[]>(), doc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApprovalIsConsumedOnceAndStaleAgendaCannotApply()
    {
        var doc = AgendaDocument();
        var local = Substitute.For<IOnDeviceAsk>();
        var app = Substitute.For<IAskApplication>();
        app.AskDocument.Returns(doc);
        app.AskTimeZone.Returns(TimeZoneInfo.Utc);
        var schedule = new ScheduleService(new TestStore(["one"]));
        local.InterpretAsync(doc, TimeZoneInfo.Utc, "Remove everything", Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(AskRequest.Resolve(doc, new(AskAction.RemoveSessions, "", "", ""), TimeZoneInfo.Utc, ["one"]));
        app.RemoveAskSessionsAsync(Arg.Any<string[]>(), Arg.Any<string[]>(), doc, Arg.Any<CancellationToken>()).Returns(true);
        var vm = new AskViewModel(local, app, schedule, NullLogger<AskViewModel>.Instance);
        vm.Open();
        vm.Request = "Remove everything";
        await vm.SubmitCommand.ExecuteAsync(null);
        await vm.ConfirmRemovalCommand.ExecuteAsync(null);
        await vm.ConfirmRemovalCommand.ExecuteAsync(null);
        await app.Received(1).RemoveAskSessionsAsync(Arg.Is<string[]>(ids => ids.SequenceEqual(new[] { "one" })),
            Arg.Any<string[]>(), doc, Arg.Any<CancellationToken>());
        await vm.SubmitCommand.ExecuteAsync(null);
        app.AskDocument.Returns(AgendaDocument());
        await vm.ConfirmRemovalCommand.ExecuteAsync(null);
        await app.Received(1).RemoveAskSessionsAsync(Arg.Any<string[]>(), Arg.Any<string[]>(), doc, Arg.Any<CancellationToken>());
        Assert.Contains("changed", vm.Message);
    }

    private static AskTools CreateTools(ConferenceDocument document, string request, string[] savedIds)
    {
        var tools = new AskTools();
        tools.Initialize(document, TimeZoneInfo.Utc, request, savedIds);
        return tools;
    }

    private sealed class TestStore(string[] initial) : IScheduleStore
    {
        private string[] ids = initial;
        public int Writes { get; private set; }
        public bool FailWrites { get; init; }
        public Task<IReadOnlyCollection<string>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<string>>(ids);
        public Task WriteAsync(IReadOnlyCollection<string> sessionIds, CancellationToken cancellationToken = default)
        {
            if (FailWrites) throw new IOException("Synthetic storage failure.");
            ids = sessionIds.ToArray();
            Writes++;
            return Task.CompletedTask;
        }
    }

    private sealed class ActionClient(bool removal, string? expectedModelId = null) : IChatClient
    {
        private int calls;
        public int Calls => calls;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expectedModelId is not null)
            {
                Assert.NotNull(options);
                Assert.NotNull(options.Tools);
                Assert.Equal(expectedModelId, options.ModelId);
                Assert.All(options.Tools.Cast<AIFunction>(),
                    function => Assert.False(function.JsonSchema.TryGetProperty("additionalProperties", out _)));
                if (removal)
                    Assert.IsType<ApprovalRequiredAIFunction>(options.Tools.Single(tool => tool.Name == "request_removal"));
                if (calls > 0)
                    Assert.Contains("Action staged", Assert.IsType<string>(
                        Assert.Single(messages.SelectMany(message => message.Contents)
                            .OfType<FunctionResultContent>()).Result));
            }
            if (++calls == 1)
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent("action", removal ? "request_removal" : "save_sessions",
                        removal ? new Dictionary<string, object?> { ["day"] = "Thursday" }
                        : new Dictionary<string, object?> { ["query"] = "Blazor" })])));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Not a trusted success message.")));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
