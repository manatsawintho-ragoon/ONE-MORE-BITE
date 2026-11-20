using System.ComponentModel.DataAnnotations;

namespace KruaImDee.Models;

public class RefundRequest
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;
    public int? ActivePaymentId { get; set; }
    public decimal Amount { get; set; }

    [MaxLength(24)]
    public string Status { get; set; } = "PendingApproval";

    [MaxLength(500)]
    public string Reason { get; set; } = "";
    public int RequestedBy { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public int? DecidedBy { get; set; }
    public DateTime? DecidedAtUtc { get; set; }

    [MaxLength(500)]
    public string DecisionReason { get; set; } = "";

    [MaxLength(20)]
    public string Method { get; set; } = "Cash";

    [MaxLength(100)]
    public string RecipientName { get; set; } = "";

    [MaxLength(300)]
    public string Destination { get; set; } = "";
    public int? ClaimedBy { get; set; }
    public DateTime? ClaimedAtUtc { get; set; }
    public int? CompletedBy { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? RefundedAtUtc { get; set; }

    [MaxLength(100)]
    public string Reference { get; set; } = "";

    [MaxLength(100)]
    public string EvidenceFileName { get; set; } = "";

    [MaxLength(64)]
    public string RequestKey { get; set; } = "";

    [MaxLength(64)]
    public string CompletionKey { get; set; } = "";

    [Timestamp]
    public byte[] RowVersion { get; set; } = new byte[0];
}

public class Reconciliation
{
    public int Id { get; set; }
    public DateTime BusinessDate { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal ExpectedTransfer { get; set; }
    public decimal CountedCash { get; set; }
    public decimal CountedTransfer { get; set; }

    [MaxLength(500)]
    public string Note { get; set; } = "";
    public int RecordedBy { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

public class FinancePage
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal Received { get; set; }
    public decimal Refunded { get; set; }

    public decimal Net
    {
        get { return Received - Refunded; }
    }

    public decimal CashReceived { get; set; }
    public decimal CashRefunded { get; set; }
    public decimal TransferReceived { get; set; }
    public decimal TransferRefunded { get; set; }
    public decimal PendingAmount { get; set; }
    public decimal ApprovedAmount { get; set; }
    public int LegacyRefunds { get; set; }
    public List<Payment> Payments { get; set; } = new List<Payment>();
    public List<RefundRequest> Refunds { get; set; } = new List<RefundRequest>();
    public List<Reconciliation> Reconciliations { get; set; } = new List<Reconciliation>();
    public List<FinanceEntry> Entries { get; set; } = new List<FinanceEntry>();
}

public class FinanceEntry
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Kind { get; set; } = "";
    public string Method { get; set; } = "";
    public decimal Amount { get; set; }
}
