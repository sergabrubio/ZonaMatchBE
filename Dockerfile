# Plantilla para el repo del backend (ZonaMatchBE). Copiar como "Dockerfile" en la raiz.
# AJUSTAR: la ruta del .csproj y el nombre del .dll (sin probar contra tu solucion).
 
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish "ZonaMatch.Api/ZonaMatch.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false
 
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
 
# Las imagenes de .NET 8+ escuchan en el puerto 8080 por defecto.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ZonaMatch.Api.dll"]