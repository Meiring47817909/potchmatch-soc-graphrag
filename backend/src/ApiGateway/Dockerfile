# Build stage - uses the full SDK to restore and publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ApiGateway.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage - much smaller image, just the ASP.NET Core runtime + published output.
# ocelot.json and ocelot.Docker.json are copied into /app/publish automatically by
# `dotnet publish` because of the <CopyToOutputDirectory> entries in ApiGateway.csproj.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_ENVIRONMENT=Docker
EXPOSE 8080

ENTRYPOINT ["dotnet", "ApiGateway.dll"]
