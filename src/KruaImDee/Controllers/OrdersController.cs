using System.Security.Claims;
using KruaImDee.Data;
using KruaImDee.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Controllers;

[Authorize]
// Only the owner or restaurant staff can view an order.
public class OrdersController : Controller
{
    private readonly AppDb db;
    private readonly RestaurantService restaurant;
    private readonly IWebHostEnvironment environment;

    public OrdersController(AppDb db, RestaurantService restaurant, IWebHostEnvironment environment)
    {
        this.db = db;
        this.restaurant = restaurant;
        this.environment = environment;
    }

    int UserId
    {
        get { return int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!); }
    }

    bool Team
    {
        get
        {
            return User.IsInRole("Admin")
                || User.IsInRole("Manager")
                || User.IsInRole("Staff")
                || User.IsInRole("Accountant");
        }
    }

    [HttpPost]
    public async Task<IActionResult> StartPayment(int id)
    {
        var order = await db
            .Orders.AsNoTracking()
            .SingleOrDefaultAsync(matchingOrder => matchingOrder.Id == id);
        if (order == null)
        {
            return NotFound();
        }

        if (order.UserId != UserId)
        {
            return Forbid();
        }

        await restaurant.StartCustomerPayment(id, UserId);
        return RedirectToAction(nameof(Payment), new { id });
    }

    public async Task<IActionResult> Payment(int id)
    {
        var order = await db
            .Orders.Include(matchingOrder => matchingOrder.Items)
            .Include(matchingOrder => matchingOrder.Payment)
            .Include(matchingOrder => matchingOrder.DiningTable)
            .SingleOrDefaultAsync(matchingOrder => matchingOrder.Id == id);
        if (order == null)
        {
            return NotFound();
        }

        if (order.UserId != UserId)
        {
            return Forbid();
        }

        if (order.Payment != null)
        {
            return RedirectToAction(nameof(Receipt), new { id });
        }

        if (order.PaymentExpiresAt == null || order.Status == "Cancelled")
        {
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(order);
    }

    [HttpPost]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> ConfirmPayment(int orderId, IFormFile? slip)
    {
        var order = await db
            .Orders.AsNoTracking()
            .Include(matchingOrder => matchingOrder.Payment)
            .SingleOrDefaultAsync(matchingOrder => matchingOrder.Id == orderId);
        if (order == null)
        {
            return NotFound();
        }

        if (order.UserId != UserId)
        {
            return Forbid();
        }

        if (order.Payment != null)
        {
            return RedirectToAction(nameof(Receipt), new { id = orderId });
        }

        if (slip == null || slip.Length == 0 || slip.Length > 5 * 1024 * 1024)
        {
            return BadRequest(new { message = "กรุณาแนบรูปสลิปขนาดไม่เกิน 5 MB" });
        }

        using var stream = new MemoryStream();
        await slip.CopyToAsync(stream);
        try
        {
            string folder = Path.Combine(FileStorage.DataFolder(environment), "slips");
            await restaurant.ConfirmSlipPayment(orderId, UserId, stream.ToArray(), folder);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        return RedirectToAction(nameof(Receipt), new { id = orderId });
    }

    public async Task<IActionResult> Slip(int id)
    {
        var order = await db
            .Orders.AsNoTracking()
            .Include(matchingOrder => matchingOrder.Payment)
            .SingleOrDefaultAsync(matchingOrder => matchingOrder.Id == id);
        if (order == null)
        {
            return NotFound();
        }

        if (!Team && order.UserId != UserId)
        {
            return Forbid();
        }

        string filename = order.Payment?.SlipFileName ?? "";
        if (filename == "" || Path.GetFileName(filename) != filename)
        {
            return NotFound();
        }

        string path = Path.Combine(FileStorage.DataFolder(environment), "slips", filename);
        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, no-store";
        return PhysicalFile(path, SlipImage.ContentType(filename));
    }

    public async Task<IActionResult> Index(int page = 1)
    {
        return View(
            await Paging.Load(
                this,
                db.Orders.Include(matchingOrder => matchingOrder.DiningTable)
                    .Include(matchingOrder => matchingOrder.Payment)
                    .Where(matchingOrder => matchingOrder.UserId == UserId)
                    .OrderByDescending(matchingOrder => matchingOrder.Id),
                page
            )
        );
    }

    public async Task<IActionResult> Details(int id)
    {
        var order = await db
            .Orders.Include(matchingOrder => matchingOrder.Items)
            .Include(matchingOrder => matchingOrder.DiningTable)
            .Include(matchingOrder => matchingOrder.Payment)
            .SingleOrDefaultAsync(matchingOrder => matchingOrder.Id == id);
        if (order == null)
        {
            return NotFound();
        }

        if (!Team && order.UserId != UserId)
        {
            return Forbid();
        }

        ViewBag.ActiveRefund = await db
            .RefundRequests.AsNoTracking()
            .FirstOrDefaultAsync(refund =>
                refund.Payment.OrderId == id && refund.ActivePaymentId != null
            );

        return View(order);
    }

    public async Task<IActionResult> Receipt(int id)
    {
        var order = await db
            .Orders.Include(matchingOrder => matchingOrder.Items)
            .Include(matchingOrder => matchingOrder.DiningTable)
            .Include(matchingOrder => matchingOrder.Payment)
            .SingleOrDefaultAsync(matchingOrder => matchingOrder.Id == id);
        if (order == null)
        {
            return NotFound();
        }

        if (!Team && order.UserId != UserId)
        {
            return Forbid();
        }

        if (order.Payment == null)
        {
            return BadRequest("ยังไม่ได้ชำระเงิน");
        }

        return View(order);
    }
}
