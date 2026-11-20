namespace KruaImDee.Services;

// ใช้เวลากรุงเทพฯ ในหน้าจอ และแปลงกลับ UTC ก่อนเก็บเวลาคืนเงิน
public static class BusinessTime
{
    public static DateTime Now
    {
        get { return DateTime.UtcNow.AddHours(7); }
    }

    public static DateTime Local(DateTime utc)
    {
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(7);
    }

    public static DateTime Utc(DateTime bangkok)
    {
        return DateTime.SpecifyKind(bangkok.AddHours(-7), DateTimeKind.Utc);
    }
}
