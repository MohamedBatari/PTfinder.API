using Microsoft.AspNetCore.SignalR;

namespace PTfinder.API.Hubs
{
    public class NotifyHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var http = Context.GetHttpContext();
            var kind = http?.Request.Query["kind"].ToString();  // "coach" or "client"
            var idStr = http?.Request.Query["id"].ToString();

            if (kind == "coach" && int.TryParse(idStr, out var coachId))
                await Groups.AddToGroupAsync(Context.ConnectionId, $"coach:{coachId}");
            else if (kind == "client" && int.TryParse(idStr, out var clientId))
                await Groups.AddToGroupAsync(Context.ConnectionId, $"client:{clientId}");

            await base.OnConnectedAsync();
        }
    }
}
