using System.Data;
using KruaImDee.Data;
using KruaImDee.Models;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Services;

// Validate prices and update orders inside SQL transactions.
public class RestaurantService
{
    private readonly AppDb db;
    private readonly NotificationService notifications;

    public RestaurantService(AppDb db, NotificationService notifications)
    {
        this.db = db;
        this.notifications = notifications;
    }

    public void Audit(int actor, string action, string detail)
    {
        var log = new AuditLog
        {
            ActorId = actor,
            Action = action,
            Detail = detail,
        };
        db.AuditLogs.Add(log);
    }

    // Serialize capacity changes with a transaction-owned SQL lock.
    public async Task LockCapacity()
    {
        if (db.Database.IsSqlite())
        {
            // SQLite จองสิทธิ์เขียนตั้งแต่เริ่ม transaction แล้ว ไม่ใช้ lock ของ SQL Server
            return;
        }
        string command =
            "DECLARE @r int; "
            + "EXEC @r = sp_getapplock @Resource='KruaImDee-capacity', "
            + "@LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; "
            + "IF @r < 0 THROW 51000, 'Restaurant busy; retry', 1;";
        await db.Database.ExecuteSqlRawAsync(command);
    }

    public async Task<Order> Place(
        int userId,
        List<CartLine> lines,
        int? tableId,
        string promo,
        string note,
        string? posRequestKey = null
    )
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("ตะกร้ายังว่าง");
        }

        if (lines.Count > 50)
        {
            throw new InvalidOperationException("รายการมากเกินไป");
        }

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockCapacity();
        if (posRequestKey != null)
        {
            var existing = await db
                .Orders.Include(matchingOrder => matchingOrder.Items)
                .Include(matchingOrder => matchingOrder.Payment)
                .SingleOrDefaultAsync(matchingOrder =>
                    matchingOrder.PosRequestKey == posRequestKey
                );
            if (existing != null)
            {
                if (existing.PosOperatorId != userId)
                {
                    throw new InvalidOperationException("ไม่สามารถเปิดบิลนี้ได้");
                }

                await tx.CommitAsync();
                return existing;
            }
        }

        int actorId = userId;
        var actor = await db.Users.FindAsync(userId);
        bool team =
            actor != null
            && (actor.Role == "Admin" || actor.Role == "Manager" || actor.Role == "Staff");
        if (tableId.HasValue)
        {
            var seated = await db.Reservations.SingleOrDefaultAsync(matchingReservation =>
                matchingReservation.DiningTableId == tableId
                && matchingReservation.Status == "Seated"
            );
            if (seated != null)
            {
                if (team)
                {
                    userId = seated.UserId;
                }
                else if (seated.UserId != userId)
                {
                    throw new InvalidOperationException("โต๊ะนี้กำลังให้บริการผู้จอง");
                }
            }

            var table = await db.DiningTables.FindAsync(tableId);
            if (
                table == null
                || !table.Active
                || await db.Orders.AnyAsync(matchingOrder =>
                    matchingOrder.DiningTableId == tableId && !matchingOrder.Closed
                )
            )
            {
                throw new InvalidOperationException("โต๊ะนี้ไม่พร้อมใช้งาน");
            }

            if (
                await db.Reservations.AnyAsync(matchingReservation =>
                    matchingReservation.DiningTableId == tableId
                    && matchingReservation.Status != "Cancelled"
                    && matchingReservation.Status != "Completed"
                    && matchingReservation.UserId != userId
                    && matchingReservation.StartsAt < DateTime.Now.AddMinutes(90)
                    && matchingReservation.EndsAt > DateTime.Now
                )
            )
            {
                throw new InvalidOperationException("โต๊ะนี้มีการจองในช่วงเวลานี้");
            }
        }

        var order = new Order
        {
            PosRequestKey = posRequestKey,
            PosOperatorId = posRequestKey == null ? null : actorId,
            Status = posRequestKey == null ? "Received" : "AwaitingPayment",
            UserId = userId,
            DiningTableId = tableId,
            Type = tableId.HasValue ? "DineIn" : "Takeaway",
            Note = (note ?? "").Trim(),
        };
        await ApplyPricing(order, lines, promo);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        await notifications.OrderChanged(order);
        Audit(actorId, "CreateOrder", $"Order #{order.Id}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return order;
    }

    public async Task<CartQuote> Quote(List<CartLine> lines, string promo)
    {
        var order = new Order();
        await ApplyPricing(order, lines, promo);
        return new CartQuote
        {
            Subtotal = order.Subtotal,
            Discount = order.Discount,
            VatRate = order.VatRate,
            VatAmount = order.VatAmount,
            Total = order.Total,
            Message = "ตรวจสอบยอดแล้ว กรุณายืนยันออเดอร์",
        };
    }

    private async Task ApplyPricing(Order order, List<CartLine> lines, string promo)
    {
        if (lines.Count == 0 || lines.Count > 50)
        {
            throw new InvalidOperationException("กรุณาเลือกอาหาร 1 ถึง 50 รายการ");
        }

        foreach (var line in lines)
        {
            Rules.Quantity(line.Quantity);
            var menuItem = await db
                .MenuItems.Include(matchingMenuItem => matchingMenuItem.Category)
                .SingleOrDefaultAsync(matchingMenuItem => matchingMenuItem.Id == line.MenuItemId);
            if (
                menuItem == null
                || !menuItem.Active
                || !menuItem.Category.Active
                || menuItem.SoldOut
            )
            {
                throw new InvalidOperationException("อาหารบางรายการไม่พร้อมจำหน่าย");
            }

            order.Items.Add(
                new OrderItem
                {
                    MenuItemId = menuItem.Id,
                    Name = menuItem.Name,
                    OriginalUnitPrice = menuItem.Price,
                    DiscountPercent = menuItem.DiscountPercent,
                    UnitPrice = menuItem.EffectivePrice,
                    Quantity = line.Quantity,
                    Note = (line.Note ?? "").Trim(),
                }
            );
        }

        order.Subtotal = order.Items.Sum(matchingOrderItem =>
            matchingOrderItem.UnitPrice * matchingOrderItem.Quantity
        );
        if (!string.IsNullOrWhiteSpace(promo))
        {
            promo = promo.Trim().ToUpperInvariant();
            var promotion = await db.Promotions.SingleOrDefaultAsync(matchingPromotion =>
                matchingPromotion.Code == promo && matchingPromotion.Active
            );
            if (
                promotion == null
                || promotion.StartsAt > DateTime.Now
                || promotion.EndsAt < DateTime.Now
                || promotion.Minimum > order.Subtotal
            )
            {
                throw new InvalidOperationException(
                    "รหัสส่วนลดไม่ถูกต้อง หมดอายุ หรือยอดไม่ถึงขั้นต่ำ"
                );
            }

            order.PromoCode = promotion.Code;
            order.Discount = Rules.Discount(order.Subtotal, promotion.Value, promotion.Percentage);
        }

        order.VatRate = 7;
        order.VatAmount = Rules.Vat(order.Subtotal - order.Discount, order.VatRate);
        order.Total = order.Subtotal - order.Discount + order.VatAmount;
    }

    public async Task StartCustomerPayment(int id, int actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockCapacity();
        var order = await db
            .Orders.Include(matchingOrder => matchingOrder.Payment)
            .SingleAsync(matchingOrder => matchingOrder.Id == id);
        if (order.UserId != actor || order.Status == "Cancelled")
        {
            throw new InvalidOperationException("ไม่สามารถชำระออเดอร์นี้ได้");
        }

        if (order.Payment == null && order.PaymentExpiresAt == null)
        {
            order.PaymentExpiresAt = DateTime.UtcNow.AddMinutes(30);
            Audit(actor, "StartMockPayment", $"Order #{id}");
            await db.SaveChangesAsync();
        }

        await tx.CommitAsync();
    }

    public async Task ConfirmSlipPayment(int id, int actor, byte[] bytes, string folder)
    {
        string extension = SlipImage.Extension(bytes);
        if (bytes.Length > 5 * 1024 * 1024 || extension == "")
        {
            throw new InvalidOperationException("กรุณาใช้รูป JPEG, PNG หรือ WebP ขนาดไม่เกิน 5 MB");
        }

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockCapacity();
        var order = await db
            .Orders.Include(matchingOrder => matchingOrder.Payment)
            .SingleAsync(matchingOrder => matchingOrder.Id == id);
        if (order.UserId != actor)
        {
            throw new InvalidOperationException("ไม่สามารถชำระออเดอร์นี้ได้");
        }

        if (order.Payment != null)
        {
            await tx.CommitAsync();
            return;
        }

        if (
            order.Status == "Cancelled"
            || order.PaymentExpiresAt == null
            || order.PaymentExpiresAt <= DateTime.UtcNow
        )
        {
            throw new InvalidOperationException("หมดเวลาชำระเงินหรือออเดอร์ถูกยกเลิกแล้ว");
        }

        Directory.CreateDirectory(folder);
        string filename = Guid.NewGuid().ToString("N") + extension;
        string path = Path.Combine(folder, filename);
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            db.Payments.Add(
                new Payment
                {
                    OrderId = id,
                    Amount = order.Total,
                    Received = order.Total,
                    Change = 0,
                    Method = "Transfer",
                    ActorId = actor,
                    SlipFileName = filename,
                }
            );
            order.Closed = order.Status == "Served";
            if (order.Status == "AwaitingPayment")
            {
                order.Status = "Received";
            }

            await notifications.OrderChanged(order, true);
            Audit(actor, "SlipPayment", $"Order #{id} {order.Total}");
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            throw;
        }
    }

    public async Task Settle(
        int id,
        int actor,
        string method,
        decimal received,
        bool transferConfirmed
    )
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockCapacity();
        db.ChangeTracker.Clear();
        var order = await db
            .Orders.Include(matchingOrder => matchingOrder.Payment)
            .SingleAsync(matchingOrder => matchingOrder.Id == id);
        if (order.Payment != null)
        {
            await tx.CommitAsync();
            return;
        }

        if (order.Closed || order.Status == "Cancelled")
        {
            throw new InvalidOperationException("ไม่สามารถชำระรายการที่ยกเลิก");
        }

        if (method != "Cash" && method != "Transfer")
        {
            throw new InvalidOperationException("วิธีชำระเงินไม่ถูกต้อง");
        }

        if (method == "Transfer" && !transferConfirmed)
        {
            throw new InvalidOperationException("ต้องตรวจสอบยอดโอนก่อนยืนยัน");
        }

        if (method == "Cash" && received < order.Total)
        {
            throw new InvalidOperationException("เงินที่รับไม่เพียงพอ");
        }

        if (method == "Cash" && decimal.Round(received, 2) != received)
        {
            throw new InvalidOperationException("ยอดเงินที่รับต้องมีทศนิยมไม่เกิน 2 ตำแหน่ง");
        }

        if (received > 1000000)
        {
            throw new InvalidOperationException("ยอดเงินที่รับสูงเกินกำหนด");
        }

        db.Payments.Add(
            new Payment
            {
                OrderId = id,
                Amount = order.Total,
                Received = method == "Cash" ? received : order.Total,
                Change = method == "Cash" ? received - order.Total : 0,
                Method = method,
                ActorId = actor,
            }
        );
        order.Closed = order.Status == "Served";
        if (order.Status == "AwaitingPayment")
        {
            order.Status = "Received";
        }

        await notifications.OrderChanged(order, true);
        Audit(actor, "Payment", $"Order #{id} {method} {order.Total}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task ChangePendingPos(string requestKey, int actor, bool sendToKitchen)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable
        );
        await LockCapacity();
        db.ChangeTracker.Clear();
        var order = await db
            .Orders.Include(matchingOrder => matchingOrder.Payment)
            .SingleOrDefaultAsync(matchingOrder =>
                matchingOrder.PosRequestKey == requestKey && matchingOrder.PosOperatorId == actor
            );
        if (order == null || order.Payment != null || order.Status != "AwaitingPayment")
        {
            throw new InvalidOperationException("บิลนี้ไม่อยู่ระหว่างรอชำระเงิน");
        }

        if (sendToKitchen)
        {
            if (order.DiningTableId == null)
            {
                throw new InvalidOperationException(
                    "ส่งครัวก่อนชำระเงินได้เฉพาะรายการนั่งทานที่ร้าน"
                );
            }

            order.Status = "Received";
        }
        else
        {
            order.Status = "Cancelled";
            order.Closed = true;
        }

        await notifications.OrderChanged(order);
        Audit(actor, sendToKitchen ? "PosSendKitchen" : "PosCancelPending", $"Order #{order.Id}");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task Advance(int id, int actor, string status)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockCapacity();
        var order = await db
            .Orders.Include(matchingOrder => matchingOrder.Payment)
            .SingleAsync(matchingOrder => matchingOrder.Id == id);
        if (!Rules.CanAdvance(order.Status, status))
        {
            throw new InvalidOperationException("ไม่สามารถข้ามขั้นตอนหรือย้อนสถานะได้");
        }

        order.Status = status;
        order.Closed = status == "Served" && order.Payment != null;
        await notifications.OrderChanged(order);
        Audit(actor, "Kitchen", $"Order #{id}: {status}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task Void(int id, int actor, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("กรุณาระบุเหตุผล");
        }

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockCapacity();
        var order = await db
            .Orders.Include(matchingOrder => matchingOrder.Payment)
            .SingleAsync(matchingOrder => matchingOrder.Id == id);
        if (order.Status == "Cancelled")
        {
            throw new InvalidOperationException("รายการนี้ยกเลิกแล้ว");
        }

        if (order.Payment != null)
        {
            throw new InvalidOperationException("รายการชำระแล้วต้องสร้างคำขอคืนเงินและรออนุมัติ");
        }

        order.Status = "Cancelled";
        order.Closed = true;
        await notifications.OrderChanged(order);
        Audit(actor, "Void", $"Order #{id}: {reason}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<Reservation> Book(
        int userId,
        DateTime startsAt,
        int guests,
        string name,
        string phone,
        string note,
        int? tableId = null
    )
    {
        if (
            startsAt < DateTime.Now
            || startsAt.TimeOfDay < TimeSpan.FromHours(10)
            || startsAt.AddMinutes(90).Date != startsAt.Date
            || startsAt.AddMinutes(90).TimeOfDay > TimeSpan.FromHours(22)
            || guests < 1
            || guests > 6
            || string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(phone)
        )
        {
            throw new InvalidOperationException(
                "ตรวจสอบชื่อ เบอร์โทร จำนวน 1 ถึง 6 คน และเวลาจอง 10:00 ถึง 20:30 น."
            );
        }

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockCapacity();
        var end = startsAt.AddMinutes(90);
        var tables = await db
            .DiningTables.Where(matchingTable =>
                matchingTable.Active
                && matchingTable.Capacity >= guests
                && (!tableId.HasValue || matchingTable.Id == tableId)
            )
            .OrderBy(matchingTable => matchingTable.Capacity)
            .ToListAsync();
        DiningTable? chosen = null;
        foreach (DiningTable table in tables)
        {
            bool hasConflictingReservation = await db.Reservations.AnyAsync(matchingReservation =>
                matchingReservation.DiningTableId == table.Id
                && matchingReservation.Status != "Cancelled"
                && matchingReservation.Status != "Completed"
                && (
                    matchingReservation.StartsAt < end && matchingReservation.EndsAt > startsAt
                    || matchingReservation.Status == "Seated"
                        && startsAt < DateTime.Now.AddMinutes(90)
                        && end > DateTime.Now
                )
            );
            if (hasConflictingReservation)
            {
                continue;
            }

            // ถ้าจองใกล้เวลาปัจจุบัน ต้องตรวจลูกค้าที่กำลังใช้โต๊ะด้วย
            if (startsAt < DateTime.Now.AddMinutes(90))
            {
                bool hasOpenOrder = await db.Orders.AnyAsync(order =>
                    order.DiningTableId == table.Id && !order.Closed
                );
                if (hasOpenOrder)
                {
                    continue;
                }
            }

            chosen = table;
            break;
        }

        if (chosen == null)
        {
            throw new InvalidOperationException("ไม่มีโต๊ะว่างในช่วงเวลานี้ กรุณาเลือกเวลาอื่น");
        }

        var reservation = new Reservation
        {
            UserId = userId,
            DiningTableId = chosen.Id,
            StartsAt = startsAt,
            EndsAt = end,
            Guests = guests,
            Name = name,
            Phone = phone,
            Note = note ?? "",
        };
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();
        await notifications.ReservationChanged(reservation, true);
        Audit(userId, "Reservation", $"Reservation #{reservation.Id} table {chosen.Name}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return reservation;
    }

    public async Task BookingStatus(int id, int actor, string status, int? tableId, bool customer)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockCapacity();
        var reservation = await db.Reservations.SingleAsync(matchingReservation =>
            matchingReservation.Id == id
        );
        if (
            customer
            && (
                reservation.UserId != actor
                || status != "Cancelled"
                || (reservation.Status != "Pending" && reservation.Status != "Confirmed")
            )
        )
        {
            throw new InvalidOperationException("ไม่สามารถแก้ไขการจองนี้ได้");
        }

        bool validTransition = false;
        if (reservation.Status == "Pending" && (status == "Confirmed" || status == "Cancelled"))
        {
            validTransition = true;
        }

        if (reservation.Status == "Confirmed" && (status == "Seated" || status == "Cancelled"))
        {
            validTransition = true;
        }

        if (reservation.Status == "Seated" && status == "Completed")
        {
            validTransition = true;
        }

        if (!customer && !validTransition)
        {
            throw new InvalidOperationException("สถานะการจองไม่ถูกต้อง");
        }

        if (tableId.HasValue)
        {
            var table = await db.DiningTables.FindAsync(tableId);
            if (
                table == null
                || !table.Active
                || table.Capacity < reservation.Guests
                || await db.Reservations.AnyAsync(matchingReservation =>
                    matchingReservation.Id != id
                    && matchingReservation.DiningTableId == tableId
                    && matchingReservation.Status != "Cancelled"
                    && matchingReservation.Status != "Completed"
                    && matchingReservation.StartsAt < reservation.EndsAt
                    && matchingReservation.EndsAt > reservation.StartsAt
                )
            )
            {
                throw new InvalidOperationException("โต๊ะนี้ไม่ว่างหรือความจุไม่พอ");
            }

            reservation.DiningTableId = tableId;
        }

        if (
            status == "Seated"
            && (
                await db.Orders.AnyAsync(matchingOrder =>
                    matchingOrder.DiningTableId == reservation.DiningTableId
                    && !matchingOrder.Closed
                )
                || await db.Reservations.AnyAsync(matchingReservation =>
                    matchingReservation.Id != id
                    && matchingReservation.DiningTableId == reservation.DiningTableId
                    && matchingReservation.Status == "Seated"
                )
            )
        )
        {
            throw new InvalidOperationException("โต๊ะกำลังให้บริการออเดอร์อื่น");
        }

        reservation.Status = status;
        await notifications.ReservationChanged(reservation, false);
        Audit(actor, "BookingStatus", $"Reservation #{id}: {status}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
