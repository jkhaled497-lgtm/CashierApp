using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cashier.UI.Services;

public sealed class DailyDatabaseBackupService(
    DatabaseBackupService backupService,
    ILogger<DailyDatabaseBackupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var scheduledTime = now.Date.AddHours(23);
            if (now >= scheduledTime)
                scheduledTime = scheduledTime.AddDays(1);

            try
            {
                await Task.Delay(scheduledTime - now, stoppingToken);
                if (stoppingToken.IsCancellationRequested)
                    break;

                var result = await backupService.ExecuteBackupAsync(stoppingToken);
                logger.LogInformation(
                    "Daily database backup completed locally at {BackupPath}; Google Drive status: {DriveStatus}",
                    result.LocalBackupPath,
                    result.GoogleDrive.Status);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Daily database backup failed.");
            }
        }
    }
}
