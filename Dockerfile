FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["MiniProject_Everything_1.csproj", "./"]
RUN dotnet restore "MiniProject_Everything_1.csproj"
COPY . .
RUN dotnet publish "MiniProject_Everything_1.csproj" -c Release -o /app/publish /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:10000
ENV Storage__DataPath=/app/data
EXPOSE 10000
COPY --from=build /app/publish .
RUN mkdir -p /app/data && chown -R app:app /app/data
USER app
ENTRYPOINT ["dotnet", "MiniProject_Everything_1.dll"]
