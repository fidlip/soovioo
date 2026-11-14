# Support plans service image. Build context: backend/ (see deploy/docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Soovioo.SupportPlans/Soovioo.SupportPlans.csproj src/Soovioo.SupportPlans/
RUN dotnet restore src/Soovioo.SupportPlans/Soovioo.SupportPlans.csproj
COPY src/Soovioo.SupportPlans/ src/Soovioo.SupportPlans/
RUN dotnet publish src/Soovioo.SupportPlans/Soovioo.SupportPlans.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Soovioo.SupportPlans.dll"]
