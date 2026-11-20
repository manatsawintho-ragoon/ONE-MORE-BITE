namespace KruaImDee.Services;

public static class Rules
{
    public static decimal Vat(decimal taxableAmount, decimal rate)
    {
        return Math.Round(
            Math.Max(0, taxableAmount) * rate / 100,
            2,
            MidpointRounding.AwayFromZero
        );
    }

    public static void Quantity(int value)
    {
        if (value < 1 || value > 99)
        {
            throw new InvalidOperationException("จำนวนต้องอยู่ระหว่าง 1 ถึง 99");
        }
    }

    public static decimal Discount(decimal subtotal, decimal value, bool percentage)
    {
        decimal discount = value;
        if (percentage)
        {
            discount = subtotal * value / 100;
        }

        discount = Math.Round(discount, 2, MidpointRounding.AwayFromZero);
        if (discount < 0)
        {
            return 0;
        }

        if (discount > subtotal)
        {
            return subtotal;
        }

        return discount;
    }

    public static bool CanAdvance(string from, string to)
    {
        if (from == "Received" && to == "Preparing")
        {
            return true;
        }

        if (from == "Preparing" && to == "Ready")
        {
            return true;
        }

        if (from == "Ready" && to == "Served")
        {
            return true;
        }

        return false;
    }

    public static string Label(string status)
    {
        switch (status)
        {
            case "AwaitingPayment":
                return "รอชำระเงิน";
            case "Received":
                return "รับออเดอร์แล้ว";
            case "Preparing":
                return "กำลังปรุง";
            case "Ready":
                return "พร้อมเสิร์ฟ";
            case "Served":
                return "เสิร์ฟแล้ว";
            case "Cancelled":
                return "ยกเลิก";
            case "Pending":
                return "รอยืนยัน";
            case "Confirmed":
                return "ยืนยันแล้ว";
            case "Seated":
                return "เข้ารับบริการ";
            case "Completed":
                return "เสร็จสิ้น";
            default:
                return status;
        }
    }
}
