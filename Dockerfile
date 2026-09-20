FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["MiniProject_Everything_1.csproj", "./"]
RUN dotnet restore "MiniProject_Everything_1.csproj"
COPY . .
RUN dotnet publish "MiniProject_Everything_1.csproj" -c Release -o /app/publish /p:UseAppHost=false
FROM node:24-bookworm-slim AS browser-deps
WORKDIR /browser
COPY package.json package-lock.json ./
RUN npm ci --omit=dev
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:10000
ENV Storage__DataPath=/app/data
ENV WebsiteAudit__NodePath=/usr/local/bin/node
ENV WebsiteAudit__ChromiumPath=/usr/bin/chromium
EXPOSE 10000
COPY --from=build /app/publish .
COPY --from=browser-deps /usr/local/bin/node /usr/local/bin/node
COPY --from=browser-deps /browser/node_modules ./node_modules
RUN apt-get update \
    && apt-get install -y --no-install-recommends chromium fonts-liberation fonts-noto-color-emoji \
    && rm -rf /var/lib/apt/lists/*
RUN secret_group="$(getent group 1000 | cut -d: -f1)" \
    && if [ -z "$secret_group" ]; then groupadd --gid 1000 render-secrets; secret_group=render-secrets; fi \
    && usermod --append --groups "$secret_group" app \
    && mkdir -p /app/data \
    && chown -R app:app /app/data
USER app
ENTRYPOINT ["dotnet", "MiniProject_Everything_1.dll"]
