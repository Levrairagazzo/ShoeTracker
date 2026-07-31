FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ShoeTracker.Api.csproj .
RUN dotnet restore ShoeTracker.Api.csproj

COPY . .
RUN dotnet publish ShoeTracker.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app .

# The base image's default "app" user needs write access to the
# volume-mounted /data directory for the SQLite file.
USER root
RUN mkdir -p /data && chown app:app /data
USER app

ENV ASPNETCORE_URLS=http://+:8080
ENV ConnectionStrings__ShoeTrackerContext="Data Source=/data/shoetracker.db"

EXPOSE 8080
ENTRYPOINT ["dotnet", "ShoeTracker.Api.dll"]
