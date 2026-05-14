FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["App.API/App.API.csproj", "App.API/"]
COPY ["App.Application/App.Application.csproj", "App.Application/"]
COPY ["App.Domain/App.Domain.csproj", "App.Domain/"]
COPY ["App.Infrastructure/App.Infrastructure.csproj", "App.Infrastructure/"]
COPY ["App.Shared/App.Shared.csproj", "App.Shared/"]
RUN dotnet restore "App.API/App.API.csproj"
COPY . .
WORKDIR "/src/App.API"
RUN dotnet build "App.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "App.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "App.API.dll"]
