using KruaImDee.Data;
using KruaImDee.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Controllers;

public class HomeController : Controller
{
    private readonly AppDb db;
    private readonly ReviewService reviews;

    public HomeController(AppDb db, ReviewService reviews)
    {
        this.db = db;
        this.reviews = reviews;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.Promotions = await db
            .Promotions.Where(matchingPromotion =>
                matchingPromotion.Active
                && matchingPromotion.StartsAt <= DateTime.Now
                && matchingPromotion.EndsAt >= DateTime.Now
            )
            .OrderBy(matchingPromotion => matchingPromotion.EndsAt)
            .ToListAsync();
        var menu = await db
            .MenuItems.Include(matchingMenuItem => matchingMenuItem.Category)
            .Where(matchingMenuItem => matchingMenuItem.Active && matchingMenuItem.Category.Active)
            .OrderBy(matchingMenuItem => matchingMenuItem.Id)
            .ToListAsync();
        await reviews.LoadForMenus(menu);
        ViewBag.Categories = await db
            .Categories.Where(category => category.Active)
            .OrderBy(category => category.Id)
            .ToListAsync();
        ViewBag.FeaturedReviews = await reviews.Latest();
        ViewBag.ReviewSummary = await reviews.Summary();
        return View(menu);
    }

    public IActionResult Error()
    {
        return View();
    }
}
