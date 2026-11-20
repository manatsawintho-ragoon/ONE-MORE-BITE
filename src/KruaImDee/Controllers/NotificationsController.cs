using System.Security.Claims;
using KruaImDee.Data;
using KruaImDee.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Controllers;

[Authorize]
public class NotificationsController : Controller
{
    private readonly NotificationService notifications;

    public NotificationsController(NotificationService notifications)
    {
        this.notifications = notifications;
    }

    private int UserId
    {
        get { return int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!); }
    }
    private string Role
    {
        get { return User.FindFirstValue(ClaimTypes.Role) ?? ""; }
    }

    public async Task<IActionResult> Index(int page = 1)
    {
        Response.Headers.CacheControl = "private, no-store";
        var query = notifications
            .Visible(UserId, Role)
            .AsNoTracking()
            .OrderByDescending(item => item.Id);
        return View(await Paging.Load(this, query, page));
    }

    public async Task<IActionResult> Summary()
    {
        Response.Headers.CacheControl = "private, no-store";
        var query = notifications.Visible(UserId, Role).AsNoTracking();
        int unreadCount = await query.CountAsync(item => item.ReadAtUtc == null);
        var latest = await query.OrderByDescending(item => item.Id).Take(8).ToListAsync();
        var counts = await notifications.Counts(UserId, Role);
        var items = latest.Select(item => new
        {
            id = item.Id,
            title = item.Title,
            message = item.Message,
            createdAt = DateTime.SpecifyKind(item.CreatedAtUtc, DateTimeKind.Utc).ToString("O"),
            url = item.Url,
            kind = item.Kind,
            isRead = item.ReadAtUtc.HasValue,
        });
        return Json(
            new
            {
                unreadCount,
                items,
                counts,
            }
        );
    }

    [HttpPost]
    public async Task<IActionResult> Read(int id)
    {
        var query = notifications.Visible(UserId, Role).Where(item => item.Id == id);
        if (!await query.AnyAsync())
        {
            return NotFound();
        }
        await query
            .Where(item => item.ReadAtUtc == null)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(item => item.ReadAtUtc, DateTime.UtcNow)
            );
        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> ReadAll()
    {
        await notifications
            .Visible(UserId, Role)
            .Where(item => item.ReadAtUtc == null)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(item => item.ReadAtUtc, DateTime.UtcNow)
            );
        return Ok();
    }
}
