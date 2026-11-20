using System.Security.Claims;
using KruaImDee.Data;
using KruaImDee.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KruaImDee.Controllers;

[Authorize(Roles = "Customer")]
public class ReviewsController : Controller
{
    private readonly AppDb db;
    private readonly ReviewService reviews;

    public ReviewsController(AppDb db, ReviewService reviews)
    {
        this.db = db;
        this.reviews = reviews;
    }

    [HttpPost]
    public async Task<IActionResult> Create(int orderId, int menuItemId, int rating, string comment)
    {
        int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var order = await db.Orders.FindAsync(orderId);
        if (order == null)
        {
            return NotFound();
        }

        if (order.UserId != userId)
        {
            return Forbid();
        }

        bool created = await reviews.Create(userId, orderId, menuItemId, rating, comment);
        if (!created)
        {
            return Conflict(new { message = "คุณรีวิวเมนูนี้ในออเดอร์นี้แล้ว" });
        }

        TempData["Success"] = "บันทึกรีวิวแล้ว ขอบคุณสำหรับความคิดเห็น";
        return Redirect("/Menu/Details/" + menuItemId);
    }
}
