# syntax=docker/dockerfile:1
# Builds both hosts. Pick one with --target api or --target mcp.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, so package downloads are cached until a project file changes.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/DevWorkflow.Domain/DevWorkflow.Domain.csproj src/DevWorkflow.Domain/
COPY src/DevWorkflow.Application/DevWorkflow.Application.csproj src/DevWorkflow.Application/
COPY src/DevWorkflow.Infrastructure/DevWorkflow.Infrastructure.csproj src/DevWorkflow.Infrastructure/
COPY src/DevWorkflow.Api/DevWorkflow.Api.csproj src/DevWorkflow.Api/
COPY src/DevWorkflow.Mcp/DevWorkflow.Mcp.csproj src/DevWorkflow.Mcp/
RUN dotnet restore src/DevWorkflow.Api/DevWorkflow.Api.csproj \
 && dotnet restore src/DevWorkflow.Mcp/DevWorkflow.Mcp.csproj

COPY src/ src/
RUN dotnet publish src/DevWorkflow.Api/DevWorkflow.Api.csproj -c Release --no-restore -o /out/api \
 && dotnet publish src/DevWorkflow.Mcp/DevWorkflow.Mcp.csproj -c Release --no-restore -o /out/mcp

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=1
EXPOSE 8080
# The image's built-in unprivileged user.
USER $APP_UID

FROM runtime AS api
COPY --from=build /out/api .
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=3 CMD ["dotnet", "DevWorkflow.Api.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "DevWorkflow.Api.dll"]

FROM runtime AS mcp
COPY --from=build /out/mcp .
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=3 CMD ["dotnet", "DevWorkflow.Mcp.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "DevWorkflow.Mcp.dll"]
