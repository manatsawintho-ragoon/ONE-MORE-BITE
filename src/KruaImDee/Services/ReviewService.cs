using System.Data;
using KruaImDee.Data;
using KruaImDee.Models;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Services;

public class ReviewService
{
    private readonly AppDb db;
    private readonly RestaurantService restaurant;

    public ReviewService(AppDb db, RestaurantService restaurant)
    {
        this.db = db;
        this.restaurant = restaurant;
    }

    private IQueryable<Review> VerifiedReviews()
    {
        return db
            .Reviews.Include(review => review.User)
            .Where(review =>
                review.Order.Status == "Served"
                && review.Order.Payment != null
                && !review.Order.Payment.Refunded
                && review.MenuItem.Active
                && review.MenuItem.Category.Active
            );
    }

    public async Task LoadForMenus(List<MenuItem> items)
    {
        int[] menuIds = items.Select(item => item.Id).ToArray();
        var reviews = await VerifiedReviews()
            .Where(review => menuIds.Contains(review.MenuItemId))
            .OrderByDescending(review => review.CreatedAt)
            .ToListAsync();
        foreach (MenuItem item in items)
        {
            item.Reviews = reviews.Where(review => review.MenuItemId == item.Id).ToList();
        }
    }

    public async Task<List<Review>> Latest()
    {
        return await VerifiedReviews()
            .Include(review => review.MenuItem)
            .OrderByDescending(review => review.CreatedAt)
            .Take(6)
            .ToListAsync();
    }

    public async Task<ReviewSummary> Summary()
    {
        var ratings = await VerifiedReviews().Select(review => review.Rating).ToListAsync();
        var summary = new ReviewSummary { Count = ratings.Count };
        if (ratings.Count > 0)
        {
            summary.Average = Math.Round((decimal)ratings.Average(), 1);
        }

        return summary;
    }

    public async Task<List<ReviewPurchase>> EligiblePurchases(int userId, int menuItemId)
    {
        return await db
            .Orders.Where(order =>
                order.UserId == userId
                && order.Status == "Served"
                && order.Payment != null
                && !order.Payment.Refunded
                && order.Items.Any(item => item.MenuItemId == menuItemId)
                && !db.Reviews.Any(review =>
                    review.OrderId == order.Id && review.MenuItemId == menuItemId
                )
            )
            .OrderByDescending(order => order.CreatedAt)
            .Select(order => new ReviewPurchase { OrderId = order.Id, CreatedAt = order.CreatedAt })
            .ToListAsync();
    }

    public async Task<bool> Create(
        int userId,
        int orderId,
        int menuItemId,
        int rating,
        string comment
    )
    {
        comment = (comment ?? "").Trim();
        if (rating < 1 || rating > 5 || comment.Length < 1 || comment.Length > 1000)
        {
            throw new InvalidOperationException(
                "กรุณาให้คะแนน 1 ถึง 5 และเขียนรีวิวไม่เกิน 1,000 ตัวอักษร"
            );
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable
        );
        await restaurant.LockCapacity();
        var order = await db
            .Orders.Include(item => item.Items)
            .Include(item => item.Payment)
            .SingleOrDefaultAsync(item => item.Id == orderId);
        if (
            order == null
            || order.UserId != userId
            || order.Status != "Served"
            || order.Payment == null
            || order.Payment.Refunded
            || !order.Items.Any(item => item.MenuItemId == menuItemId)
        )
        {
            throw new InvalidOperationException(
                "รีวิวได้เฉพาะอาหารที่คุณซื้อ ชำระเงินและรับอาหารแล้ว"
            );
        }

        if (
            await db.Reviews.AnyAsync(review =>
                review.OrderId == orderId && review.MenuItemId == menuItemId
            )
        )
        {
            return false;
        }

        var review = new Review
        {
            UserId = userId,
            OrderId = orderId,
            MenuItemId = menuItemId,
            Rating = rating,
            Comment = comment,
        };
        db.Reviews.Add(review);
        restaurant.Audit(userId, "Review", "Order #" + orderId + ", menu #" + menuItemId);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }
}
