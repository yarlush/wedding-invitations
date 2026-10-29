FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Wedding.Api/Wedding.Api.csproj src/Wedding.Api/
RUN dotnet restore src/Wedding.Api/Wedding.Api.csproj
COPY src/ src/
RUN dotnet publish src/Wedding.Api/Wedding.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Wedding.Api.dll"]
