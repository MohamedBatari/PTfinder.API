# Use the official ASP.NET Core runtime image
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80

# Use the SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["PTfinder.API/PTfinder.API.csproj", "PTfinder.API/"]
RUN dotnet restore "PTfinder.API/PTfinder.API.csproj"
COPY . .
WORKDIR "/src/PTfinder.API"
RUN dotnet build "PTfinder.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PTfinder.API.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PTfinder.API.dll"]
