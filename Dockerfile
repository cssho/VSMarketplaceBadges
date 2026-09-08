FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# copy csproj and restore as distinct layers
COPY *.csproj ./aspnetapp/
WORKDIR /app/aspnetapp
RUN dotnet restore VSMarketplaceBadges.csproj

# copy everything else and build app
COPY . .
RUN dotnet publish VSMarketplaceBadges.csproj -c Release -o out


FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/aspnetapp/out ./
ENTRYPOINT ["dotnet", "VSMarketplaceBadges.dll"]