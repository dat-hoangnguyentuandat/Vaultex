FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["App.Web/App.Web.csproj", "App.Web/"]
COPY ["App.Shared/App.Shared.csproj", "App.Shared/"]
RUN dotnet restore "App.Web/App.Web.csproj"
COPY . .
WORKDIR "/src/App.Web"
RUN dotnet build "App.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "App.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "App.Web.dll"]
