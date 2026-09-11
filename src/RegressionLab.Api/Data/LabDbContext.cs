using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RegressionLab.Domain;

namespace RegressionLab.Data;

public class LabDbContext : DbContext
{
    public LabDbContext(DbContextOptions<LabDbContext> options) : base(options) { }

    public DbSet<RuleSet> RuleSets => Set<RuleSet>();
    public DbSet<Scenario> Scenarios => Set<Scenario>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<RunResult> RunResults => Set<RunResult>();

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<RuleSet>(e =>
        {
            e.Property(x => x.IgnorePaths).HasColumnType("jsonb");
            e.Property(x => x.ArraySortKeys).HasColumnType("jsonb");
            e.Property(x => x.DynamicPatterns).HasColumnType("jsonb");
            e.Property(x => x.IgnoreHeaders).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Name, x.Version }).IsUnique();
            e.HasIndex(x => x.IsActive);
        });

        b.Entity<Scenario>(e =>
        {
            e.Property(x => x.PathParameters).HasColumnType("jsonb");
            e.Property(x => x.QueryParameters).HasColumnType("jsonb");
            e.Property(x => x.Headers).HasColumnType("jsonb");
            e.Property(x => x.SecretRefs).HasColumnType("jsonb");
            e.HasOne(x => x.RuleSet).WithMany(r => r.Scenarios)
                .HasForeignKey(x => x.RuleSetId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Enabled);
        });

        b.Entity<Run>(e =>
        {
            // Primitive owned options work with ToJson; complex owned graphs do not (EF8 read bug).
            e.OwnsOne(x => x.Options).ToJson("options");
            e.Ignore(x => x.Results);
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<RunResult>(e =>
        {
            // Explicit JSON value converters: owned-ToJson fails to build readers for
            // nested graphs containing enums/lists in EF Core 8 (ArgumentNullException 'method').
            e.Property(x => x.Baseline)
                .HasConversion(JsonConverter<TargetCall>())
                .HasColumnType("jsonb");
            e.Property(x => x.Candidate)
                .HasConversion(JsonConverter<TargetCall>())
                .HasColumnType("jsonb");
            e.Property(x => x.Diffs)
                .HasConversion(
                    JsonConverter<List<DiffEntry>>(),
                    new ValueComparer<List<DiffEntry>>(
                        (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
                        v => v == null ? 0 : JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
                        v => JsonSerializer.Deserialize<List<DiffEntry>>(
                            JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!))
                .HasColumnType("jsonb");
            e.HasOne<Run>().WithMany().HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.RunId);
        });
    }

    private static ValueConverter<T, string> JsonConverter<T>() => new(
        v => JsonSerializer.Serialize(v, JsonOptions),
        v => JsonSerializer.Deserialize<T>(v, JsonOptions)!,
        new ConverterMappingHints(size: null));
}
