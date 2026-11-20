namespace KruaImDee.Models;

public class PosBill
{
    public int OperatorId { get; set; }
    public string RequestKey { get; set; } = Guid.NewGuid().ToString("N");
    public List<CartLine> Lines { get; set; } = new List<CartLine>();
    public int? TableId { get; set; }
    public string Promo { get; set; } = "";
    public string Note { get; set; } = "";
    public string PaymentMethod { get; set; } = "Cash";
}

public class PosState
{
    public string RequestKey { get; set; } = "";
    public List<PosLine> Lines { get; set; } = new List<PosLine>();
    public int? TableId { get; set; }
    public string TableName { get; set; } = "";
    public string Promo { get; set; } = "";
    public string Note { get; set; } = "";
    public string PaymentMethod { get; set; } = "Cash";
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
    public int? OrderId { get; set; }
    public string Status { get; set; } = "Draft";
    public bool Paid { get; set; }
    public decimal Received { get; set; }
    public decimal Change { get; set; }
    public string ReceiptUrl { get; set; } = "";
    public string Message { get; set; } = "";
}

public class PosLine
{
    public int Index { get; set; }
    public int MenuItemId { get; set; }
    public string Name { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public string Note { get; set; } = "";
    public decimal LineTotal { get; set; }
}
