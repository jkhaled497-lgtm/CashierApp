using System.IO;
using System.Text.RegularExpressions;
using Cashier.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cashier.UI.Services;

public sealed record DatabaseBackupResult(string LocalBackupPath, GoogleDriveBackupResult GoogleDrive);

public sealed class DatabaseBackupService(
    IDbContextFactory<CashierDbContext> contextFactory,
    GoogleDriveBackupService googleDriveBackupService,
    ILogger<DatabaseBackupService> logger)
{
    private static readonly Regex BackupFileNamePattern = new(
        @"^backup_(\d{4}_\d{2}_\d{2}_\d{6})\.db$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly TimeSpan BackupRetentionAge = TimeSpan.FromDays(7);
    private const int MaximumLocalBackups = 7;
    private readonly SemaphoreSlim backupLock = new(1, 1);

    public async Task<DatabaseBackupResult> ExecuteBackupAsync(CancellationToken cancellationToken = default)
    {
        await backupLock.WaitAsync(cancellationToken);
        try
        {
            var backupPath = await CreateLocalBackupAsync(cancellationToken);
            ApplyLocalRetentionPolicy(Path.GetDirectoryName(backupPath)!, DateTime.Now);
            var googleDriveResult = await googleDriveBackupService.UploadAndApplyRetentionAsync(
                backupPath,
                cancellationToken);
            logger.LogInformation("Database backup created at {BackupPath}", backupPath);
            return new DatabaseBackupResult(backupPath, googleDriveResult);
        }
        finally
        {
            backupLock.Release();
        }
    }

    private async Task<string> CreateLocalBackupAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!string.Equals(
                db.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.Sqlite",
                StringComparison.Ordinal))
            throw new InvalidOperationException("يتطلب النسخ الاحتياطي استخدام قاعدة بيانات SQLite المحلية.");

        var sourceConnection = db.Database.GetDbConnection() as SqliteConnection
            ?? throw new InvalidOperationException("تعذر فتح اتصال قاعدة بيانات SQLite.");
        var backupDirectory = Path.Combine(AppContext.BaseDirectory, "Backups");
        Directory.CreateDirectory(backupDirectory);

        var timestamp = DateTime.Now;
        string backupPath;
        do
        {
            backupPath = Path.Combine(backupDirectory, $"backup_{timestamp:yyyy_MM_dd_HHmmss}.db");
            timestamp = timestamp.AddSeconds(1);
        }
        while (File.Exists(backupPath));

        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var backupConnection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = backupPath }.ToString());
        await backupConnection.OpenAsync(cancellationToken);
        await Task.Run(() => sourceConnection.BackupDatabase(backupConnection), cancellationToken);

        return backupPath;
    }

    private static void ApplyLocalRetentionPolicy(string backupDirectory, DateTime now)
    {
        var backups = Directory.EnumerateFiles(backupDirectory, "backup_*.db")
            .Select(path => new
            {
                Path = path,
                Timestamp = ParseBackupTimestamp(Path.GetFileName(path))
            })
            .Where(backup => backup.Timestamp.HasValue)
            .OrderByDescending(backup => backup.Timestamp)
            .ToList();

        foreach (var (backup, index) in backups.Select((backup, index) => (backup, index)))
        {
            if (index >= MaximumLocalBackups || backup.Timestamp!.Value < now - BackupRetentionAge)
                File.Delete(backup.Path);
        }
    }

    private static DateTime? ParseBackupTimestamp(string fileName)
    {
        var match = BackupFileNamePattern.Match(fileName);
        return match.Success &&
               DateTime.TryParseExact(
                   match.Groups[1].Value,
                   "yyyy_MM_dd_HHmmss",
                   System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None,
                   out var timestamp)
            ? timestamp
            : null;
    }
}
