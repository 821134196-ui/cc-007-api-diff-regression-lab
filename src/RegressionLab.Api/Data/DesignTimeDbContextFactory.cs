using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace RegressionLab.Data;

/// <summary>Used by `dotnet ef` at build time; the runtime connection comes from configuration.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LabDbContext>
{
    public LabDbContext CreateDbContext(string[] args)
    {
        var dataSource = new NpgsqlDataSourceBuilder(
            "Host=localhost;Database=regression_lab;Username=lab;Password=lab")
            .EnableDynamicJson()
            .Build();
        var options = new DbContextOptionsBuilder<LabDbContext>()
            .UseNpgsql(dataSource)
            .Options;
        return new LabDbContext(options);
    }
}
