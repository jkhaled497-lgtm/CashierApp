using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cashier.UI.Services;

public enum GoogleDriveBackupStatus
{
    Uploaded,
    UploadedRetentionFailed,
    NotConfigured,
    Offline,
    Failed
}

public sealed record GoogleDriveBackupResult(GoogleDriveBackupStatus Status, string? Detail = null);

public sealed class GoogleDriveBackupService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<GoogleDriveBackupService> logger)
{
    private const int MaximumDriveBackups = 7;
    private const string ConnectivityCheckUrl = "https://www.googleapis.com/";
    private static readonly Regex BackupFileNamePattern = new(
        @"^backup_\d{4}_\d{2}_\d{2}_\d{6}\.db$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<GoogleDriveBackupResult> UploadAndApplyRetentionAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        var folderId = configuration["GoogleDrive:FolderId"];
        if (string.IsNullOrWhiteSpace(folderId))
        {
            logger.LogWarning("Google Drive upload skipped because GoogleDrive:FolderId is empty.");
            return new(GoogleDriveBackupStatus.NotConfigured, "معرّف مجلد Google Drive غير مضبوط.");
        }

        var credentialsPath = ResolveCredentialsPath(configuration["GoogleDrive:CredentialsPath"]);
        if (credentialsPath is null)
        {
            logger.LogWarning("Google Drive upload skipped because the service-account credentials file was not found.");
            return new(
                GoogleDriveBackupStatus.NotConfigured,
                "ملف google-credentials.json غير موجود بجانب التطبيق أو في C:\\ProgramData\\Cashier.");
        }

        if (!await IsInternetAvailableAsync(cancellationToken))
            return new(GoogleDriveBackupStatus.Offline, "لا يوجد اتصال بالإنترنت؛ حُفظت النسخة محلياً.");

        try
        {
            var serviceAccountCredential = await CredentialFactory
                .FromFileAsync<ServiceAccountCredential>(credentialsPath, cancellationToken);
            var credential = serviceAccountCredential.ToGoogleCredential()
                .CreateScoped(DriveService.Scope.Drive);
            using var drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Cashier"
            });

            var metadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = Path.GetFileName(backupPath),
                Parents = [folderId]
            };

            await using (var stream = new FileStream(
                             backupPath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             bufferSize: 81920,
                             useAsync: true))
            {
                var upload = drive.Files.Create(metadata, stream, "application/x-sqlite3");
                upload.Fields = "id,name,createdTime";
                upload.SupportsAllDrives = true;
                var result = await upload.UploadAsync(cancellationToken);
                if (result.Status != Google.Apis.Upload.UploadStatus.Completed)
                    throw result.Exception ?? new IOException($"Google Drive upload ended with status {result.Status}.");
            }

            try
            {
                await ApplyDriveRetentionPolicyAsync(drive, folderId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Database backup uploaded, but Google Drive retention failed.");
                return new(
                    GoogleDriveBackupStatus.UploadedRetentionFailed,
                    "تم الرفع، لكن تعذر حذف النسخ القديمة. تحقق من صلاحية الحساب على مجلد Drive.");
            }

            logger.LogInformation("Database backup uploaded to Google Drive: {BackupPath}", backupPath);
            return new(GoogleDriveBackupStatus.Uploaded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GoogleApiException exception)
        {
            logger.LogWarning(
                exception,
                "Google Drive upload failed with HTTP status {StatusCode}.",
                exception.HttpStatusCode);
            return new(
                GoogleDriveBackupStatus.Failed,
                $"فشل Google Drive ({(int)exception.HttpStatusCode}): {exception.Error?.Message ?? exception.Message}. " +
                "تأكد من مشاركة المجلد مع بريد حساب الخدمة.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Google Drive backup upload failed; the local backup remains available.");
            return new(GoogleDriveBackupStatus.Failed, $"فشل رفع Google Drive: {exception.Message}");
        }
    }

    private static string? ResolveCredentialsPath(string? configuredPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            candidates.Add(Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(AppContext.BaseDirectory, configuredPath));
        }
        else
        {
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "google-credentials.json"));
        }

        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Cashier",
            "google-credentials.json"));
        return candidates.FirstOrDefault(System.IO.File.Exists);
    }

    private async Task<bool> IsInternetAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClientFactory.CreateClient("GoogleDriveConnectivity")
                .GetAsync(ConnectivityCheckUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private static async Task ApplyDriveRetentionPolicyAsync(
        DriveService drive,
        string folderId,
        CancellationToken cancellationToken)
    {
        var files = new List<Google.Apis.Drive.v3.Data.File>();
        string? pageToken = null;
        do
        {
            var list = drive.Files.List();
            list.Q = $"'{folderId.Replace("'", "\\'", StringComparison.Ordinal)}' in parents and trashed = false";
            list.Fields = "nextPageToken,files(id,name,createdTime)";
            list.OrderBy = "createdTime desc";
            list.PageSize = 1000;
            list.PageToken = pageToken;
            list.SupportsAllDrives = true;
            list.IncludeItemsFromAllDrives = true;
            var page = await list.ExecuteAsync(cancellationToken);
            files.AddRange(page.Files ?? []);
            pageToken = page.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        foreach (var oldFile in files
                     .Where(file => file.Name is not null && BackupFileNamePattern.IsMatch(file.Name))
                     .OrderByDescending(file => file.CreatedTimeDateTimeOffset ?? DateTimeOffset.MinValue)
                     .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                     .Skip(MaximumDriveBackups))
        {
            if (!string.IsNullOrWhiteSpace(oldFile.Id))
            {
                var delete = drive.Files.Delete(oldFile.Id);
                delete.SupportsAllDrives = true;
                await delete.ExecuteAsync(cancellationToken);
            }
        }
    }
}
