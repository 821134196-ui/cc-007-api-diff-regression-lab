FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY fixtures/RegressionLab.Fixture/RegressionLab.Fixture.csproj fixtures/RegressionLab.Fixture/
RUN dotnet restore fixtures/RegressionLab.Fixture/RegressionLab.Fixture.csproj
COPY fixtures/ fixtures/
RUN dotnet publish fixtures/RegressionLab.Fixture/RegressionLab.Fixture.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
HEALTHCHECK --interval=5s --timeout=3s --start-period=20s --retries=12 \
  CMD curl -fsS http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "RegressionLab.Fixture.dll"]
