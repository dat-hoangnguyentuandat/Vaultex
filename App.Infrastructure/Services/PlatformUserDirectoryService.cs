using App.Domain.Entities;
using App.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Services
{
    public class PlatformUserDirectoryService
    {
        private readonly PlatformDbContext _platformDb;

        public PlatformUserDirectoryService(PlatformDbContext platformDb)
        {
            _platformDb = platformDb;
        }

        public async Task UpsertAsync(Guid tenantId, string email, Guid? userId = null, bool isActive = true)
        {
            var normalizedEmail = Normalize(email);
            var entry = await _platformDb.UserDirectory
                .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.NormalizedEmail == normalizedEmail);

            if (entry is null)
            {
                _platformDb.UserDirectory.Add(new PlatformUserDirectoryEntry
                {
                    TenantId = tenantId,
                    Email = email,
                    NormalizedEmail = normalizedEmail,
                    UserId = userId,
                    IsActive = isActive
                });
            }
            else
            {
                entry.Email = email;
                entry.UserId = userId ?? entry.UserId;
                entry.IsActive = isActive;
            }

            await _platformDb.SaveChangesAsync();
        }

        public async Task<Tenant?> ResolveSingleTenantAsync(string email)
        {
            var normalizedEmail = Normalize(email);
            var matches = await _platformDb.UserDirectory
                .AsNoTracking()
                .Include(d => d.Tenant)
                .Where(d => d.NormalizedEmail == normalizedEmail
                         && d.IsActive
                         && d.Tenant.Status == TenantStatus.Active)
                .Select(d => d.Tenant)
                .Take(2)
                .ToListAsync();

            return matches.Count == 1 ? matches[0] : null;
        }

        private static string Normalize(string email)
            => email.Trim().ToUpperInvariant();
    }
}
