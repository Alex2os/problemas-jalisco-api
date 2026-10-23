# Build only the deployable API, not the solution's test project.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish JaliscoProblemsAPI.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 10000
ENV ASPNETCORE_URLS=http://+:10000
USER $APP_UID
ENTRYPOINT ["dotnet", "JaliscoProblemsAPI.dll"]
