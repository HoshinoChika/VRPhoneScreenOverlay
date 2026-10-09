using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AndroidMediaPlaybackControlTests
{
    [Theory]
    [InlineData("PLAYING(3)")]
    [InlineData("3")]
    [InlineData(" 3 ")]
    public void DetectsActivePlayingSessionFromAndroidMediaDump(string state)
    {
        string output = $$"""
            MEDIA SESSION SERVICE (dumpsys media_session)
            Sessions Stack - have 1 sessions:
              Session #0:
                active=true
                state=PlaybackState {state={{state}}, position=42}
            """;

        Assert.True(AndroidMediaPlaybackControl.IsPlaybackActive(output));
    }

    [Theory]
    [InlineData("30")]
    [InlineData("13")]
    [InlineData("3invalid")]
    [InlineData("PLAYING(30)")]
    [InlineData("PLAYING(3)invalid")]
    [InlineData("2, error=state=PLAYING(3)")]
    [InlineData("PAUSED(2), error=state=3")]
    public void DoesNotFindPlayingStateInsideAnotherFieldOrPartialValue(string state)
    {
        string output = $"Sessions Stack - have 1 sessions:\nactive=true\nstate=PlaybackState {{state={state}}}";

        Assert.False(AndroidMediaPlaybackControl.IsPlaybackActive(output));
    }

    [Theory]
    [InlineData("Sessions Stack - have 0 sessions:")]
    [InlineData("Sessions Stack - have 1 sessions:\nactive=false\nstate=PlaybackState {state=PLAYING(3)}")]
    [InlineData("Sessions Stack - have 1 sessions:\nactive=true\nstate=PlaybackState {state=PAUSED(2)}")]
    [InlineData("state=PlaybackState {state=PLAYING(3)}")]
    public void RejectsInactiveOrNonPlayingMediaSessions(string output)
    {
        Assert.False(AndroidMediaPlaybackControl.IsPlaybackActive(output));
    }
}
