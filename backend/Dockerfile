FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . ./backend/
RUN dotnet restore backend/src/EVCharging.Api/EVCharging.Api.csproj --locked-mode
RUN dotnet publish backend/src/EVCharging.Api/EVCharging.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
COPY render-entrypoint.sh /app/render-entrypoint.sh
EXPOSE 10000
ENTRYPOINT ["/bin/sh", "/app/render-entrypoint.sh"]
