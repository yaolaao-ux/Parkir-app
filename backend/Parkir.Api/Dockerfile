FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY backend/Parkir.Api/Parkir.Api.csproj backend/Parkir.Api/
RUN dotnet restore backend/Parkir.Api/Parkir.Api.csproj

COPY backend/Parkir.Api/ backend/Parkir.Api/
COPY database/migrations/ database/migrations/
RUN dotnet publish backend/Parkir.Api/Parkir.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

RUN mkdir -p /var/lib/parkir/images \
    && chown -R $APP_UID:$APP_UID /var/lib/parkir

COPY --from=build /app/publish .
USER $APP_UID

ENTRYPOINT ["dotnet", "Parkir.Api.dll"]
