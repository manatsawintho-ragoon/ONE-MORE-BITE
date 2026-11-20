using KruaImDee.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Data;

public static class DemoAccountUpgrade
{
    public static async Task Run(AppDb db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(
            "EXEC sp_getapplock @Resource='KruaImDee-demo-upgrade', @LockMode='Exclusive', @LockOwner='Transaction';"
        );
        await db.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'AppUpdates', N'U') IS NULL CREATE TABLE AppUpdates (Name nvarchar(100) NOT NULL PRIMARY KEY);"
        );
        int applied = await db
            .Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM AppUpdates WHERE Name=N'DemoCredentials20261114'"
            )
            .SingleAsync();
        if (applied == 0)
        {
            string[] roles = new string[] { "Admin", "Manager", "Staff", "Customer" };
            foreach (string role in roles)
            {
                string username = role.ToLowerInvariant();
                string oldEmail = username + "@imdee.local";
                string newEmail = username + "@" + username + ".com";
                var user = await db.Users.SingleOrDefaultAsync(matchingUser =>
                    matchingUser.Email == oldEmail && matchingUser.Role == role
                );
                if (
                    user != null
                    && !await db.Users.AnyAsync(matchingUser => matchingUser.Email == newEmail)
                )
                {
                    user.Email = newEmail;
                    user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, username);
                }
            }

            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO AppUpdates (Name) VALUES (N'DemoCredentials20261114');"
            );
        }

        await transaction.CommitAsync();
    }
}
