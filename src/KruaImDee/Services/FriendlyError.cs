using System.Net;

namespace KruaImDee.Services;

public static class FriendlyError
{
    public static async Task Write(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        if (context.Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            await context.Response.WriteAsJsonAsync(new { message });
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(
            "<!doctype html><html lang='th'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>ตรวจสอบรายการ, เอาอีก</title><link rel='stylesheet' href='/css/design.css'><body><main class='section narrow'><h1>ตรวจสอบรายการ</h1><div class='panel'><p role='alert'>"
                + WebUtility.HtmlEncode(message)
                + "</p><button class='button' onclick='history.back()'>กลับไปแก้ไข</button><a class='back-link' href='/'>กลับหน้าแรก</a></div></main></body></html>"
        );
    }
}
