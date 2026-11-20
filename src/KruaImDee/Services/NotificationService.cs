using KruaImDee.Data;
using KruaImDee.Models;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Services;

public class NotificationService
{
    private readonly AppDb db;

    public NotificationService(AppDb db)
    {
        this.db = db;
    }

    private async Task Send(
        int userId,
        string audience,
        string kind,
        int entityId,
        string key,
        string title,
        string message
    )
    {
        bool exists = await db.Notifications.AnyAsync(item =>
            item.UserId == userId && item.EventKey == key
        );
        if (
            exists
            || db.Notifications.Local.Any(item => item.UserId == userId && item.EventKey == key)
        )
        {
            return;
        }
        // ผู้เรียกบันทึกแจ้งเตือนพร้อมธุรกรรมเดิม จึงไม่ส่งข่าวจากรายการที่บันทึกล้มเหลว
        db.Notifications.Add(
            new AppNotification
            {
                UserId = userId,
                Audience = audience,
                Kind = kind,
                EntityId = entityId,
                EventKey = key,
                Title = title,
                Message = message,
            }
        );
    }

    private async Task Customer(
        int userId,
        string kind,
        int id,
        string key,
        string title,
        string message
    )
    {
        if (
            await db.Users.AnyAsync(user =>
                user.Id == userId && user.Active && user.Role == "Customer"
            )
        )
        {
            await Send(userId, "Customer", kind, id, key, title, message);
        }
    }

    private async Task Team(string kind, int id, string key, string title, string message)
    {
        var users = await db
            .Users.Where(user =>
                user.Active
                && (user.Role == "Staff" || user.Role == "Manager" || user.Role == "Admin")
            )
            .ToListAsync();
        foreach (AppUser user in users)
        {
            await Send(user.Id, "Team", kind, id, key, title, message);
        }
    }

    public async Task OrderChanged(Order order, bool paid = false)
    {
        string key = "Order:" + order.Id + ":" + order.Status;
        string title = "ออเดอร์ #" + order.Id.ToString("D4") + " " + Rules.Label(order.Status);
        if (paid)
        {
            await Customer(
                order.UserId,
                "Order",
                order.Id,
                "Payment:" + order.Id,
                "ชำระเงินออเดอร์ #" + order.Id.ToString("D4") + " แล้ว",
                "ดูรายละเอียดออเดอร์และใบเสร็จ"
            );
        }
        else
        {
            await Customer(order.UserId, "Order", order.Id, key, title, "ดูสถานะออเดอร์ล่าสุด");
        }
        if (order.Status == "Received")
        {
            await Team(
                "Order",
                order.Id,
                key,
                "มีออเดอร์ใหม่ #" + order.Id.ToString("D4"),
                "เปิดรายละเอียดเพื่อดำเนินการ"
            );
        }
        else if (order.Status == "Ready" && !paid)
        {
            await Team(
                "Order",
                order.Id,
                key,
                title,
                "อาหารพร้อมเสิร์ฟแล้ว เปิดออเดอร์เพื่อดำเนินการ"
            );
        }
    }

    public async Task ReservationChanged(Reservation reservation, bool created)
    {
        string key = "Reservation:" + reservation.Id + ":" + Guid.NewGuid().ToString("N");
        if (created)
        {
            await Team(
                "Reservation",
                reservation.Id,
                key,
                "มีการจองใหม่ #" + reservation.Id,
                "เปิดรายการจองเพื่อตรวจสอบและยืนยัน"
            );
        }
        else
        {
            await Customer(
                reservation.UserId,
                "Reservation",
                reservation.Id,
                key,
                "การจอง #" + reservation.Id + " " + Rules.Label(reservation.Status),
                "ดูรายละเอียดการจอง"
            );
        }
    }

    public async Task RefundChanged(RefundRequest refund, string action)
    {
        string key = "Refund:" + refund.Id + ":" + Guid.NewGuid().ToString("N");
        int owner = await db
            .Payments.Where(payment => payment.Id == refund.PaymentId)
            .Select(payment => payment.Order.UserId)
            .SingleAsync();
        string title =
            "คำขอ RF-" + refund.Id.ToString("D6") + " " + RefundService.Label(refund.Status);
        await Customer(owner, "Refund", refund.Id, key, title, "ดูผลคำขอคืนเงินของคุณ");

        if (action == "Request")
        {
            var managers = await db
                .Users.Where(user =>
                    user.Active
                    && user.Id != refund.RequestedBy
                    && (user.Role == "Manager" || user.Role == "Admin")
                )
                .ToListAsync();
            foreach (AppUser user in managers)
            {
                await Send(
                    user.Id,
                    "Manager",
                    "Refund",
                    refund.Id,
                    key,
                    "มีคำขอคืนเงิน RF-" + refund.Id.ToString("D6"),
                    "ยอด " + refund.Amount.ToString("N2") + " บาท รอตรวจคำขอ"
                );
            }
        }
        if (refund.Status == "Approved" || action == "ReassignPaying")
        {
            var accountants = await db
                .Users.Where(user =>
                    user.Active && user.Role == "Accountant" && user.Id != refund.DecidedBy
                )
                .ToListAsync();
            foreach (AppUser user in accountants)
            {
                if (refund.Status == "Paying" && refund.ClaimedBy != user.Id)
                {
                    continue;
                }
                await Send(
                    user.Id,
                    "Accountant",
                    "Refund",
                    refund.Id,
                    key,
                    title,
                    "เปิดคำขอเพื่อดำเนินการคืนเงิน"
                );
            }
        }
    }

    public IQueryable<AppNotification> Visible(int userId, string role)
    {
        var query = db.Notifications.Where(item => item.UserId == userId);
        if (role == "Customer")
        {
            return query.Where(item =>
                item.Audience == "Customer"
                && (
                    (
                        item.Kind == "Order"
                        && db.Orders.Any(order =>
                            order.Id == item.EntityId && order.UserId == userId
                        )
                    )
                    || (
                        item.Kind == "Reservation"
                        && db.Reservations.Any(reservation =>
                            reservation.Id == item.EntityId && reservation.UserId == userId
                        )
                    )
                    || (
                        item.Kind == "Refund"
                        && db.RefundRequests.Any(refund =>
                            refund.Id == item.EntityId && refund.Payment.Order.UserId == userId
                        )
                    )
                )
            );
        }
        if (role == "Accountant")
        {
            return query.Where(item =>
                item.Audience == "Accountant"
                && db.RefundRequests.Any(refund =>
                    refund.Id == item.EntityId
                    && refund.DecidedBy != userId
                    && (
                        refund.Status == "Approved"
                        || (refund.Status == "Paying" && refund.ClaimedBy == userId)
                    )
                )
            );
        }
        if (role == "Manager" || role == "Admin")
        {
            return query.Where(item =>
                item.Audience == "Team"
                || (
                    item.Audience == "Manager"
                    && db.RefundRequests.Any(refund =>
                        refund.Id == item.EntityId && refund.RequestedBy != userId
                    )
                )
            );
        }
        if (role == "Staff")
        {
            return query.Where(item => item.Audience == "Team");
        }
        return query.Where(item => false);
    }

    public async Task<NotificationCounts> Counts(int userId, string role)
    {
        var counts = new NotificationCounts();
        if (role == "Customer")
        {
            var unread = Visible(userId, role).Where(item => item.ReadAtUtc == null);
            counts.Orders = await unread.CountAsync(item => item.Kind == "Order");
            counts.Reservations = await unread.CountAsync(item => item.Kind == "Reservation");
            counts.Refunds = await unread.CountAsync(item => item.Kind == "Refund");
            return counts;
        }
        if (role == "Staff" || role == "Manager" || role == "Admin")
        {
            counts.Orders = await db.Orders.CountAsync(order =>
                !order.Closed && order.Status == "Received"
            );
            counts.Kitchen = await db.Orders.CountAsync(order =>
                !order.Closed
                && (
                    order.Status == "Received"
                    || order.Status == "Preparing"
                    || order.Status == "Ready"
                )
            );
            counts.Reservations = await db.Reservations.CountAsync(reservation =>
                reservation.Status == "Pending" && reservation.EndsAt > BusinessTime.Now
            );
        }
        if (role == "Manager" || role == "Admin")
        {
            counts.Refunds = await db.RefundRequests.CountAsync(refund =>
                refund.Status == "PendingApproval" && refund.RequestedBy != userId
            );
        }
        if (role == "Accountant")
        {
            counts.Refunds = await db.RefundRequests.CountAsync(refund =>
                refund.DecidedBy != userId
                && (
                    refund.Status == "Approved"
                    || (refund.Status == "Paying" && refund.ClaimedBy == userId)
                )
            );
        }
        return counts;
    }
}
