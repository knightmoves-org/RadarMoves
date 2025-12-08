# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files (needed for restore)
COPY RadarMoves.sln .
COPY RadarMoves.Server/RadarMoves.Server.csproj RadarMoves.Server/
COPY RadarMoves.Client/RadarMoves.Client.csproj RadarMoves.Client/
COPY RadarMoves.Shared/RadarMoves.Shared.csproj RadarMoves.Shared/
COPY RadarMoves.Test/RadarMoves.Test.csproj RadarMoves.Test/

# Restore dependencies for the solution
RUN dotnet restore RadarMoves.sln --verbosity quiet

# Copy all source files (including test files)
COPY RadarMoves.Server/ RadarMoves.Server/
COPY RadarMoves.Client/ RadarMoves.Client/
COPY RadarMoves.Shared/ RadarMoves.Shared/
COPY RadarMoves.Test/ RadarMoves.Test/

# Stage 2: Test
FROM build AS test
WORKDIR /src

# Copy test data files
COPY data/ data/

# Create test results directory (ensures it exists even if tests fail)
RUN mkdir -p /test-results

# Run tests
RUN dotnet test RadarMoves.Test/RadarMoves.Test.csproj \
    --no-restore \
    --verbosity normal \
    --logger "trx;LogFileName=test-results.trx" \
    --results-directory /test-results

# Stage 3: Publish
FROM build AS publish
WORKDIR /src/RadarMoves.Server
RUN dotnet publish -c Release -o /app/publish

# Stage 4: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copy published application from publish stage
COPY --from=publish /app/publish .

# Expose port
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Run the application with migrations
ENTRYPOINT ["dotnet", "RadarMoves.Server.dll"]

