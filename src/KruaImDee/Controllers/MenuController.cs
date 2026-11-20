using System.Security.Claims;
using KruaImDee.Data;
using KruaImDee.Models;
using KruaImDee.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Controllers;

public class MenuController : Controller
{
    private readonly AppDb db;
    private readonly ReviewService reviews;

    public MenuController(AppDb db, ReviewService reviews)
    {
        this.db = db;
        this.reviews = reviews;
    }

    public async Task<IActionResult> Index(string? q, int? category, int? table)
    {
        if (table.HasValue)
        {
            HttpContext.Session.SetInt32("TableId", table.Value);
        }

        ViewBag.Categories = await db
            .Categories.Where(matchingCategory => matchingCategory.Active)
            .ToListAsync();
        ViewBag.Query = q;
        ViewBag.Category = category;
        var items = db
            .MenuItems.Include(matchingMenuItem => matchingMenuItem.Category)
            .Where(matchingMenuItem => matchingMenuItem.Active && matchingMenuItem.Category.Active);
        if (category.HasValue)
        {
            items = items.Where(matchingMenuItem => matchingMenuItem.CategoryId == category);
        }

        var menu = await items.OrderBy(item => item.Id).ToListAsync();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var suggestions = MenuSearch.Suggest(menu, q, menu.Count);
            var ranked = new List<MenuItem>();
            foreach (MenuSuggestion suggestion in suggestions)
            {
                ranked.Add(menu.First(item => item.Id == suggestion.Id));
            }

            menu = ranked;
        }

        await reviews.LoadForMenus(menu);
        return View(menu);
    }

    public async Task<IActionResult> Details(int id)
    {
        var menuItem = await db
            .MenuItems.Include(matchingMenuItem => matchingMenuItem.Category)
            .SingleOrDefaultAsync(matchingMenuItem =>
                matchingMenuItem.Id == id
                && matchingMenuItem.Active
                && matchingMenuItem.Category.Active
            );
        if (menuItem == null)
        {
            return NotFound();
        }

        await reviews.LoadForMenus(new List<MenuItem> { menuItem });
        ViewBag.ReviewOrders = new List<ReviewPurchase>();
        if (User.IsInRole("Customer"))
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            ViewBag.ReviewOrders = await reviews.EligiblePurchases(userId, id);
        }

        return View(menuItem);
    }

    public async Task<IActionResult> Suggest(string? q)
    {
        var menu = await db
            .MenuItems.Include(item => item.Category)
            .Where(item => item.Active && item.Category.Active && !item.SoldOut)
            .OrderBy(item => item.Id)
            .ToListAsync();
        return Json(MenuSearch.Suggest(menu, q ?? ""));
    }
}
