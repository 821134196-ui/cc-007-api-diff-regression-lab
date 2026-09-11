# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/RegressionLab.Api/RegressionLab.Api.csproj src/RegressionLab.Api/
RUN dotnet restore src/RegressionLab.Api/RegressionLab.Api.csproj

COPY src/ src/
RUN dotnet publish src/RegressionLab.Api/RegressionLab.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- web build ----
FROM node:20-bookworm-slim AS webbuild
WORKDIR /web
COPY web/package.json web/package-lock.json* ./
RUN npm install --no-audit --no-fund
COPY web/ ./
RUN npm run build

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./
COPY --from=webbuild /web/dist ./wwwroot

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=40s --retries=12 \
  CMD curl -fsS http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "RegressionLab.Api.dll"]
