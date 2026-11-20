using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KruaImDee.Models;

public class AppNotification
{
    public int Id { get; set; }
    public int UserId { get; set; }

    [MaxLength(100)]
    public string EventKey { get; set; } = "";

    [MaxLength(20)]
    public string Audience { get; set; } = "";

    [MaxLength(20)]
    public string Kind { get; set; } = "";
    public int EntityId { get; set; }

    [MaxLength(160)]
    public string Title { get; set; } = "";

    [MaxLength(300)]
    public string Message { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAtUtc { get; set; }

    [NotMapped]
    public string Url
    {
        get
        {
            if (Kind == "Order")
            {
                return "/Orders/Details/" + EntityId;
            }
            if (Kind == "Refund")
            {
                return "/Refunds/Details/" + EntityId;
            }
            if (Audience == "Customer")
            {
                return "/Reservations?id=" + EntityId;
            }
            return "/Staff/Reservations?id=" + EntityId;
        }
    }
}

public class NotificationCounts
{
    public int Orders { get; set; }
    public int Kitchen { get; set; }
    public int Reservations { get; set; }
    public int Refunds { get; set; }
}
