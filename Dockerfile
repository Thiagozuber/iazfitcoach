# Multi-stage build for .NET 8 Web API
# 1. Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "iazfitcoach.csproj"
RUN dotnet build "iazfitcoach.csproj" -c Release -o /app/build

# 2. Publish stage
FROM build AS publish
RUN dotnet publish "iazfitcoach.csproj" -c Release -o /app/publish

# 3. Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Secure runtime configuration
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1

# Health check
HEALTHCHECK --interval=30s --timeout=3s \
    CMD curl -f http://localhost:8080/health || exit 1

# Expose port
EXPOSE 8080

# Entrypoint
ENTRYPOINT ["dotnet", "iazfitcoach.dll"]