# Knightmoves development Blazor SignalR App

## Quick Start

### Local Development

```bash
docker run --name redis -p 6379:6379 -d redis # start redis
dotnet watch --project RadarMoves # start the application
# open http://localhost:8080/state in the browser
```

### Testing

#### Local Testing

```bash
dotnet test RadarMoves.Test/RadarMoves.Test.csproj
```

#### Docker Testing

The Dockerfile includes a dedicated test stage that runs all tests as part of the build process. Tests must pass before the application is built and published.

**Run tests in Docker:**

```bash
# Build the test stage (tests run during build)
docker build --target test -t radar-moves-test .

# Extract test results
# The test-results directory is created during the build, so we can extract it
docker create --name test-container radar-moves-test
docker cp test-container:/test-results ./test-results
docker rm test-container
```

**Note:** The `/test-results` directory is created in the Dockerfile before tests run, so it will always exist in the image even if tests fail. Test result files (`.trx`) will only be present if tests actually executed.

**Build with tests (fails if tests fail):**

```bash
# Building the full image will automatically run tests
# The build will fail if any tests fail
docker build -t radar-moves .
```

**Skip tests (build only):**

```bash
# Build only the runtime image, skipping tests
docker build --target runtime -t radar-moves .
```

**Note:** The test stage requires the `data/` directory to be present, which contains the HDF5 test files needed by the tests.

### Production

#### Using Docker Compose

```bash
# Build and start all services (includes Redis)
docker-compose up -d

# View logs
docker-compose logs -f

# Stop services
docker-compose down

# open http://localhost:8080/state in the browser
```

#### Docker Build Stages

The Dockerfile uses a multi-stage build process:

1. **Build Stage**: Restores dependencies and copies all source files
2. **Test Stage**: Runs unit tests (requires `data/` directory with test files)
3. **Publish Stage**: Builds and publishes the server application
4. **Runtime Stage**: Final production image with only the published application

**Build the production image:**

```bash
docker build -t radar-moves .
```

**Run the production container:**

```bash
docker run -d -p 8080:8080 --name radar-moves-app radar-moves
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
