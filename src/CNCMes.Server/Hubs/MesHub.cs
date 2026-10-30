using Microsoft.AspNetCore.SignalR;
using CNCMes.Server.Models;

namespace CNCMes.Server.Hubs;

public class MesHub : Hub
{
    private readonly ILogger<MesHub> _logger;

    public MesHub(ILogger<MesHub> logger)
    {
        _logger = logger;
    }

    public async Task JoinTopic(string channelType, string topicKey)
    {
        var groupName = $"{channelType}:{topicKey}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Client {ConnectionId} joined topic {GroupName}", Context.ConnectionId, groupName);
    }

    public async Task LeaveTopic(string channelType, string topicKey)
    {
        var groupName = $"{channelType}:{topicKey}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Client {ConnectionId} left topic {GroupName}", Context.ConnectionId, groupName);
    }
}
