using System.Data;
using System.Security.Claims;
using System.Text;
using KruaImDee.Data;
using KruaImDee.Models;
using KruaImDee.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Controllers;

[Authorize(Roles = "Accountant,Manager,Admin")]
public class FinanceController : Controller
{
    private readonly AppDb db;
    private readonly FinanceService finance;
    private readonly RestaurantService restaurant;

    public FinanceController(AppDb db, FinanceService finance, RestaurantService restaurant)
    {
        this.db = db;
        this.finance = finance;
        this.restaurant = restaurant;
    }

    private int Actor
    {
        get { return int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!); }
    }

    public async Task<IActionResult> Index(DateTime? from, DateTime? to, int page = 1)
    {
        Response.Headers.CacheControl = "private, no-store";
        var (start, end) = FinanceService.Range(from, to);
        var model = await finance.Summary(start, end);
        model.Entries = await Paging.Load(
            this,
            finance
                .Entries(start, end)
                .OrderByDescending(matchingEntry => matchingEntry.OccurredAt)
                .ThenBy(matchingEntry => matchingEntry.Kind)
                .ThenByDescending(matchingEntry => matchingEntry.Id),
            page
        );
        return View(model);
    }

    public async Task<IActionResult> Export(DateTime? from, DateTime? to)
    {
        var (start, end) = FinanceService.Range(from, to);
        var entries = await finance
            .Entries(start, end)
            .OrderBy(matchingEntry => matchingEntry.OccurredAt)
            .ThenBy(matchingEntry => matchingEntry.Id)
            .ToListAsync();
        var text = new StringBuilder("วันที่,ประเภท,ออเดอร์,ช่องทาง,จำนวนเงิน\r\n");
        foreach (FinanceEntry entry in entries)
        {
            string kind = "คืนเงิน";
            if (entry.Kind == "Receipt")
            {
                kind = "รับชำระ";
            }
            string method = "โอนเงิน";
            if (entry.Method == "Cash")
            {
                method = "เงินสด";
            }
            string amount = entry.Amount.ToString(
                "0.00",
                System.Globalization.CultureInfo.InvariantCulture
            );
            text.AppendLine(
                $"{entry.OccurredAt:yyyy-MM-dd HH:mm:ss},{kind},{entry.OrderId},{method},{amount}"
            );
        }

        // All exported fields are typed values or fixed labels, never user-supplied formulas.
        restaurant.Audit(
            Actor,
            "FinanceExport",
            $"{start:yyyy-MM-dd} to {end.AddDays(-1):yyyy-MM-dd}"
        );
        await db.SaveChangesAsync();
        Response.Headers.CacheControl = "private, no-store";
        return File(
            Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray(),
            "text/csv; charset=utf-8",
            "finance.csv"
        );
    }

    public async Task<IActionResult> Reconcile(int page = 1)
    {
        Response.Headers.CacheControl = "private, no-store";
        return View(
            await Paging.Load(
                this,
                db.Reconciliations.AsNoTracking()
                    .OrderByDescending(matchingReconciliation => matchingReconciliation.Id),
                page
            )
        );
    }

    [HttpPost, Authorize(Roles = "Accountant")]
    public async Task<IActionResult> Reconcile(
        DateTime businessDate,
        decimal countedCash,
        decimal countedTransfer,
        string note
    )
    {
        var (start, end) = FinanceService.Range(businessDate, businessDate);
        bool invalidDate = start > BusinessTime.Now.Date;
        bool invalidCash =
            countedCash > 1000000000
            || countedCash < -1000000000
            || decimal.Round(countedCash, 2) != countedCash;
        bool invalidTransfer =
            countedTransfer > 1000000000
            || countedTransfer < -1000000000
            || decimal.Round(countedTransfer, 2) != countedTransfer;
        bool invalidNote = (note ?? "").Length > 500;
        if (invalidDate || invalidCash || invalidTransfer || invalidNote)
        {
            throw new InvalidOperationException("ตรวจวันที่ ยอดเงินจริง และหมายเหตุ");
        }

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await restaurant.LockCapacity();
        var summary = await finance.Summary(start, end);
        var cash = summary.CashReceived - summary.CashRefunded;
        var transfer = summary.TransferReceived - summary.TransferRefunded;
        if ((cash != countedCash || transfer != countedTransfer) && string.IsNullOrWhiteSpace(note))
        {
            throw new InvalidOperationException("กรอกเหตุผลเมื่อยอดต่างจากระบบ");
        }

        db.Reconciliations.Add(
            new Reconciliation
            {
                BusinessDate = start,
                ExpectedCash = cash,
                ExpectedTransfer = transfer,
                CountedCash = countedCash,
                CountedTransfer = countedTransfer,
                Note = note?.Trim() ?? "",
                RecordedBy = Actor,
                RecordedAtUtc = DateTime.UtcNow,
            }
        );
        restaurant.Audit(Actor, "Reconciliation", $"Business date {start:yyyy-MM-dd}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Success"] = "บันทึกผลกระทบยอดแล้ว ยอดธุรกรรมเดิมไม่ถูกแก้ไข";
        return RedirectToAction(nameof(Reconcile));
    }
}
