using Microsoft.AspNetCore.SignalR;

namespace VotingSystem.Web.Hubs;

public class VoteHub : Hub
{
    public Task SendVoteCounts(int electionId, object counts)
    {
        return Clients.All.SendAsync("VoteCountsUpdated", electionId, counts);
    }
}
