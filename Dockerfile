FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["FarmaciaEuridice.Inventario.csproj", "./"]
RUN dotnet restore "FarmaciaEuridice.Inventario.csproj"
COPY . .
RUN dotnet publish "FarmaciaEuridice.Inventario.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Para crear la BD SQLite en un volumen persistente, mapearemos la ruta
ENV DB_DIR=/app/data
ENV ASPNETCORE_URLS="http://+:80;https://+:443"

ENTRYPOINT ["dotnet", "FarmaciaEuridice.Inventario.dll"]
