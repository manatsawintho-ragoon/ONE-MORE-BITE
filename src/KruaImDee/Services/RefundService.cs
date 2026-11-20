using System.Data;
using KruaImDee.Data;
using KruaImDee.Models;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Services;

public class RefundConflictException : Exception
{
    public RefundConflictException()
        : base("รายการเปลี่ยนแปลงแล้ว กรุณาเปิดรายละเอียดใหม่ก่อนทำต่อ") { }
}

public class RefundService
{
    private readonly AppDb db;
    private readonly NotificationService notifications;
    private readonly RestaurantService restaurant;

    public RefundService(AppDb db, RestaurantService restaurant, NotificationService notifications)
    {
        this.db = db;
        this.notifications = notifications;
        this.restaurant = restaurant;
    }

    public static string Label(string status)
    {
        switch (status)
        {
            case "PendingApproval":
                return "รออนุมัติ";
            case "Approved":
                return "อนุมัติแล้ว รอคืนเงิน";
            case "Paying":
                return "นักบัญชีรับงานแล้ว";
            case "Completed":
                return "คืนเงินแล้ว";
            case "Rejected":
                return "ไม่อนุมัติ";
            case "Withdrawn":
                return "ถอนคำขอ";
            case "Revoked":
                return "เพิกถอนอนุมัติ";
            default:
                return status;
        }
    }

    private async Task<AppUser> Actor(int id, params string[] roles)
    {
        var user = await db.Users.SingleOrDefaultAsync(matchingUser =>
            matchingUser.Id == id && matchingUser.Active
        );
        if (user == null || !roles.Contains(user.Role))
        {
            throw new UnauthorizedAccessException();
        }

        return user;
    }

    private static string Required(string? value, int max, string label)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > max)
        {
            throw new InvalidOperationException($"กรอก{label}ไม่เกิน {max} ตัวอักษร");
        }

        return value;
    }

    public async Task<int> Request(
        int orderId,
        int actor,
        string reason,
        string method,
        string recipientName,
        string destination,
        string requestKey
    )
    {
        reason = Required(reason, 500, "เหตุผล");
        recipientName = Required(recipientName, 100, "ชื่อผู้รับ");
        destination = Required(destination, 300, "ช่องทางรับเงิน/บัญชีปลายทาง");
        requestKey = Required(requestKey, 64, "รหัสคำขอ");
        if (method != "Cash" && method != "Transfer")
        {
            throw new InvalidOperationException("วิธีคืนเงินไม่ถูกต้อง");
        }

        // บันทึกทุกขั้นตอนพร้อมกัน และกันผู้ใช้สองคนแก้บิลเดียวกันในเวลาเดียวกัน
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable
        );
        await restaurant.LockCapacity();
        db.ChangeTracker.Clear();
        var user = await Actor(actor, "Customer", "Staff", "Manager", "Admin", "Accountant");
        // ตรวจเจ้าของบนเซิร์ฟเวอร์ แม้มีการแก้เลขออเดอร์ในแบบฟอร์ม
        if (user.Role == "Customer")
        {
            bool ownsOrder = await db.Orders.AnyAsync(order =>
                order.Id == orderId && order.UserId == actor
            );
            if (!ownsOrder)
            {
                throw new UnauthorizedAccessException();
            }
        }
        var existing = await db
            .RefundRequests.Include(matchingRefund => matchingRefund.Payment)
            .SingleOrDefaultAsync(matchingRefund =>
                matchingRefund.RequestedBy == actor && matchingRefund.RequestKey == requestKey
            );
        if (existing != null)
        {
            if (
                existing.Payment.OrderId != orderId
                || existing.Reason != reason
                || existing.Method != method
                || existing.RecipientName != recipientName
                || existing.Destination != destination
            )
            {
                throw new RefundConflictException();
            }

            return existing.Id;
        }

        var payment = await db
            .Payments.Include(matchingPayment => matchingPayment.Order)
            .SingleOrDefaultAsync(matchingPayment => matchingPayment.OrderId == orderId);
        if (
            payment == null
            || payment.Refunded
            || payment.Amount <= 0
            || payment.Order.Status == "Cancelled"
        )
        {
            throw new InvalidOperationException(
                "ขอคืนได้เฉพาะออเดอร์ที่ชำระเงินแล้วและยังไม่คืนเงิน"
            );
        }

        if (
            await db.RefundRequests.AnyAsync(matchingRefund =>
                matchingRefund.ActivePaymentId == payment.Id
            )
        )
        {
            throw new InvalidOperationException(
                "มีคำขอคืนเงินสำหรับบิลนี้แล้ว กรุณาตรวจสอบรายการเดิม"
            );
        }

        var request = new RefundRequest
        {
            PaymentId = payment.Id,
            ActivePaymentId = payment.Id,
            Amount = payment.Amount,
            RequestedBy = actor,
            RequestedAtUtc = DateTime.UtcNow,
            Reason = reason,
            Method = method,
            RecipientName = recipientName,
            Destination = destination,
            RequestKey = requestKey,
        };
        db.RefundRequests.Add(request);
        await db.SaveChangesAsync();
        await notifications.RefundChanged(request, "Request");
        restaurant.Audit(actor, "RefundRequest", $"Refund #{request.Id}, Order #{orderId}");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return request.Id;
    }

    public async Task Change(
        int id,
        int actor,
        string action,
        string version,
        string reason = "",
        int? assignee = null
    )
    {
        // บันทึกทุกขั้นตอนพร้อมกัน และกันผู้ใช้สองคนแก้บิลเดียวกันในเวลาเดียวกัน
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable
        );
        await restaurant.LockCapacity();
        db.ChangeTracker.Clear();
        var refund = await db.RefundRequests.SingleOrDefaultAsync(matchingRefund =>
            matchingRefund.Id == id
        ) ?? throw new InvalidOperationException("ไม่พบคำขอคืนเงิน");
        var user = await Actor(actor, "Customer", "Staff", "Manager", "Admin", "Accountant");
        if (user.Role == "Customer" && (action != "Withdraw" || refund.RequestedBy != actor))
        {
            throw new UnauthorizedAccessException();
        }
        if (user.Role == "Customer")
        {
            bool ownsOrder = await db.Payments.AnyAsync(payment =>
                payment.Id == refund.PaymentId && payment.Order.UserId == actor
            );
            if (!ownsOrder)
            {
                throw new UnauthorizedAccessException();
            }
        }
        if (
            !string.Equals(
                Convert.ToHexString(refund.RowVersion),
                version,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new RefundConflictException();
        }

        string old = refund.Status;
        bool manager = user.Role == "Manager" || user.Role == "Admin";
        switch (action)
        {
            case "Approve":
            case "Reject":
                if (!manager)
                {
                    throw new UnauthorizedAccessException();
                }

                if (refund.Status != "PendingApproval" || refund.RequestedBy == actor)
                {
                    throw new InvalidOperationException(
                        "ต้องเป็นคำขอรออนุมัติ และห้ามตัดสินคำขอของตนเอง"
                    );
                }

                refund.DecidedBy = actor;
                refund.DecidedAtUtc = DateTime.UtcNow;
                if (action == "Approve")
                {
                    refund.Status = "Approved";
                }
                else
                {
                    refund.Status = "Rejected";
                }

                if (action == "Reject")
                {
                    refund.DecisionReason = Required(reason, 500, "เหตุผลที่ไม่อนุมัติ");
                }

                break;
            case "Withdraw":
                if (refund.RequestedBy != actor)
                {
                    throw new UnauthorizedAccessException();
                }

                if (refund.Status != "PendingApproval")
                {
                    throw new InvalidOperationException("ถอนคำขอได้เฉพาะก่อนอนุมัติ");
                }

                refund.Status = "Withdrawn";
                break;
            case "Claim":
                if (user.Role != "Accountant")
                {
                    throw new UnauthorizedAccessException();
                }

                if (refund.Status != "Approved" || refund.DecidedBy == actor)
                {
                    throw new InvalidOperationException(
                        "รับงานได้เมื่ออนุมัติแล้ว และผู้รับงานต้องไม่ใช่ผู้อนุมัติ"
                    );
                }

                refund.ClaimedBy = actor;
                refund.ClaimedAtUtc = DateTime.UtcNow;
                refund.Status = "Paying";
                break;
            case "Revoke":
                if (!manager)
                {
                    throw new UnauthorizedAccessException();
                }

                if (refund.Status != "Approved")
                {
                    throw new InvalidOperationException("เพิกถอนได้ก่อนนักบัญชีรับงานเท่านั้น");
                }

                reason = Required(reason, 500, "เหตุผลเพิกถอน");
                refund.Status = "Revoked";
                refund.DecisionReason = reason;
                break;
            case "Release":
            case "ReassignPaying":
                if (!manager)
                {
                    throw new UnauthorizedAccessException();
                }

                if (refund.Status != "Paying")
                {
                    throw new InvalidOperationException("รายการไม่ได้อยู่ระหว่างคืนเงิน");
                }

                reason = Required(reason, 500, "ผลตรวจสอบหลักฐานการจ่ายกับผู้รับงานเดิม");
                if (action == "Release")
                {
                    refund.Status = "Approved";
                    refund.ClaimedBy = null;
                    refund.ClaimedAtUtc = null;
                }
                else
                {
                    if (assignee == null || assignee == refund.DecidedBy)
                    {
                        throw new InvalidOperationException("เลือกนักบัญชีที่ไม่ใช่ผู้อนุมัติ");
                    }

                    await Actor(assignee.Value, "Accountant");
                    refund.ClaimedBy = assignee;
                    refund.ClaimedAtUtc = DateTime.UtcNow;
                }

                break;
            default:
                throw new InvalidOperationException("ขั้นตอนไม่ถูกต้อง");
        }

        // คำขอที่ยกเลิกแล้วไม่กันบิลไว้ เพื่อให้ส่งคำขอที่แก้ไขใหม่ได้
        if (
            refund.Status == "Rejected"
            || refund.Status == "Withdrawn"
            || refund.Status == "Revoked"
        )
        {
            refund.ActivePaymentId = null;
        }
        else
        {
            refund.ActivePaymentId = refund.PaymentId;
        }

        await notifications.RefundChanged(refund, action);
        restaurant.Audit(
            actor,
            "Refund" + action,
            $"Refund #{id}: {old} -> {refund.Status}; {reason}; assignee {assignee}"
        );
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<bool> Complete(
        int id,
        int actor,
        string version,
        string reference,
        DateTime refundedAt,
        string completionKey,
        bool confirmed,
        string evidence
    )
    {
        // บันทึกทุกขั้นตอนพร้อมกัน และกันผู้ใช้สองคนแก้บิลเดียวกันในเวลาเดียวกัน
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable
        );
        await restaurant.LockCapacity();
        db.ChangeTracker.Clear();
        await Actor(actor, "Accountant");
        var refund = await db
            .RefundRequests.Include(matchingRefund => matchingRefund.Payment)
                .ThenInclude(matchingPayment => matchingPayment.Order)
            .SingleOrDefaultAsync(matchingRefund => matchingRefund.Id == id)
            ?? throw new InvalidOperationException("ไม่พบคำขอคืนเงิน");
        reference = Required(reference, 100, "เลขอ้างอิง/เลขเอกสารรับเงิน");
        completionKey = Required(completionKey, 64, "รหัสยืนยัน");
        if (refundedAt.Year < 2000 || refundedAt.Year > 2100)
        {
            throw new InvalidOperationException("กรอกวันเวลาคืนเงินจริงให้ถูกต้อง");
        }

        var utc = BusinessTime.Utc(refundedAt);
        if (refund.Status == "Completed")
        {
            if (
                refund.CompletedBy != actor
                || refund.CompletionKey != completionKey
                || refund.Reference != reference
                || refund.RefundedAtUtc != utc
            )
            {
                throw new RefundConflictException();
            }

            return false;
        }

        if (
            refund.Status != "Paying"
            || refund.ClaimedBy != actor
            || refund.DecidedBy == actor
            || !confirmed
        )
        {
            throw new InvalidOperationException(
                "ต้องรับงานหลังอนุมัติ และยืนยันว่าคืนเงินจริงแล้ว"
            );
        }

        if (
            !string.Equals(
                Convert.ToHexString(refund.RowVersion),
                version,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new RefundConflictException();
        }

        // HTML datetime-local records seconds, so compare approval at the same precision.
        var approved = refund.DecidedAtUtc!.Value;
        approved = approved.AddTicks(-(approved.Ticks % TimeSpan.TicksPerSecond));
        if (utc < approved || utc > DateTime.UtcNow || refund.Payment.Refunded)
        {
            throw new InvalidOperationException("ตรวจวันเวลาคืนเงินจริงและสถานะการชำระเงิน");
        }

        if (refund.Method == "Transfer" && string.IsNullOrEmpty(evidence))
        {
            throw new InvalidOperationException("แนบหลักฐานการโอนคืนก่อนบันทึก");
        }

        if (
            await db.RefundRequests.AnyAsync(matchingRefund =>
                matchingRefund.Id != id
                && matchingRefund.Status == "Completed"
                && matchingRefund.Method == refund.Method
                && matchingRefund.Reference == reference
            )
        )
        {
            throw new InvalidOperationException(
                "เลขอ้างอิงการคืนเงินนี้ถูกใช้แล้ว กรุณาตรวจสอบก่อนบันทึกซ้ำ"
            );
        }

        refund.Status = "Completed";
        refund.CompletedBy = actor;
        refund.CompletedAtUtc = DateTime.UtcNow;
        refund.RefundedAtUtc = utc;
        refund.Reference = reference;
        refund.CompletionKey = completionKey;
        refund.EvidenceFileName = evidence;
        refund.Payment.Refunded = true;
        refund.Payment.Order.Status = "Cancelled";
        refund.Payment.Order.Closed = true;
        await notifications.RefundChanged(refund, "Complete");
        restaurant.Audit(
            actor,
            "RefundCompleted",
            $"Refund #{id}, Order #{refund.Payment.OrderId}, Amount {refund.Amount}"
        );
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }
}
