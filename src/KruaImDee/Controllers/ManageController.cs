using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using KruaImDee.Data;
using KruaImDee.Models;
using KruaImDee.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Controllers;

[Authorize(Roles = "Admin,Manager")]
public class ManageController : Controller
{
    private readonly AppDb db;
    private readonly RestaurantService service;
    private readonly IWebHostEnvironment env;

    public ManageController(AppDb db, RestaurantService service, IWebHostEnvironment env)
    {
        this.db = db;
        this.service = service;
        this.env = env;
    }

    int Actor
    {
        get { return int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!); }
    }

    async Task Saved(string action, int id)
    {
        service.Audit(Actor, action, $"Record #{id}");
        await db.SaveChangesAsync();
        TempData["Success"] = "บันทึกข้อมูลเรียบร้อยแล้ว";
    }

    public async Task<IActionResult> Menu(int? id, int page = 1)
    {
        ViewBag.Edit = id.HasValue ? await db.MenuItems.FindAsync(id) : new MenuItem();
        ViewBag.Categories = await db
            .Categories.Where(matchingCategory => matchingCategory.Active)
            .ToListAsync();
        return View(
            await Paging.Load(
                this,
                db.MenuItems.Include(matchingMenuItem => matchingMenuItem.Category)
                    .OrderBy(matchingMenuItem => matchingMenuItem.Id),
                page
            )
        );
    }

    [HttpPost]
    public async Task<IActionResult> SaveMenu(
        int id,
        [StringLength(120)] string name,
        string description,
        decimal price,
        int categoryId,
        string imageUrl,
        bool active,
        bool soldOut,
        IFormFile? image,
        decimal discountPercent = 0
    )
    {
        if (
            string.IsNullOrWhiteSpace(name)
            || price <= 0
            || price > 100000
            || discountPercent < 0
            || discountPercent > 100
            || !await db.Categories.AnyAsync(matchingCategory =>
                matchingCategory.Id == categoryId && matchingCategory.Active
            )
        )
        {
            throw new InvalidOperationException("ตรวจสอบชื่อ ราคา และหมวดหมู่");
        }

        var menuItem = id == 0 ? new MenuItem() : await db.MenuItems.FindAsync(id);
        if (menuItem == null)
        {
            return NotFound();
        }

        if (image != null)
        {
            if (image.Length == 0 || image.Length > 5 * 1024 * 1024)
            {
                throw new InvalidOperationException("รูปต้องมีขนาดไม่เกิน 5 MB");
            }

            await using var stream = image.OpenReadStream();
            byte[] header = new byte[12];
            var read = await stream.ReadAsync(header);
            string ext = "";
            bool isJpeg = read >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255;
            byte[] pngSignature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            bool isPng = read >= 8 && header.Take(8).SequenceEqual(pngSignature);
            bool isWebp =
                read >= 12
                && System.Text.Encoding.ASCII.GetString(header, 0, 4) == "RIFF"
                && System.Text.Encoding.ASCII.GetString(header, 8, 4) == "WEBP";
            if (isJpeg)
            {
                ext = ".jpg";
            }
            else if (isPng)
            {
                ext = ".png";
            }
            else if (isWebp)
            {
                ext = ".webp";
            }

            if (ext == "")
            {
                throw new InvalidOperationException("รองรับรูป JPEG, PNG, WebP เท่านั้น");
            }

            var folder = FileStorage.UploadFolder(env);
            Directory.CreateDirectory(folder);
            var filename = Guid.NewGuid() + ext;
            await using var output = System.IO.File.Create(Path.Combine(folder, filename));
            stream.Position = 0;
            await stream.CopyToAsync(output);
            imageUrl = "/uploads/" + filename;
        }
        else if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            bool isAllowedImageUrl =
                imageUrl.StartsWith("/images/") || imageUrl.StartsWith("/uploads/");
            if (!isAllowedImageUrl && Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
            {
                isAllowedImageUrl = uri.Scheme == "https";
            }
            if (!isAllowedImageUrl)
            {
                throw new InvalidOperationException("URL รูปต้องเป็น HTTPS หรือไฟล์ในเว็บไซต์");
            }
        }

        menuItem.Name = name.Trim();
        menuItem.Description = description ?? "";
        menuItem.Price = price;
        menuItem.DiscountPercent = discountPercent;
        menuItem.CategoryId = categoryId;
        menuItem.ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? "/images/food-01.jpg" : imageUrl;
        menuItem.Active = active;
        menuItem.SoldOut = soldOut;
        if (id == 0)
        {
            db.MenuItems.Add(menuItem);
        }

        await Saved("SaveMenu", id);
        return RedirectToAction(nameof(Menu));
    }

    [HttpPost]
    public async Task<IActionResult> Deactivate(string kind, int id)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable
        );
        await service.LockCapacity();
        switch (kind)
        {
            case "Menu":
                var menuItem = await db.MenuItems.FindAsync(id);
                if (menuItem == null)
                {
                    return NotFound();
                }

                menuItem.Active = false;
                break;
            case "Categories":
                var category = await db.Categories.FindAsync(id);
                if (category == null)
                {
                    return NotFound();
                }

                category.Active = false;
                break;
            case "Tables":
                var table = await db.DiningTables.FindAsync(id);
                if (table == null)
                {
                    return NotFound();
                }

                if (
                    await db.Orders.AnyAsync(matchingOrder =>
                        matchingOrder.DiningTableId == id && !matchingOrder.Closed
                    )
                    || await db.Reservations.AnyAsync(matchingReservation =>
                        matchingReservation.DiningTableId == id
                        && matchingReservation.Status != "Cancelled"
                        && matchingReservation.Status != "Completed"
                    )
                )
                {
                    throw new InvalidOperationException("โต๊ะนี้มีรายการค้างอยู่");
                }

                table.Active = false;
                break;
            case "Promotions":
                var promotion = await db.Promotions.FindAsync(id);
                if (promotion == null)
                {
                    return NotFound();
                }

                promotion.Active = false;
                break;
            default:
                return BadRequest();
        }

        await Saved("Deactivate" + kind, id);
        await transaction.CommitAsync();
        return RedirectToAction(kind);
    }

    public async Task<IActionResult> Categories(int? id, int page = 1)
    {
        ViewBag.Edit = id.HasValue ? await db.Categories.FindAsync(id) : new Category();
        return View(
            await Paging.Load(
                this,
                db.Categories.OrderBy(matchingCategory => matchingCategory.Id),
                page
            )
        );
    }

    [HttpPost]
    public async Task<IActionResult> SaveCategory(
        int id,
        [StringLength(80)] string name,
        bool active
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("กรุณาระบุชื่อหมวดหมู่");
        }

        var category = id == 0 ? new Category() : await db.Categories.FindAsync(id);
        if (category == null)
        {
            return NotFound();
        }

        category.Name = name.Trim();
        category.Active = active;
        if (id == 0)
        {
            db.Categories.Add(category);
        }

        await Saved("SaveCategory", id);
        return RedirectToAction(nameof(Categories));
    }

    public async Task<IActionResult> TableQr(int id)
    {
        if (!await db.DiningTables.AnyAsync(matchingTable => matchingTable.Id == id))
        {
            return NotFound();
        }

        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(
            $"{Request.Scheme}://{Request.Host}/Menu?table={id}",
            QRCoder.QRCodeGenerator.ECCLevel.Q
        );
        return File(new QRCoder.PngByteQRCode(data).GetGraphic(8), "image/png");
    }

    public async Task<IActionResult> Tables(int? id, int page = 1)
    {
        ViewBag.Edit = id.HasValue ? await db.DiningTables.FindAsync(id) : new DiningTable();
        return View(
            await Paging.Load(
                this,
                db.DiningTables.OrderBy(matchingTable => matchingTable.Id),
                page
            )
        );
    }

    [HttpPost]
    public async Task<IActionResult> SaveTable(
        int id,
        [StringLength(30)] string name,
        int capacity,
        bool active
    )
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable
        );
        await service.LockCapacity();
        if (string.IsNullOrWhiteSpace(name) || capacity < 1 || capacity > 20)
        {
            throw new InvalidOperationException("ตรวจสอบชื่อโต๊ะและความจุ 1 ถึง 20 คน");
        }

        var table = id == 0 ? new DiningTable() : await db.DiningTables.FindAsync(id);
        if (table == null)
        {
            return NotFound();
        }

        if (
            (!active || capacity < table.Capacity)
            && (
                await db.Orders.AnyAsync(matchingOrder =>
                    matchingOrder.DiningTableId == id && !matchingOrder.Closed
                )
                || await db.Reservations.AnyAsync(matchingReservation =>
                    matchingReservation.DiningTableId == id
                    && matchingReservation.Status != "Cancelled"
                    && matchingReservation.Status != "Completed"
                )
            )
        )
        {
            throw new InvalidOperationException(
                "โต๊ะนี้มีรายการค้าง ไม่สามารถลดความจุหรือปิดใช้งาน"
            );
        }

        table.Name = name.Trim();
        table.Capacity = capacity;
        table.Active = active;
        if (id == 0)
        {
            db.DiningTables.Add(table);
        }

        await Saved("SaveTable", id);
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Tables));
    }

    public async Task<IActionResult> Promotions(int? id, int page = 1)
    {
        ViewBag.Edit = id.HasValue
            ? await db.Promotions.FindAsync(id)
            : new Promotion { StartsAt = DateTime.Today, EndsAt = DateTime.Today.AddMonths(1) };
        return View(
            await Paging.Load(
                this,
                db.Promotions.OrderBy(matchingPromotion => matchingPromotion.Id),
                page
            )
        );
    }

    [HttpPost]
    public async Task<IActionResult> SavePromotion(
        int id,
        [StringLength(30)] string code,
        string name,
        bool percentage,
        decimal value,
        decimal minimum,
        DateTime startsAt,
        DateTime endsAt,
        bool active
    )
    {
        if (
            string.IsNullOrWhiteSpace(code)
            || string.IsNullOrWhiteSpace(name)
            || value <= 0
            || minimum < 0
            || endsAt <= startsAt
            || percentage && value > 100
        )
        {
            throw new InvalidOperationException("ตรวจสอบรหัส มูลค่า และช่วงเวลาโปรโมชั่น");
        }

        var promotion = id == 0 ? new Promotion() : await db.Promotions.FindAsync(id);
        if (promotion == null)
        {
            return NotFound();
        }

        promotion.Code = code.Trim().ToUpperInvariant();
        promotion.Name = name;
        promotion.Percentage = percentage;
        promotion.Value = value;
        promotion.Minimum = minimum;
        promotion.StartsAt = startsAt;
        promotion.EndsAt = endsAt;
        promotion.Active = active;
        if (id == 0)
        {
            db.Promotions.Add(promotion);
        }

        await Saved("SavePromotion", id);
        return RedirectToAction(nameof(Promotions));
    }

    public async Task<IActionResult> Reports(DateTime? from, DateTime? to, int page = 1)
    {
        var start = (from ?? DateTime.Today).Date;
        var end = (to ?? DateTime.Today).Date.AddDays(1);
        var payments = db
            .Payments.Include(matchingPayment => matchingPayment.Order)
            .Where(matchingPayment =>
                matchingPayment.PaidAt >= start
                && matchingPayment.PaidAt < end
                && !matchingPayment.Refunded
            );
        var ids = payments.Select(matchingPayment => matchingPayment.OrderId);
        var items = await db
            .OrderItems.Where(matchingOrderItem => ids.Contains(matchingOrderItem.OrderId))
            .ToListAsync();
        var report = new ReportPage();
        report.From = start;
        report.To = end.AddDays(-1);
        report.PaymentCount = await payments.CountAsync();
        report.TotalAmount = await payments.SumAsync(payment => payment.Amount);
        report.CashAmount = await payments
            .Where(payment => payment.Method == "Cash")
            .SumAsync(payment => payment.Amount);
        report.TransferAmount = await payments
            .Where(payment => payment.Method == "Transfer")
            .SumAsync(payment => payment.Amount);
        report.Payments = await Paging.Load(
            this,
            payments
                .OrderByDescending(payment => payment.PaidAt)
                .ThenByDescending(payment => payment.Id),
            page
        );

        // รวมอาหารชื่อเดียวกันก่อนเรียงตามจำนวนที่ขายได้
        var bestSellers = new List<BestSeller>();
        foreach (var group in items.GroupBy(item => item.Name))
        {
            var bestSeller = new BestSeller();
            bestSeller.Name = group.Key;
            foreach (OrderItem item in group)
            {
                bestSeller.Quantity += item.Quantity;
                bestSeller.Total += item.UnitPrice * item.Quantity;
            }
            bestSellers.Add(bestSeller);
        }
        report.BestSellers = bestSellers.OrderByDescending(item => item.Quantity).ToList();
        return View(report);
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Users(int? id, int page = 1)
    {
        ViewBag.Edit = id.HasValue ? await db.Users.FindAsync(id) : new AppUser();
        return View(
            await Paging.Load(this, db.Users.OrderBy(matchingUser => matchingUser.Id), page)
        );
    }

    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<IActionResult> SaveUser(
        int id,
        [StringLength(100)] string name,
        [StringLength(180)] string email,
        string role,
        string phone,
        bool active,
        [StringLength(128)] string? password
    )
    {
        if (
            string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(email)
            || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email)
            || !new[] { "Admin", "Manager", "Staff", "Customer", "Accountant" }.Contains(role)
        )
        {
            throw new InvalidOperationException("ข้อมูลผู้ใช้ไม่ถูกต้อง");
        }

        if (id == Actor && (!active || role != "Admin"))
        {
            throw new InvalidOperationException("ไม่สามารถปิดหรือเปลี่ยนสิทธิ์บัญชีตนเองได้");
        }

        var user = id == 0 ? new AppUser() : await db.Users.FindAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        if (
            id == 0 && string.IsNullOrEmpty(password)
            || !string.IsNullOrEmpty(password) && password.Length < 8
        )
        {
            throw new InvalidOperationException("รหัสผ่านอย่างน้อย 8 ตัวอักษร");
        }

        user.Name = name;
        user.Email = email.Trim().ToLower();
        user.Role = role;
        user.Phone = phone ?? "";
        user.Active = active;
        if (!string.IsNullOrEmpty(password))
        {
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, password);
        }

        if (id == 0)
        {
            db.Users.Add(user);
        }

        await Saved("SaveUser", id);
        return RedirectToAction(nameof(Users));
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Settings(int page = 1)
    {
        ViewBag.Audit = await Paging.Load(
            this,
            db.AuditLogs.OrderByDescending(matchingAuditLog => matchingAuditLog.Id),
            page,
            20
        );
        return View(await db.Settings.FirstAsync());
    }

    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<IActionResult> TaxSettings(
        [Required, StringLength(200)] string taxLegalName,
        [Required, StringLength(500)] string taxAddress,
        [Required, RegularExpression("^[0-9]{13}$")] string taxId,
        [Required, RegularExpression("^[0-9]{5}$")] string taxBranch
    )
    {
        var setting = await db.Settings.FirstAsync();
        setting.TaxLegalName = taxLegalName.Trim();
        setting.TaxAddress = taxAddress.Trim();
        setting.TaxId = taxId;
        setting.TaxBranch = taxBranch;
        await Saved("TaxSettings", setting.Id);
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Settings(string name, string phone, string address)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("กรุณาระบุชื่อร้าน");
        }

        var setting = await db.Settings.FirstAsync();
        setting.Name = name;
        setting.Phone = phone ?? "";
        setting.Address = address ?? "";
        await Saved("Settings", setting.Id);
        return RedirectToAction(nameof(Settings));
    }
}
