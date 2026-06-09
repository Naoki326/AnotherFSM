using Microsoft.AspNetCore.SignalR;

namespace FSMDemo.API.Hubs;

public class ExecutionHub : Hub
{
    public async Task SubscribeToExecution()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "Execution");
    }

    public async Task UnsubscribeFromExecution()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Execution");
    }
}
