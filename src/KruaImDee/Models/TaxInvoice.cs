using System.ComponentModel.DataAnnotations;

namespace KruaImDee.Models;

public class TaxInvoice
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    [MaxLength(30)]
    public string Number { get; set; } = "";
    public DateTime IssuedAt { get; set; } = DateTime.Now;
    public int IssuedBy { get; set; }
    public string SellerName { get; set; } = "";
    public string SellerAddress { get; set; } = "";
    public string SellerTaxId { get; set; } = "";
    public string SellerBranch { get; set; } = "";
    public string BuyerName { get; set; } = "";
    public string BuyerAddress { get; set; } = "";
    public string BuyerTaxId { get; set; } = "";
    public string BuyerBranch { get; set; } = "";
    public string LinesJson { get; set; } = "[]";
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
}

public class TaxInvoiceInput
{
    public int OrderId { get; set; }

    [Required, StringLength(200)]
    public string BuyerName { get; set; } = "";

    [Required, StringLength(500)]
    public string BuyerAddress { get; set; } = "";

    [Required, RegularExpression("^[0-9]{13}$")]
    public string BuyerTaxId { get; set; } = "";

    [RegularExpression("^[0-9]{5}$")]
    public string? BuyerBranch { get; set; }
}

public class TaxInvoiceLine
{
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
