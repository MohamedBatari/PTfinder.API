# Use .NET 8 SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["PTfinder.API/PTfinder.API.csproj", "PTfinder.API/"]
RUN dotnet restore "PTfinder.API/PTfinder.API.csproj"
COPY . .
WORKDIR "/src/PTfinder.API"
RUN dotnet publish "PTfinder.API.csproj" -c Release -o /app/publish

# Use ASP.NET 8 runtime image to run the app
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PTfinder.API.dll"]
