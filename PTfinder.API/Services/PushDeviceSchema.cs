using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;

namespace PTfinder.API.Services;

/// <summary>
/// Creates only the additive push-device table when deployments intentionally
/// keep EF auto-migrations disabled. Existing tables and rows are untouched.
/// </summary>
public static class PushDeviceSchema
{
    public static async Task EnsureAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        const string sql = """
IF OBJECT_ID(N'[dbo].[PushDevices]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PushDevices]
    (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PushDevices] PRIMARY KEY,
        [Token] nvarchar(512) NOT NULL,
        [Provider] nvarchar(40) NOT NULL CONSTRAINT [DF_PushDevices_Provider] DEFAULT N'expo',
        [Platform] nvarchar(20) NOT NULL CONSTRAINT [DF_PushDevices_Platform] DEFAULT N'android',
        [CoachId] int NULL,
        [ClientId] int NULL,
        [IsActive] bit NOT NULL CONSTRAINT [DF_PushDevices_IsActive] DEFAULT 1,
        [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_PushDevices_CreatedAtUtc] DEFAULT GETUTCDATE(),
        [LastSeenAtUtc] datetime2 NOT NULL CONSTRAINT [DF_PushDevices_LastSeenAtUtc] DEFAULT GETUTCDATE()
    );
    CREATE UNIQUE INDEX [IX_PushDevices_Token] ON [dbo].[PushDevices]([Token]);
    CREATE INDEX [IX_PushDevices_CoachId_IsActive] ON [dbo].[PushDevices]([CoachId], [IsActive]);
    CREATE INDEX [IX_PushDevices_ClientId_IsActive] ON [dbo].[PushDevices]([ClientId], [IsActive]);
END
""";

        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
        catch (Exception ex)
        {
            // A restricted SQL principal must not prevent the API from
            // starting; push registration will retry on the next request.
            logger.LogWarning(ex, "Push device table could not be ensured; push delivery will retry later.");
        }
    }
}
