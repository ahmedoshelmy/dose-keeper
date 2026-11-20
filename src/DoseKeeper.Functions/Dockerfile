# Build context: repository root (see compose.yaml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
RUN --mount=type=cache,id=dose-keeper-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet publish src/DoseKeeper.Functions/DoseKeeper.Functions.csproj -c Release -o /home/site/wwwroot

# Official Functions host image for the .NET 10 isolated worker.
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true \
    FUNCTIONS_WORKER_RUNTIME=dotnet-isolated
COPY --from=build /home/site/wwwroot /home/site/wwwroot
