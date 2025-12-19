FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/VOLWebHook.Api/VOLWebHook.Api.csproj ./VOLWebHook.Api/
RUN dotnet restore VOLWebHook.Api/VOLWebHook.Api.csproj

COPY src/VOLWebHook.Api/ ./VOLWebHook.Api/
WORKDIR /src/VOLWebHook.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN mkdir -p /var/data/volwebhook/webhooks /var/log/volwebhook && \
    chown -R app:app /var/data/volwebhook /var/log/volwebhook

USER app

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "VOLWebHook.Api.dll"]
