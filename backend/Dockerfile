# Backend API image. Build context: backend/ (see deploy/docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first, in its own layer — re-runs only when the project file changes.
COPY src/Soovioo.Api/Soovioo.Api.csproj src/Soovioo.Api/
RUN dotnet restore src/Soovioo.Api/Soovioo.Api.csproj
COPY src/ src/
RUN dotnet publish src/Soovioo.Api/Soovioo.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Uploaded attachments (MessageAttachmentStorageService: {ContentRoot}/
# AttachmentStorage) — a volume in production; created here so a fresh
# named volume inherits the non-root owner.
RUN mkdir AttachmentStorage && chown app:app AttachmentStorage
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Soovioo.Api.dll"]
