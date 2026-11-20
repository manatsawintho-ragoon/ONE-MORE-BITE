using System.Data;
using System.Security.Claims;
using System.Text.Json;
using KruaImDee.Data;
using KruaImDee.Models;
using KruaImDee.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Controllers;

[Authorize]
// Save a fixed copy of invoice details after payment.
public class InvoicesController : Controller
{
    private readonly AppDb db;
    private readonly RestaurantService service;

    public InvoicesController(AppDb db, RestaurantService service)
    {
        this.db = db;
        this.service = service;
    }

    private int Actor
    {
        get { return int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!); }
    }

    private bool Team
    {
        get
        {
            return User.IsInRole("Admin")
                || User.IsInRole("Manager")
                || User.IsInRole("Staff")
                || User.IsInRole("Accountant");
        }
    }

    [HttpGet]
    public async Task<IActionResult> Create(int id)
    {
        if (User.IsInRole("Accountant"))
        {
            if (await db.TaxInvoices.AnyAsync(matchingInvoice => matchingInvoice.OrderId == id))
            {
                return RedirectToAction(nameof(Details), new { id });
            }
            else
            {
                return Forbid();
            }
        }

        var order = await db
            .Orders.Include(matchingOrder => matchingOrder.Payment)
            .SingleOrDefaultAsync(matchingOrder => matchingOrder.Id == id);
        if (order == null)
        {
            return NotFound();
        }

        if (!Team && order.UserId != Actor)
        {
            return Forbid();
        }

        if (await db.TaxInvoices.AnyAsync(matchingInvoice => matchingInvoice.OrderId == id))
        {
            return RedirectToAction(nameof(Details), new { id });
        }

        if (order.Payment == null || order.Payment.Refunded || order.Status == "Cancelled")
        {
            throw new InvalidOperationException(
                "ออกใบกำกับภาษีได้หลังชำระเงิน และออเดอร์ยังไม่ถูกยกเลิก"
            );
        }

        return View(new TaxInvoiceInput { OrderId = id });
    }

    [HttpPost]
    public async Task<IActionResult> Issue(TaxInvoiceInput input)
    {
        if (User.IsInRole("Accountant"))
        {
            return Forbid();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable
        );
        await service.LockCapacity();
        var order = await db
            .Orders.Include(matchingOrder => matchingOrder.Payment)
            .Include(matchingOrder => matchingOrder.Items)
            .SingleOrDefaultAsync(matchingOrder => matchingOrder.Id == input.OrderId);
        if (order == null)
        {
            return NotFound();
        }

        if (!Team && order.UserId != Actor)
        {
            return Forbid();
        }

        if (await db.TaxInvoices.AnyAsync(matchingInvoice => matchingInvoice.OrderId == order.Id))
        {
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }

        if (order.Payment == null || order.Payment.Refunded || order.Status == "Cancelled")
        {
            throw new InvalidOperationException(
                "ไม่สามารถออกใบกำกับภาษีให้ออเดอร์ที่ยังไม่ชำระหรือยกเลิกแล้ว"
            );
        }

        var seller = await db.Settings.FirstAsync();
        if (
            string.IsNullOrWhiteSpace(seller.TaxLegalName)
            || string.IsNullOrWhiteSpace(seller.TaxAddress)
            || !System.Text.RegularExpressions.Regex.IsMatch(seller.TaxId, "^[0-9]{13}$")
            || !System.Text.RegularExpressions.Regex.IsMatch(seller.TaxBranch, "^[0-9]{5}$")
        )
        {
            throw new InvalidOperationException(
                "กรุณาตั้งค่าข้อมูลผู้ประกอบการให้ครบก่อนออกใบกำกับภาษี"
            );
        }

        var lines = order
            .Items.OrderBy(matchingOrderItem => matchingOrderItem.Id)
            .Select(matchingOrderItem => new TaxInvoiceLine
            {
                Name = matchingOrderItem.Name,
                Quantity = matchingOrderItem.Quantity,
                UnitPrice = matchingOrderItem.UnitPrice,
            })
            .ToList();
        var invoice = new TaxInvoice
        {
            OrderId = order.Id,
            IssuedBy = Actor,
            SellerName = seller.TaxLegalName,
            SellerAddress = seller.TaxAddress,
            SellerTaxId = seller.TaxId,
            SellerBranch = seller.TaxBranch,
            BuyerName = input.BuyerName.Trim(),
            BuyerAddress = input.BuyerAddress.Trim(),
            BuyerTaxId = input.BuyerTaxId,
            BuyerBranch = input.BuyerBranch ?? "",
            LinesJson = JsonSerializer.Serialize(lines),
            Subtotal = order.Subtotal,
            Discount = order.Discount,
            VatRate = order.VatRate,
            VatAmount = order.VatAmount,
            Total = order.Total,
        };
        db.TaxInvoices.Add(invoice);
        await db.SaveChangesAsync();
        invoice.Number =
            "TI-" + invoice.IssuedAt.ToString("yyyy") + "-" + invoice.Id.ToString("D6");
        service.Audit(Actor, "IssueTaxInvoice", invoice.Number + ", Order #" + order.Id);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var invoice = await db
            .TaxInvoices.AsNoTracking()
            .Include(matchingInvoice => matchingInvoice.Order)
                .ThenInclude(matchingOrder => matchingOrder.Payment)
            .SingleOrDefaultAsync(matchingInvoice => matchingInvoice.OrderId == id);
        if (invoice == null)
        {
            return NotFound();
        }

        if (!Team && invoice.Order.UserId != Actor)
        {
            return Forbid();
        }

        Response.Headers.CacheControl = "private, no-store";
        return View(invoice);
    }
}
