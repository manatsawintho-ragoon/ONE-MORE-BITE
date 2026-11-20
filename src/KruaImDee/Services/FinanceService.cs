using KruaImDee.Data;
using KruaImDee.Models;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Services;

public class FinanceService
{
    private readonly AppDb db;

    public FinanceService(AppDb db)
    {
        this.db = db;
    }

    public static (DateTime start, DateTime end) Range(DateTime? from, DateTime? to)
    {
        var start = (from ?? BusinessTime.Now).Date;
        var last = (to ?? BusinessTime.Now).Date;
        if (start.Year < 2000 || last.Year > 2100 || last < start || (last - start).TotalDays > 366)
        {
            throw new InvalidOperationException("เลือกช่วงวันที่ตามลำดับ ไม่เกิน 367 วัน");
        }

        return (start, last.AddDays(1));
    }

    public IQueryable<FinanceEntry> Entries(DateTime start, DateTime end)
    {
        var utcStart = BusinessTime.Utc(start);
        var utcEnd = BusinessTime.Utc(end);
        // เตรียมคำค้นเงินรับและเงินคืนแยกกัน แล้วให้ฐานข้อมูลรวมและแบ่งหน้า
        var receipts = db
            .Payments.Where(payment => payment.PaidAt >= start && payment.PaidAt < end)
            .Select(payment => new FinanceEntry
            {
                Id = payment.Id,
                OrderId = payment.OrderId,
                OccurredAt = payment.PaidAt,
                Kind = "Receipt",
                Method = payment.Method,
                Amount = payment.Amount,
            });

        var refunds = db
            .RefundRequests.Where(refund =>
                refund.Status == "Completed"
                && refund.RefundedAtUtc >= utcStart
                && refund.RefundedAtUtc < utcEnd
            )
            .Select(refund => new FinanceEntry
            {
                Id = refund.Id,
                OrderId = refund.Payment.OrderId,
                OccurredAt = refund.RefundedAtUtc!.Value.AddHours(7),
                Kind = "Refund",
                Method = refund.Method,
                Amount = -refund.Amount,
            });

        return receipts.Concat(refunds);
    }

    public async Task<FinancePage> Summary(DateTime start, DateTime end)
    {
        var rows = await Entries(start, end)
            .GroupBy(matchingEntry => new { matchingEntry.Kind, matchingEntry.Method })
            .Select(g => new
            {
                g.Key.Kind,
                g.Key.Method,
                Amount = g.Sum(matchingEntry => matchingEntry.Amount),
            })
            .ToListAsync();
        var model = new FinancePage { From = start, To = end.AddDays(-1) };
        // ฐานข้อมูลรวมยอดเป็นกลุ่มแล้ว จึงวนเฉพาะยอดรวม ไม่โหลดธุรกรรมทุกแถว
        foreach (var row in rows)
        {
            if (row.Kind == "Receipt")
            {
                model.Received += row.Amount;
                if (row.Method == "Cash")
                {
                    model.CashReceived += row.Amount;
                }
                else if (row.Method == "Transfer")
                {
                    model.TransferReceived += row.Amount;
                }
            }
            else if (row.Kind == "Refund")
            {
                // รายการคืนเก็บเป็นค่าลบ แต่ช่องยอดคืนแสดงจำนวนเงินเป็นค่าบวก
                model.Refunded -= row.Amount;
                if (row.Method == "Cash")
                {
                    model.CashRefunded -= row.Amount;
                }
                else if (row.Method == "Transfer")
                {
                    model.TransferRefunded -= row.Amount;
                }
            }
        }
        model.PendingAmount = await db
            .RefundRequests.Where(matchingRefund => matchingRefund.Status == "PendingApproval")
            .SumAsync(matchingRefund => matchingRefund.Amount);
        model.ApprovedAmount = await db
            .RefundRequests.Where(matchingRefund =>
                matchingRefund.Status == "Approved" || matchingRefund.Status == "Paying"
            )
            .SumAsync(matchingRefund => matchingRefund.Amount);
        model.LegacyRefunds = await db.Payments.CountAsync(matchingPayment =>
            matchingPayment.Refunded
            && !db.RefundRequests.Any(matchingRefund =>
                matchingRefund.PaymentId == matchingPayment.Id
                && matchingRefund.Status == "Completed"
            )
        );
        return model;
    }
}
