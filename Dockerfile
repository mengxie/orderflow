# syntax=docker/dockerfile:1

# ---- Build: restore in its own layer so it stays cached until a project file changes.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/OrderFlow.Domain/OrderFlow.Domain.csproj src/OrderFlow.Domain/
COPY src/OrderFlow.Contracts/OrderFlow.Contracts.csproj src/OrderFlow.Contracts/
COPY src/OrderFlow.Application/OrderFlow.Application.csproj src/OrderFlow.Application/
COPY src/OrderFlow.Infrastructure/OrderFlow.Infrastructure.csproj src/OrderFlow.Infrastructure/
COPY src/OrderFlow.Api/OrderFlow.Api.csproj src/OrderFlow.Api/
RUN dotnet restore src/OrderFlow.Api/OrderFlow.Api.csproj
COPY src/ src/
RUN dotnet publish src/OrderFlow.Api/OrderFlow.Api.csproj -c Release -o /app --no-restore -p:UseAppHost=false

# ---- Runtime: chiseled Ubuntu image. No shell or package manager, runs as a non-root user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrderFlow.Api.dll"]
