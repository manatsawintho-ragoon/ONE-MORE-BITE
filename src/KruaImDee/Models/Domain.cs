using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KruaImDee.Models;

public class AppUser
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = "";

    [MaxLength(180)]
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";

    [MaxLength(20)]
    public string Role { get; set; } = "Customer";
    public string Phone { get; set; } = "";
    public bool Active { get; set; } = true;
}

public class Category
{
    public int Id { get; set; }

    [MaxLength(80)]
    public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
}

public class MenuItem
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public decimal DiscountPercent { get; set; }
    public List<Review> Reviews { get; set; } = new List<Review>();

    [NotMapped]
    public decimal EffectivePrice
    {
        get
        {
            decimal price = Price * (100 - DiscountPercent) / 100;
            return Math.Max(0, Math.Round(price, 2, MidpointRounding.AwayFromZero));
        }
    }

    [NotMapped]
    public int ReviewCount
    {
        get { return Reviews.Count; }
    }

    [NotMapped]
    public decimal AverageRating
    {
        get
        {
            if (Reviews.Count == 0)
            {
                return 0;
            }

            return Math.Round((decimal)Reviews.Average(review => review.Rating), 1);
        }
    }

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public string ImageUrl { get; set; } = "/images/food-01.jpg";
    public bool Active { get; set; } = true;
    public bool SoldOut { get; set; }
}

public class DiningTable
{
    public int Id { get; set; }

    [MaxLength(30)]
    public string Name { get; set; } = "";
    public int Capacity { get; set; } = 4;
    public bool Active { get; set; } = true;
}

public class Promotion
{
    public int Id { get; set; }

    [MaxLength(30)]
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Percentage { get; set; } = true;
    public decimal Value { get; set; }
    public decimal Minimum { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool Active { get; set; } = true;
}

public class Order
{
    [MaxLength(64)]
    public string? PosRequestKey { get; set; }
    public int? PosOperatorId { get; set; }
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public int? DiningTableId { get; set; }
    public DiningTable? DiningTable { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Type { get; set; } = "Takeaway";
    public string Status { get; set; } = "Received";
    public string Note { get; set; } = "";
    public string PromoCode { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public DateTime? PaymentExpiresAt { get; set; }
    public decimal Total { get; set; }
    public bool Closed { get; set; }
    public List<OrderItem> Items { get; set; } = new List<OrderItem>();
    public Payment? Payment { get; set; }
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;
    public string Name { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal OriginalUnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public int Quantity { get; set; }
    public string Note { get; set; } = "";
}

public class Payment
{
    public bool IsMock { get; set; }
    public string SlipFileName { get; set; } = "";
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public decimal Amount { get; set; }
    public decimal Received { get; set; }
    public decimal Change { get; set; }
    public string Method { get; set; } = "Cash";
    public DateTime PaidAt { get; set; } = KruaImDee.Services.BusinessTime.Now;
    public int ActorId { get; set; }
    public bool Refunded { get; set; }
}

public class Reservation
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public int? DiningTableId { get; set; }
    public DiningTable? DiningTable { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public int Guests { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Note { get; set; } = "";
    public string Status { get; set; } = "Pending";
}

public class AuditLog
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int ActorId { get; set; }
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}

public class RestaurantSetting
{
    public string TaxLegalName { get; set; } = "ร้านเอาอีก (ONE MORE BITE)";
    public string TaxAddress { get; set; } =
        "99 ถนนพหลโยธิน แขวงเสนานิคม เขตจตุจักร กรุงเทพมหานคร 10900";
    public string TaxId { get; set; } = "0000000000000";
    public string TaxBranch { get; set; } = "00000";
    public int Id { get; set; }
    public string Name { get; set; } = "เอาอีก";
    public string Phone { get; set; } = "02-123-4567";
    public string Address { get; set; } = "กรุงเทพมหานคร";
}

public class CartLine
{
    public int MenuItemId { get; set; }
    public int Quantity { get; set; }
    public string Note { get; set; } = "";

    public CartLine(int menuItemId, int quantity, string note)
    {
        MenuItemId = menuItemId;
        Quantity = quantity;
        Note = note;
    }
}

public class CartPage
{
    public List<CartEntry> Lines { get; set; } = new List<CartEntry>();
    public List<DiningTable> Tables { get; set; } = new List<DiningTable>();

    public decimal Total
    {
        get
        {
            decimal total = 0;
            foreach (CartEntry entry in Lines)
            {
                total += entry.Item.EffectivePrice * entry.Line.Quantity;
            }

            return total;
        }
    }

    public decimal VatAmount
    {
        get { return Math.Round(Total * 7 / 100, 2, MidpointRounding.AwayFromZero); }
    }

    public decimal GrandTotal
    {
        get { return Total + VatAmount; }
    }
}

public class CartEntry
{
    public MenuItem Item { get; set; } = null!;
    public CartLine Line { get; set; } = null!;
}

public class BestSeller
{
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Total { get; set; }
}

public class ReportPage
{
    public decimal TotalAmount { get; set; }
    public int PaymentCount { get; set; }
    public decimal CashAmount { get; set; }
    public decimal TransferAmount { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public List<Payment> Payments { get; set; } = new List<Payment>();
    public List<BestSeller> BestSellers { get; set; } = new List<BestSeller>();
}

public class Review
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;
    public int Rating { get; set; }

    [MaxLength(1000)]
    public string Comment { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class ReviewPurchase
{
    public int OrderId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ReviewSummary
{
    public int Count { get; set; }
    public decimal Average { get; set; }
}

public class MenuSuggestion
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public decimal Price { get; set; }
    public decimal OriginalPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public double Score { get; set; }
}

public class CartQuote
{
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
    public string Message { get; set; } = "";
}
