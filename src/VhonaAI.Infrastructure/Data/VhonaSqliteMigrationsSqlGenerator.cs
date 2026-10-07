using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Sqlite.Migrations.Internal;

namespace VhonaAI.Infrastructure.Data;

/// <summary>
/// Migrations are authored for Azure SQL. SQLite rejects the type name nvarchar(max)
/// because max is a keyword, so those columns are stored as TEXT when the same migration runs locally.
/// </summary>
public sealed class VhonaSqliteMigrationsSqlGenerator : SqliteMigrationsSqlGenerator
{
    public VhonaSqliteMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,
        IRelationalAnnotationProvider migrationsAnnotations)
        : base(dependencies, migrationsAnnotations)
    {
    }

    protected override void Generate(
        CreateTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        foreach (var column in operation.Columns)
        {
            column.ColumnType = Rewrite(column.ColumnType);
        }

        base.Generate(operation, model, builder, terminate);
    }

    protected override void Generate(
        AddColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        operation.ColumnType = Rewrite(operation.ColumnType);
        base.Generate(operation, model, builder, terminate);
    }

    protected override void Generate(
        AlterColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        operation.ColumnType = Rewrite(operation.ColumnType);
        base.Generate(operation, model, builder);
    }

    private static string? Rewrite(string? columnType) =>
        columnType?.Replace("nvarchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase);
}

public static class VhonaSqliteOptions
{
    public static DbContextOptionsBuilder UseVhonaSqlite(this DbContextOptionsBuilder options, string connectionString)
    {
        options.UseSqlite(connectionString);
        options.ReplaceService<IMigrationsSqlGenerator, VhonaSqliteMigrationsSqlGenerator>();
        return options;
    }

    public static DbContextOptionsBuilder<TContext> UseVhonaSqlite<TContext>(
        this DbContextOptionsBuilder<TContext> options,
        string connectionString)
        where TContext : DbContext
    {
        ((DbContextOptionsBuilder)options).UseVhonaSqlite(connectionString);
        return options;
    }
}
