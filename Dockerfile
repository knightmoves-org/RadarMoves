# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY RadarMoves.sln .
COPY RadarMoves.Server/RadarMoves.Server.csproj RadarMoves.Server/
COPY RadarMoves.Client/RadarMoves.Client.csproj RadarMoves.Client/
COPY RadarMoves.Shared/RadarMoves.Shared.csproj RadarMoves.Shared/

# Restore dependencies
RUN dotnet restore RadarMoves.sln

# Copy all source files
COPY RadarMoves.Server/ RadarMoves.Server/
COPY RadarMoves.Client/ RadarMoves.Client/
COPY RadarMoves.Shared/ RadarMoves.Shared/

# Build and publish the server project
WORKDIR /src/RadarMoves.Server
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copy published application from build stage
COPY --from=build /app/publish .

# Expose port
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Run the application with migrations
ENTRYPOINT ["dotnet", "RadarMoves.Server.dll"]

