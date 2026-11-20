using DoseKeeper.Functions.Triggers;

namespace DoseKeeper.Functions.UnitTests;

public sealed class NotificationParsingTests
{
    [Fact]
    public void A_complete_message_parses()
    {
        var message = NotificationFunctions.Parse(
            """{"id":"ntf-1","kind":"Reminder","recipient":"user","text":"Time for Metformin","createdUtc":"2026-01-10T06:00:00Z"}""");

        message.Kind.ShouldBe(DoseKeeper.Application.NotificationKind.Reminder);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"id":"ntf-1","kind":"Reminder","recipient":"","text":"x","createdUtc":"2026-01-10T06:00:00Z"}""")]
    [InlineData("null")]
    public void Broken_messages_throw_so_the_runtime_retries_and_poisons_them(string body)
    {
        Should.Throw<Exception>(() => NotificationFunctions.Parse(body));
    }
}
