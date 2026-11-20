using System.Security.Claims;
using KruaImDee.Data;
using KruaImDee.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Controllers;

[Authorize(Roles = "Customer,Staff,Manager,Admin")]
public class ReservationsController : Controller
{
    private readonly AppDb db;
    private readonly RestaurantService service;

    public ReservationsController(AppDb db, RestaurantService service)
    {
        this.db = db;
        this.service = service;
    }

    int UserId
    {
        get { return int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!); }
    }

    public async Task<IActionResult> Index(int page = 1, int? id = null)
    {
        var tables = await db
            .DiningTables.Where(matchingTable => matchingTable.Active)
            .ToListAsync();
        ViewBag.Tables = tables;
        ViewBag.TableStates = await TableAvailability.Read(db, tables);
        return View(
            await Paging.Load(
                this,
                db.Reservations.Include(matchingReservation => matchingReservation.DiningTable)
                    .Where(matchingReservation =>
                        matchingReservation.UserId == UserId
                        && (!id.HasValue || matchingReservation.Id == id)
                    )
                    .OrderByDescending(matchingReservation => matchingReservation.StartsAt)
                    .ThenByDescending(matchingReservation => matchingReservation.Id),
                page
            )
        );
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        DateTime startsAt,
        int guests,
        string name,
        string phone,
        string note = "",
        int? tableId = null
    )
    {
        try
        {
            await service.Book(UserId, startsAt, guests, name, phone, note, tableId);
            TempData["Success"] = "รับคำขอจองโต๊ะแล้ว รอพนักงานยืนยัน";
        }
        catch (InvalidOperationException ex)
        {
            if (!string.IsNullOrEmpty(Request.Headers["X-Requested-With"]))
            {
                return BadRequest(new { message = ex.Message });
            }

            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(int id)
    {
        var reservation = await db.Reservations.FindAsync(id);
        if (reservation == null)
        {
            return NotFound();
        }

        if (reservation.UserId != UserId)
        {
            return Forbid();
        }

        await service.BookingStatus(id, UserId, "Cancelled", null, true);
        return RedirectToAction(nameof(Index));
    }
}
