using ClaudeMem.Worker.Services;

namespace ClaudeMem.Core.Tests.Services;

public class SSEBroadcasterTests
{
    [Fact]
    public void SSEBroadcaster_Tracks_Connected_Clients()
    {
        var broadcaster = new SSEBroadcaster();
        Assert.Equal(0, broadcaster.GetClientCount());
    }
}
