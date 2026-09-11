FROM mcr.microsoft.com/dotnet/sdk:8.0
WORKDIR /src
COPY RegressionLab.sln ./
COPY src/ ./src/
COPY fixtures/ ./fixtures/
COPY tests/ ./tests/
CMD ["dotnet", "test", "tests/RegressionLab.Tests/RegressionLab.Tests.csproj", "--logger", "console"]
