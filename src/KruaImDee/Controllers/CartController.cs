using System.Security.Claims;
using System.Text.Json;
using KruaImDee.Data;
using KruaImDee.Models;
using KruaImDee.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Controllers;

// Keep the customer cart separate from cashier bills.
public class CartController : Controller
{
    // รอให้คำขอก่อนหน้าบันทึกตะกร้าเสร็จ เพื่อไม่ให้การกดพร้อมกันเขียนทับกัน
    private static readonly SemaphoreSlim cartGate = new SemaphoreSlim(1, 1);
    private readonly AppDb db;
    private readonly RestaurantService service;

    public CartController(AppDb db, RestaurantService service)
    {
        this.db = db;
        this.service = service;
    }

    public override async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next
    )
    {
        if (User.IsInRole("Accountant"))
        {
            context.Result = Forbid();
            return;
        }

        // Save the latest cart before another request reads it.
        await cartGate.WaitAsync(HttpContext.RequestAborted);
        try
        {
            await HttpContext.Session.LoadAsync();
            await next();
            await HttpContext.Session.CommitAsync();
        }
        finally
        {
            cartGate.Release();
        }
    }

    private List<CartLine> Read()
    {
        string json = HttpContext.Session.GetString("Cart") ?? "[]";
        return JsonSerializer.Deserialize<List<CartLine>>(json) ?? new List<CartLine>();
    }

    private void Save(List<CartLine> lines)
    {
        string json = JsonSerializer.Serialize(lines);
        HttpContext.Session.SetString("Cart", json);
    }

    public async Task<IActionResult> Index()
    {
        var page = new CartPage
        {
            Tables = await db
                .DiningTables.Where(matchingTable => matchingTable.Active)
                .ToListAsync(),
        };
        ViewBag.TableStates = await TableAvailability.Read(db, page.Tables);
        int userId = 0;
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
        bool staff = User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("Staff");
        ViewBag.SelectableTableIds = await TableAvailability.Selectable(
            db,
            page.Tables,
            userId,
            staff
        );
        foreach (var line in Read())
        {
            var item = await db.MenuItems.FindAsync(line.MenuItemId);
            if (item != null)
            {
                page.Lines.Add(new CartEntry { Item = item, Line = line });
            }
        }

        ViewBag.TableId = HttpContext.Session.GetInt32("TableId");
        return View(page);
    }

    [HttpPost]
    public async Task<IActionResult> Add(int menuItemId, int quantity = 1, string note = "")
    {
        Rules.Quantity(quantity);
        note = (note ?? "").Trim();
        if (note.Length > 500)
        {
            throw new InvalidOperationException("หมายเหตุต้องไม่เกิน 500 ตัวอักษร");
        }

        var item = await db
            .MenuItems.Include(matchingMenuItem => matchingMenuItem.Category)
            .SingleOrDefaultAsync(matchingMenuItem => matchingMenuItem.Id == menuItemId);
        if (item == null || !item.Active || !item.Category.Active || item.SoldOut)
        {
            return BadRequest("อาหารไม่พร้อมจำหน่าย");
        }

        var lines = Read();
        var old = lines.FindIndex(x => x.MenuItemId == menuItemId && x.Note == note);
        if (old >= 0)
        {
            quantity += lines[old].Quantity;
            Rules.Quantity(quantity);
            lines[old] = new CartLine(menuItemId, quantity, note);
        }
        else
        {
            if (lines.Count >= 50)
            {
                throw new InvalidOperationException("รับได้ไม่เกิน 50 รายการต่อบิล");
            }

            lines.Add(new CartLine(menuItemId, quantity, note));
        }

        Save(lines);
        if (Request.Headers["X-Requested-With"] == "fetch")
        {
            return Json(
                new
                {
                    count = lines.Sum(x => x.Quantity),
                    message = "เพิ่ม " + item.Name + " ลงตะกร้าแล้ว",
                }
            );
        }

        TempData["Success"] = "เพิ่มลงตะกร้าแล้ว";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Quote(string promo = "")
    {
        try
        {
            return Json(await service.Quote(Read(), promo));
        }
        catch (InvalidOperationException error)
        {
            return BadRequest(new { message = error.Message });
        }
    }

    [HttpPost]
    public IActionResult Update(int index, int quantity)
    {
        Rules.Quantity(quantity);
        var lines = Read();
        if (index < 0 || index >= lines.Count)
        {
            return BadRequest();
        }

        lines[index].Quantity = quantity;
        Save(lines);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public IActionResult Remove(int index)
    {
        var lines = Read();
        if (index < 0 || index >= lines.Count)
        {
            return BadRequest();
        }

        lines.RemoveAt(index);
        Save(lines);
        return RedirectToAction(nameof(Index));
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> Checkout(int? tableId, string promo = "", string note = "")
    {
        try
        {
            var order = await service.Place(
                int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
                Read(),
                tableId,
                promo,
                note
            );
            Save(new List<CartLine>());
            HttpContext.Session.Remove("TableId");
            return Redirect("/Orders/Details/" + order.Id);
        }
        catch (InvalidOperationException ex)
        {
            if (!string.IsNullOrEmpty(Request.Headers["X-Requested-With"]))
            {
                return BadRequest(new { message = ex.Message });
            }

            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }
}
