using KruaImDee.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Data;

public static class Seed
{
    public static async Task Run(AppDb db)
    {
        await db.Database.EnsureCreatedAsync();
        if (db.Database.IsSqlServer())
        {
            await SchemaUpgrade.Run(db);
            await DemoAccountUpgrade.Run(db);
        }
        if (await db.Users.AnyAsync())
        {
            if (db.Database.IsSqlite())
            {
                return;
            }
            await using var upgrade = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync(
                "EXEC sp_getapplock @Resource='KruaImDee-demo-upgrade', @LockMode='Exclusive', @LockOwner='Transaction';"
            );
            bool applied =
                await db
                    .Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*) AS Value FROM AppUpdates WHERE Name=N'Accountant20261120'"
                    )
                    .SingleAsync() > 0;
            if (!applied)
            {
                if (
                    await db.Users.AnyAsync(matchingUser =>
                        matchingUser.Email == "admin@admin.com" && matchingUser.Role == "Admin"
                    )
                    && !await db.Users.AnyAsync(matchingUser =>
                        matchingUser.Email == "accountant@accountant.com"
                    )
                )
                {
                    var accountant = new AppUser
                    {
                        Name = "นักบัญชี",
                        Email = "accountant@accountant.com",
                        Role = "Accountant",
                        Phone = "0812345678",
                    };
                    accountant.PasswordHash = new PasswordHasher<AppUser>().HashPassword(
                        accountant,
                        "accountant"
                    );
                    db.Users.Add(accountant);
                    await db.SaveChangesAsync();
                }

                await db.Database.ExecuteSqlRawAsync(
                    "INSERT AppUpdates(Name) VALUES(N'Accountant20261120');"
                );
            }

            await upgrade.CommitAsync();
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        string[] roles = new string[] { "Admin", "Manager", "Staff", "Customer", "Accountant" };
        foreach (string role in roles)
        {
            var user = new AppUser
            {
                Name = role == "Customer" ? "คุณอิ่มใจ" : role,
                Email = role.ToLower() + "@" + role.ToLower() + ".com",
                Role = role,
                Phone = "0812345678",
            };
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, role.ToLower());
            db.Users.Add(user);
        }

        string[] categoryNames = new string[]
        {
            "ข้าว",
            "เส้นและซุป",
            "สลัดและผัก",
            "สเต๊ก",
            "พิซซ่าและเบอร์เกอร์",
            "ของหวาน",
            "เครื่องดื่ม",
            "กาแฟ",
        };
        var categories = new List<Category>();
        foreach (string name in categoryNames)
        {
            var category = new Category { Name = name };
            categories.Add(category);
            db.Categories.Add(category);
        }

        await db.SaveChangesAsync();
        string[] names = new string[]
        {
            "ข้าวผัดสูตรเอาอีก",
            "ซุปเส้นน้ำใส",
            "สลัดผักสด",
            "สเต๊กย่างหอมสมุนไพร",
            "พิซซ่าโฮมเมด",
            "เค้กประจำวัน",
            "สลัดโบวล์",
            "เครื่องดื่มเย็นสดชื่น",
            "ผักรวมตามฤดูกาล",
            "เบอร์เกอร์เนื้อย่าง",
            "พาสต้าเส้นสด",
            "กาแฟคั่วหอม",
        };
        decimal[] prices = new decimal[] { 89, 109, 119, 259, 229, 95, 149, 65, 99, 179, 189, 70 };
        int[] categoryIndexes = new int[] { 0, 1, 2, 3, 4, 5, 2, 6, 2, 4, 1, 7 };
        decimal[] discounts = new decimal[] { 10, 0, 0, 0, 0, 0, 15, 0, 0, 0, 10, 0 };
        for (int index = 0; index < names.Length; index++)
        {
            db.MenuItems.Add(
                new MenuItem
                {
                    Name = names[index],
                    Description = "ปรุงตามออเดอร์ แจ้งความต้องการเพิ่มเติมก่อนสั่งได้",
                    Price = prices[index],
                    DiscountPercent = discounts[index],
                    CategoryId = categories[categoryIndexes[index]].Id,
                    ImageUrl = "/images/food-" + (index + 1).ToString("00") + ".jpg",
                }
            );
        }

        for (int index = 1; index <= 8; index++)
        {
            int capacity = 2;
            if (index > 4)
            {
                capacity = 4;
            }

            if (index > 6)
            {
                capacity = 6;
            }

            db.DiningTables.Add(
                new DiningTable { Name = "T" + index.ToString("00"), Capacity = capacity }
            );
        }

        db.Promotions.Add(
            new Promotion
            {
                Code = "IMDEE10",
                Name = "เอาอีก ลด 10%",
                Value = 10,
                Minimum = 200,
                StartsAt = DateTime.Today.AddYears(-1),
                EndsAt = DateTime.Today.AddYears(1),
            }
        );
        db.Settings.Add(new RestaurantSetting());
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
