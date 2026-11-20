using System.Security.Claims;
using KruaImDee.Data;
using KruaImDee.Models;
using KruaImDee.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Controllers;

[Authorize]
public class RefundsController : Controller
{
    private readonly AppDb db;
    private readonly RefundService service;
    private readonly IWebHostEnvironment env;

    public RefundsController(AppDb db, RefundService service, IWebHostEnvironment env)
    {
        this.db = db;
        this.service = service;
        this.env = env;
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

    private bool Finance
    {
        get
        {
            return User.IsInRole("Admin")
                || User.IsInRole("Manager")
                || User.IsInRole("Accountant");
        }
    }

    public async Task<IActionResult> Index(string? status, int? orderId, int page = 1)
    {
        Response.Headers.CacheControl = "private, no-store";
        var query = db
            .RefundRequests.AsNoTracking()
            .Include(matchingRefund => matchingRefund.Payment)
                .ThenInclude(matchingPayment => matchingPayment.Order)
            .AsQueryable();
        if (!Team)
        {
            query = query.Where(matchingRefund => matchingRefund.Payment.Order.UserId == Actor);
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(matchingRefund => matchingRefund.Status == status);
        }

        if (orderId.HasValue)
        {
            query = query.Where(matchingRefund => matchingRefund.Payment.OrderId == orderId);
        }

        ViewBag.Status = status;
        return View(
            await Paging.Load(
                this,
                query.OrderByDescending(matchingRefund => matchingRefund.Id),
                page
            )
        );
    }

    public async Task<IActionResult> Details(int id)
    {
        var refund = await db
            .RefundRequests.AsNoTracking()
            .Include(matchingRefund => matchingRefund.Payment)
                .ThenInclude(matchingPayment => matchingPayment.Order)
            .SingleOrDefaultAsync(matchingRefund => matchingRefund.Id == id);
        if (refund == null)
        {
            return NotFound();
        }

        if (!Team && refund.Payment.Order.UserId != Actor)
        {
            return Forbid();
        }

        Response.Headers.CacheControl = "private, no-store";
        ViewBag.Team = Team;
        ViewBag.Finance = Finance;
        ViewBag.Actor = Actor;
        var ids = new int?[]
        {
            refund.RequestedBy,
            refund.DecidedBy,
            refund.ClaimedBy,
            refund.CompletedBy,
        };
        ViewBag.Names = new Dictionary<int, string>();
        ViewBag.Accountants = new List<AppUser>();
        ViewBag.Events = new List<AuditLog>();

        // ลูกค้าเห็นสถานะของตนเอง ส่วนข้อมูลผู้ดำเนินการแสดงตามสิทธิ์
        if (Team)
        {
            ViewBag.Names = await db
                .Users.Where(user => ids.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.Name);
        }

        if (Finance)
        {
            ViewBag.Accountants = await db
                .Users.Where(user => user.Active && user.Role == "Accountant")
                .ToListAsync();
            ViewBag.Events = await db
                .AuditLogs.Where(log =>
                    log.Action.StartsWith("Refund")
                    && (
                        log.Detail.StartsWith("Refund #" + id + ":")
                        || log.Detail.StartsWith("Refund #" + id + ",")
                    )
                )
                .OrderBy(log => log.Id)
                .ToListAsync();
        }
        return View(refund);
    }

    [Authorize(Roles = "Customer,Staff,Manager,Admin,Accountant")]
    public async Task<IActionResult> Create(int orderId)
    {
        var payment = await db
            .Payments.Include(matchingPayment => matchingPayment.Order)
            .SingleOrDefaultAsync(matchingPayment => matchingPayment.OrderId == orderId);
        if (payment == null)
        {
            return NotFound();
        }

        if (!Team && payment.Order.UserId != Actor)
        {
            return Forbid();
        }

        if (payment.Refunded || payment.Order.Status == "Cancelled" || payment.Amount <= 0)
        {
            throw new InvalidOperationException(
                "รายการนี้ไม่เข้าเงื่อนไขขอคืนเงิน หรือคืนเงินแล้ว"
            );
        }

        var existing = await db.RefundRequests.FirstOrDefaultAsync(matchingRefund =>
            matchingRefund.ActivePaymentId == payment.Id
        );
        if (existing != null)
        {
            return RedirectToAction(nameof(Details), new { id = existing.Id });
        }

        return View(payment);
    }

    [HttpPost, ActionName("Request"), Authorize(Roles = "Customer,Staff,Manager,Admin,Accountant")]
    public async Task<IActionResult> SubmitRequest(
        int orderId,
        string reason,
        string method,
        string recipientName,
        string destination,
        string requestKey
    )
    {
        int id = await service.Request(
            orderId,
            Actor,
            reason,
            method,
            recipientName,
            destination,
            requestKey
        );
        TempData["Success"] = "ส่งคำขอแล้ว กรุณารออนุมัติก่อนคืนเงิน";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<IActionResult> Change(
        int id,
        string version,
        string action,
        string reason = "",
        int? assignee = null
    )
    {
        await service.Change(id, Actor, action, version, reason, assignee);
        TempData["Success"] = "บันทึกขั้นตอนคืนเงินแล้ว";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, Authorize(Roles = "Manager,Admin")]
    public Task<IActionResult> Approve(int id, string version)
    {
        return Change(id, version, "Approve");
    }

    [HttpPost, Authorize(Roles = "Manager,Admin")]
    public Task<IActionResult> Reject(int id, string version, string reason)
    {
        return Change(id, version, "Reject", reason);
    }

    [HttpPost]
    public Task<IActionResult> Withdraw(int id, string version)
    {
        return Change(id, version, "Withdraw");
    }

    [HttpPost, Authorize(Roles = "Accountant")]
    public Task<IActionResult> Claim(int id, string version)
    {
        return Change(id, version, "Claim");
    }

    [HttpPost, Authorize(Roles = "Manager,Admin")]
    public Task<IActionResult> Revoke(int id, string version, string reason)
    {
        return Change(id, version, "Revoke", reason);
    }

    [HttpPost, Authorize(Roles = "Manager,Admin")]
    public Task<IActionResult> Release(int id, string version, string reason, bool verifiedUnpaid)
    {
        if (!verifiedUnpaid)
        {
            throw new InvalidOperationException("ต้องตรวจสอบและยืนยันว่ายังไม่ได้จ่ายเงินจริง");
        }

        return Change(id, version, "Release", reason);
    }

    [HttpPost, Authorize(Roles = "Manager,Admin")]
    public Task<IActionResult> ReassignPaying(
        int id,
        string version,
        string reason,
        int assignee,
        bool verified
    )
    {
        if (!verified)
        {
            throw new InvalidOperationException("ต้องตรวจหลักฐานและแจ้งสถานะจ่ายให้ผู้รับมอบก่อน");
        }

        return Change(id, version, "ReassignPaying", reason, assignee);
    }

    [HttpPost, Authorize(Roles = "Accountant"), RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Complete(
        int id,
        string version,
        string reference,
        DateTime refundedAt,
        string completionKey,
        bool confirmed,
        IFormFile? evidence
    )
    {
        string filename = "";
        string folder = Path.Combine(FileStorage.DataFolder(env), "refunds");
        bool stored = false;
        try
        {
            if (evidence != null)
            {
                if (evidence.Length == 0 || evidence.Length > 5 * 1024 * 1024)
                {
                    throw new InvalidOperationException("หลักฐานต้องมีขนาดไม่เกิน 5 MB");
                }

                using var stream = new MemoryStream();
                await evidence.CopyToAsync(stream);
                var bytes = stream.ToArray();
                string extension = SlipImage.Extension(bytes);
                if (extension == "")
                {
                    throw new InvalidOperationException(
                        "รองรับหลักฐานรูป JPEG, PNG และ WebP เท่านั้น"
                    );
                }

                filename = Guid.NewGuid().ToString("N") + extension;
                Directory.CreateDirectory(folder);
                await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, filename), bytes);
            }

            stored = await service.Complete(
                id,
                Actor,
                version,
                reference,
                refundedAt,
                completionKey,
                confirmed,
                filename
            );
        }
        finally
        {
            if (!stored && filename != "")
            {
                System.IO.File.Delete(Path.Combine(folder, filename));
            }
        }

        TempData["Success"] = "บันทึกคืนเงินจริงเรียบร้อยแล้ว";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = "Accountant,Manager,Admin")]
    public async Task<IActionResult> Evidence(int id)
    {
        var refund = await db
            .RefundRequests.AsNoTracking()
            .SingleOrDefaultAsync(matchingRefund => matchingRefund.Id == id);
        if (
            refund == null
            || string.IsNullOrEmpty(refund.EvidenceFileName)
            || Path.GetFileName(refund.EvidenceFileName) != refund.EvidenceFileName
        )
        {
            return NotFound();
        }

        string path = Path.Combine(FileStorage.DataFolder(env), "refunds", refund.EvidenceFileName);
        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, no-store";
        return PhysicalFile(path, SlipImage.ContentType(refund.EvidenceFileName));
    }
}
