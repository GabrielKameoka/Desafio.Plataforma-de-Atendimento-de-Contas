FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY crm-simulator/CrmSimulator.csproj crm-simulator/
RUN dotnet restore crm-simulator/CrmSimulator.csproj
COPY crm-simulator/ crm-simulator/
RUN dotnet publish crm-simulator/CrmSimulator.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "CrmSimulator.dll"]
