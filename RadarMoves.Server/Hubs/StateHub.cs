using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using RadarMoves.Shared;

namespace RadarMoves.Server.Hubs;

public class StateHub(IConnectionMultiplexer multiplexer) : Hub {

    private readonly IDatabase _db = multiplexer.GetDatabase();


    public async Task SetState(State state) {
        if (state.UserState == null) {
            return;
        }

        HashEntry[] entries;
        if (await _db.KeyExistsAsync(state.UserState.UserId)) {
            entries = [
                new("MouseX", state.UserState.MouseX),
                new("MouseY", state.UserState.MouseY)
            ];
        } else {
            entries = [
                new("UserName", state.UserState.UserName),
                new("MouseX", state.UserState.MouseX),
                new("MouseY", state.UserState.MouseY)
            ];
        }
        await _db.HashSetAsync(state.UserState.UserId, entries);
        await Clients.All.SendAsync("SetState", state);
    }
}