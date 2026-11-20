using System.Security.Claims;
using KruaImDee.Data;
using KruaImDee.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Controllers;

[Authorize(Roles = "Admin,Manager,Staff")]
public class StaffController : Controller
{
    private readonly AppDb db;
    private readonly RestaurantService service;

    public StaffController(AppDb db, RestaurantService service)
    {
        this.db = db;
        this.service = service;
    }

    int Actor
    {
        get { return int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!); }
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.OpenOrders = await db.Orders.CountAsync(matchingOrder => !matchingOrder.Closed);
        ViewBag.Categories = await db
            .Categories.Where(matchingCategory => matchingCategory.Active)
            .OrderBy(matchingCategory => matchingCategory.Id)
            .ToListAsync();
        var tables = await db
            .DiningTables.Where(matchingTable => matchingTable.Active)
            .ToListAsync();
        ViewBag.Tables = tables;
        ViewBag.TableStates = await TableAvailability.Read(db, tables);
        ViewBag.SelectableTableIds = await TableAvailability.Selectable(db, tables, Actor, true);
        return View(
            await db
                .MenuItems.Include(matchingMenuItem => matchingMenuItem.Category)
                .Where(matchingMenuItem =>
                    matchingMenuItem.Active && matchingMenuItem.Category.Active
                )
                .ToListAsync()
        );
    }

    public async Task<IActionResult> Orders(int page = 1)
    {
        return View(
            await Paging.Load(
                this,
                db.Orders.Include(matchingOrder => matchingOrder.DiningTable)
                    .Include(matchingOrder => matchingOrder.Payment)
                    .OrderByDescending(matchingOrder => matchingOrder.Id),
                page
            )
        );
    }

    public async Task<IActionResult> Kitchen()
    {
        return View(
            await db
                .Orders.Include(matchingOrder => matchingOrder.Items)
                .Include(matchingOrder => matchingOrder.DiningTable)
                .Where(matchingOrder =>
                    matchingOrder.Status == "Received"
                    || matchingOrder.Status == "Preparing"
                    || matchingOrder.Status == "Ready"
                )
                .OrderBy(matchingOrder => matchingOrder.CreatedAt)
                .ThenBy(matchingOrder => matchingOrder.Id)
                .ToListAsync()
        );
    }

    [HttpPost]
    public async Task<IActionResult> Advance(int id, string status)
    {
        await service.Advance(id, Actor, status);
        return RedirectToAction(nameof(Kitchen));
    }

    [HttpPost]
    public async Task<IActionResult> Settle(
        int id,
        string method,
        decimal received,
        bool transferConfirmed
    )
    {
        try
        {
            await service.Settle(id, Actor, method, received, transferConfirmed);
            return Redirect("/Orders/Receipt/" + id);
        }
        catch (InvalidOperationException ex)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return BadRequest(new { message = ex.Message });
            }

            TempData["Error"] = ex.Message;
            return Redirect("/Orders/Details/" + id);
        }
    }

    [HttpPost, Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Void(int id, string reason)
    {
        await service.Void(id, Actor, reason);
        return Redirect("/Orders/Details/" + id);
    }

    public async Task<IActionResult> Reservations(int page = 1, int? id = null)
    {
        ViewBag.Tables = await db
            .DiningTables.Where(matchingTable => matchingTable.Active)
            .ToListAsync();
        return View(
            await Paging.Load(
                this,
                db.Reservations.Include(matchingReservation => matchingReservation.DiningTable)
                    .Where(reservation => !id.HasValue || reservation.Id == id)
                    .OrderByDescending(matchingReservation => matchingReservation.StartsAt)
                    .ThenByDescending(matchingReservation => matchingReservation.Id),
                page
            )
        );
    }

    [HttpPost]
    public async Task<IActionResult> BookingStatus(int id, string status, int? tableId)
    {
        try
        {
            await service.BookingStatus(id, Actor, status, tableId, false);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Reservations));
    }

    [HttpPost]
    public async Task<IActionResult> SoldOut(int id)
    {
        var menuItem = await db.MenuItems.FindAsync(id);
        if (menuItem == null)
        {
            return NotFound();
        }

        menuItem.SoldOut = !menuItem.SoldOut;
        service.Audit(Actor, "Availability", $"Menu #{id}: {menuItem.SoldOut}");
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
