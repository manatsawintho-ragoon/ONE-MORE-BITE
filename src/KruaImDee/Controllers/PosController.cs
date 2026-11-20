using System.Security.Claims;
using System.Text.Json;
using KruaImDee.Data;
using KruaImDee.Models;
using KruaImDee.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Controllers;

[Authorize(Roles = "Admin,Manager,Staff")]
[Route("Staff/[action]")]
// Read, edit and settle the current cashier bill.
public class PosController : Controller
{
    // คำขอแก้บิลต้องทำทีละรายการ และปล่อยล็อกใน finally เสมอ
    private static readonly SemaphoreSlim terminalGate = new SemaphoreSlim(1, 1);
    private readonly AppDb db;
    private readonly RestaurantService service;

    public PosController(AppDb db, RestaurantService service)
    {
        this.db = db;
        this.service = service;
    }

    private int Actor
    {
        get { return int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!); }
    }

    public override async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next
    )
    {
        // Commit terminal state before another POS request reads it.
        await terminalGate.WaitAsync(HttpContext.RequestAborted);
        try
        {
            await HttpContext.Session.LoadAsync();
            await next();
            await HttpContext.Session.CommitAsync();
        }
        finally
        {
            terminalGate.Release();
        }
    }

    private PosBill Read()
    {
        string json = HttpContext.Session.GetString("PosBill") ?? "null";
        var bill = JsonSerializer.Deserialize<PosBill>(json);
        if (bill == null || bill.OperatorId != Actor)
        {
            bill = new PosBill { OperatorId = Actor };
            Save(bill);
        }

        return bill;
    }

    private void Save(PosBill bill)
    {
        HttpContext.Session.SetString("PosBill", JsonSerializer.Serialize(bill));
    }

    private async Task<Order?> FindOrder(PosBill bill)
    {
        return await db
            .Orders.Include(matchingOrder => matchingOrder.Items)
            .Include(matchingOrder => matchingOrder.Payment)
            .Include(matchingOrder => matchingOrder.DiningTable)
            .SingleOrDefaultAsync(matchingOrder =>
                matchingOrder.PosRequestKey == bill.RequestKey
                && matchingOrder.PosOperatorId == Actor
            );
    }

    private async Task CheckEditable(PosBill bill)
    {
        if (await FindOrder(bill) != null)
        {
            throw new InvalidOperationException(
                "กรุณายกเลิกบิลรอชำระก่อนแก้ไข หรือเริ่มบิลใหม่เมื่อเสร็จแล้ว"
            );
        }
    }

    private void CheckKey(PosBill bill, string requestKey)
    {
        if (bill.RequestKey != requestKey)
        {
            throw new InvalidOperationException("บิลเปลี่ยนแล้ว กรุณาโหลดรายการล่าสุด");
        }
    }

    private async Task<PosState> State(PosBill bill, bool customerDisplay = false)
    {
        var state = new PosState
        {
            RequestKey = customerDisplay ? "" : bill.RequestKey,
            TableId = bill.TableId,
            Promo = customerDisplay ? "" : bill.Promo,
            Note = customerDisplay ? "" : bill.Note,
            PaymentMethod = bill.PaymentMethod,
        };
        var order = await FindOrder(bill);
        if (order != null)
        {
            state.OrderId = order.Id;
            state.Status = order.Status;
            state.Subtotal = order.Subtotal;
            state.Discount = order.Discount;
            state.VatAmount = order.VatAmount;
            state.Total = order.Total;
            state.TableName = order.DiningTable?.Name ?? "กลับบ้าน";
            state.Paid = order.Payment != null && !order.Payment.Refunded;
            state.Received = order.Payment?.Received ?? 0;
            state.Change = order.Payment?.Change ?? 0;
            if (state.Paid)
            {
                state.PaymentMethod = order.Payment!.Method;
                state.ReceiptUrl = customerDisplay ? "" : "/Orders/Receipt/" + order.Id;
            }

            foreach (var item in order.Items.OrderBy(matchingOrderItem => matchingOrderItem.Id))
            {
                state.Lines.Add(
                    new PosLine
                    {
                        Index = state.Lines.Count,
                        MenuItemId = item.MenuItemId,
                        Name = item.Name,
                        UnitPrice = item.UnitPrice,
                        Quantity = item.Quantity,
                        Note = customerDisplay ? "" : item.Note,
                        LineTotal = item.UnitPrice * item.Quantity,
                    }
                );
            }

            return state;
        }

        state.TableName = "กลับบ้าน";
        if (bill.TableId != null)
        {
            state.TableName = await db
                .DiningTables.Where(table => table.Id == bill.TableId)
                .Select(table => table.Name)
                .FirstOrDefaultAsync()
                ?? "";
        }
        foreach (var line in bill.Lines)
        {
            var item = await db.MenuItems.FindAsync(line.MenuItemId);
            if (item == null)
            {
                continue;
            }

            state.Lines.Add(
                new PosLine
                {
                    Index = state.Lines.Count,
                    MenuItemId = item.Id,
                    Name = item.Name,
                    ImageUrl = item.ImageUrl,
                    UnitPrice = item.EffectivePrice,
                    Quantity = line.Quantity,
                    Note = customerDisplay ? "" : line.Note,
                    LineTotal = item.EffectivePrice * line.Quantity,
                }
            );
        }

        if (bill.Lines.Count > 0)
        {
            try
            {
                var quote = await service.Quote(bill.Lines, bill.Promo);
                state.Subtotal = quote.Subtotal;
                state.Discount = quote.Discount;
                state.VatAmount = quote.VatAmount;
                state.Total = quote.Total;
            }
            catch (InvalidOperationException ex)
            {
                state.Message = ex.Message;
            }
        }

        return state;
    }

    [HttpGet]
    public async Task<IActionResult> PosState()
    {
        return Json(await State(Read()));
    }

    [HttpGet]
    public async Task<IActionResult> DisplayState()
    {
        Response.Headers.CacheControl = "no-store";
        return Json(await State(Read(), true));
    }

    [HttpGet]
    public IActionResult CustomerDisplay()
    {
        return View("~/Views/Staff/CustomerDisplay.cshtml");
    }

    [HttpPost]
    public async Task<IActionResult> PosAdd(int menuItemId, int quantity = 1, string note = "")
    {
        var bill = Read();
        await CheckEditable(bill);
        Rules.Quantity(quantity);
        var item = await db
            .MenuItems.Include(matchingMenuItem => matchingMenuItem.Category)
            .SingleOrDefaultAsync(matchingMenuItem => matchingMenuItem.Id == menuItemId);
        if (item == null || !item.Active || !item.Category.Active || item.SoldOut)
        {
            throw new InvalidOperationException("อาหารไม่พร้อมจำหน่าย");
        }

        note = (note ?? "").Trim();
        if (note.Length > 500)
        {
            throw new InvalidOperationException("หมายเหตุต้องไม่เกิน 500 ตัวอักษร");
        }

        var line = bill.Lines.FirstOrDefault(x => x.MenuItemId == menuItemId && x.Note == note);
        if (line == null)
        {
            if (bill.Lines.Count >= 50)
            {
                throw new InvalidOperationException("รับได้ไม่เกิน 50 รายการต่อบิล");
            }

            bill.Lines.Add(new CartLine(menuItemId, quantity, note));
        }
        else
        {
            Rules.Quantity(line.Quantity + quantity);
            line.Quantity += quantity;
        }

        Save(bill);
        return Json(await State(bill));
    }

    [HttpPost]
    public async Task<IActionResult> PosUpdate(int index, int quantity, string note = "")
    {
        var bill = Read();
        await CheckEditable(bill);
        Rules.Quantity(quantity);
        if (index < 0 || index >= bill.Lines.Count)
        {
            return NotFound();
        }

        note = (note ?? "").Trim();
        if (note.Length > 500)
        {
            throw new InvalidOperationException("หมายเหตุต้องไม่เกิน 500 ตัวอักษร");
        }

        bill.Lines[index].Quantity = quantity;
        bill.Lines[index].Note = note;
        Save(bill);
        return Json(await State(bill));
    }

    [HttpPost]
    public async Task<IActionResult> PosRemove(int index)
    {
        var bill = Read();
        await CheckEditable(bill);
        if (index < 0 || index >= bill.Lines.Count)
        {
            return NotFound();
        }

        bill.Lines.RemoveAt(index);
        Save(bill);
        return Json(await State(bill));
    }

    [HttpPost]
    public async Task<IActionResult> PosOptions(int? tableId, string promo = "", string note = "")
    {
        var bill = Read();
        await CheckEditable(bill);
        if ((note ?? "").Length > 500 || (promo ?? "").Length > 30)
        {
            throw new InvalidOperationException("หมายเหตุหรือรหัสส่วนลดยาวเกินกำหนด");
        }

        if (
            tableId != null
            && !await db.DiningTables.AnyAsync(matchingTable =>
                matchingTable.Id == tableId && matchingTable.Active
            )
        )
        {
            throw new InvalidOperationException("ไม่พบโต๊ะที่เปิดใช้งาน");
        }

        if (bill.Lines.Count > 0)
        {
            await service.Quote(bill.Lines, promo ?? "");
        }

        bill.TableId = tableId;
        bill.Promo = (promo ?? "").Trim();
        bill.Note = (note ?? "").Trim();
        Save(bill);
        return Json(await State(bill));
    }

    [HttpPost]
    public async Task<IActionResult> PosPrepare(string requestKey, string method = "Cash")
    {
        var bill = Read();
        CheckKey(bill, requestKey);
        if (method != "Cash" && method != "Transfer")
        {
            throw new InvalidOperationException("วิธีชำระเงินไม่ถูกต้อง");
        }

        await service.Place(
            Actor,
            bill.Lines,
            bill.TableId,
            bill.Promo,
            bill.Note,
            bill.RequestKey
        );
        bill.PaymentMethod = method;
        Save(bill);
        return Json(await State(bill));
    }

    [HttpPost]
    public async Task<IActionResult> PosCheckout(
        string requestKey,
        string method = "Cash",
        decimal received = 0,
        bool transferConfirmed = false,
        bool payLater = false
    )
    {
        var bill = Read();
        CheckKey(bill, requestKey);
        if (payLater && bill.TableId == null)
        {
            throw new InvalidOperationException("ส่งครัวก่อนชำระเงินได้เฉพาะรายการนั่งทานที่ร้าน");
        }

        if (!payLater && method != "Cash" && method != "Transfer")
        {
            throw new InvalidOperationException("วิธีชำระเงินไม่ถูกต้อง");
        }

        var order = await service.Place(
            Actor,
            bill.Lines,
            bill.TableId,
            bill.Promo,
            bill.Note,
            bill.RequestKey
        );
        if (order.Status == "Cancelled")
        {
            throw new InvalidOperationException("บิลนี้ถูกยกเลิกแล้ว");
        }

        if (payLater)
        {
            if (order.Status == "AwaitingPayment")
            {
                await service.ChangePendingPos(bill.RequestKey, Actor, true);
            }
        }
        else
        {
            await service.Settle(order.Id, Actor, method, received, transferConfirmed);
        }

        return Json(await State(bill));
    }

    [HttpPost]
    public async Task<IActionResult> PosCancel(string requestKey)
    {
        var bill = Read();
        CheckKey(bill, requestKey);
        await service.ChangePendingPos(bill.RequestKey, Actor, false);
        bill.RequestKey = Guid.NewGuid().ToString("N");
        Save(bill);
        return Json(await State(bill));
    }

    [HttpPost]
    public async Task<IActionResult> PosNew()
    {
        var bill = Read();
        var order = await FindOrder(bill);
        if (order != null && order.Status == "AwaitingPayment")
        {
            throw new InvalidOperationException("กรุณาชำระเงินหรือยกเลิกบิลรอชำระก่อน");
        }

        bill = new PosBill { OperatorId = Actor };
        Save(bill);
        return Json(await State(bill));
    }
}
