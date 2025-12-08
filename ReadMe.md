# Knightmoves development Blazor SignalR App

## Quick Start

### Local Development

```bash
docker run --name redis -p 6379:6379 -d redis # start redis
dotnet watch --project RadarMoves # start the application
# open http://localhost:8080/state in the browser
```

### Testing

```bash
dotnet test dotnet test RadarMoves.Test/RadarMoves.Test.csproj
```

### Production

```bash
docker-compose up -d
# open http://localhost:8080/state in the browser
```

### Application Initialization

```bash
# Usage:
#   dotnet add [<PROJECT>] package <PACKAGE_NAME> [options]
# Create a new Blazor project
export APP_NAME=RadarMoves
export APP_CLIENT=$APP_NAME.Client
dotnet new blazor -o $APP_NAME -int WebAssembly -ai False
cd $APP_NAME
# we are going to be using Redis and PureHDF to
dotnet add $APP_NAME package StackExchange.Redis
dotnet add $APP_NAME package PureHDF
# add the SignalR client package
dotnet add $APP_CLIENT package Microsoft.AspNetCore.SignalR.Client
```

[link to the documentation for adding a SignalR hub to a Blazor project](https://learn.microsoft.com/en-us/aspnet/core/blazor/tutorials/signalr-blazor?view=aspnetcore-10.0&tabs=net-cli#add-a-signalr-hub)

- [ ] Transform radar data from `(nrays, nbins)` -> `(latitude, longitude)`
- [ ] Write the transformed data to redis.
- [ ] Write state to redis.

```c#
class State {
    public string[] products;
    public DateTime[] timestamp;
}
```
