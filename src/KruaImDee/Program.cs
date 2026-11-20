using System.Security.Claims;
using KruaImDee.Data;
using KruaImDee.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

// ตั้งค่าบริการที่ Controller และ Service จะขอใช้ผ่าน constructor
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(options =>
{
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add(new MutationValidationFilter());
});
string connectionString = builder.Configuration.GetConnectionString("Restaurant")
    ?? "Server=.\\SQLEXPRESS;Database=KruaImDee;Trusted_Connection=True;TrustServerCertificate=True";
if (builder.Configuration["DatabaseProvider"] == "Sqlite")
{
    string dataFolder = FileStorage.DataFolder(builder.Environment);
    Directory.CreateDirectory(dataFolder);
    builder
        .Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataFolder, "keys")));
    var sqliteSettings = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(dataFolder, "restaurant.db"),
        ForeignKeys = true,
        DefaultTimeout = 30,
    };
    string sqliteConnection = builder.Configuration.GetConnectionString("Restaurant")
        ?? sqliteSettings.ToString();
    builder.Services.AddDbContext<AppDb>(options => options.UseSqlite(sqliteConnection));
}
else
{
    builder.Services.AddDbContext<AppDb>(options => options.UseSqlServer(connectionString));
}
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromHours(8);
});
builder
    .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnValidatePrincipal = async context =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDb>();
            var principal = context.Principal;
            if (principal == null)
            {
                context.RejectPrincipal();
                return;
            }

            int userId;
            if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId))
            {
                context.RejectPrincipal();
                return;
            }

            var user = await db.Users.FindAsync(userId);
            if (user == null || !user.Active)
            {
                context.RejectPrincipal();
                return;
            }

            string passwordVersion = SessionSecurity.Fingerprint(user.PasswordHash);
            if (
                user.Role != principal.FindFirstValue(ClaimTypes.Role)
                || passwordVersion != principal.FindFirstValue(SessionSecurity.Claim)
            )
            {
                context.RejectPrincipal();
            }
        };
    });
builder.Services.AddScoped<RestaurantService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<RefundService>();
builder.Services.AddScoped<FinanceService>();
builder.Services.AddScoped<NotificationService>();
var app = builder.Build();
if (args.Contains("--export-schema"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    Console.WriteLine(db.Database.GenerateCreateScript());
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStatusCodePages(async statusContext =>
{
    var context = statusContext.HttpContext;
    if (context.Response.StatusCode == 404)
    {
        await FriendlyError.Write(
            context,
            404,
            "ไม่พบหน้าที่คุณต้องการ กรุณาตรวจสอบลิงก์หรือกลับไปเลือกเมนู"
        );
    }
});

// ลำดับนี้สำคัญ ต้องอ่านเซสชันและยืนยันตัวตนก่อนตรวจสิทธิ์
app.UseStaticFiles();
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ONE_MORE_BITE_DATA")))
{
    string uploadFolder = FileStorage.UploadFolder(app.Environment);
    Directory.CreateDirectory(uploadFolder);
    app.UseStaticFiles(
        new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(uploadFolder),
            RequestPath = "/uploads",
        }
    );
}
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.Use(
    async (context, next) =>
    {
        try
        {
            await next();
        }
        catch (UnauthorizedAccessException)
        {
            await FriendlyError.Write(context, 403, "ไม่มีสิทธิ์ทำรายการนี้");
        }
        catch (RefundConflictException error)
        {
            await FriendlyError.Write(context, 409, error.Message);
        }
        catch (InvalidOperationException error)
        {
            await FriendlyError.Write(context, 400, error.Message);
        }
        catch (DbUpdateException)
        {
            await FriendlyError.Write(
                context,
                409,
                "ข้อมูลซ้ำหรือรายการมีการเปลี่ยนแปลง กรุณาตรวจสอบแล้วลองใหม่"
            );
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            await FriendlyError.Write(context, 409, "ระบบกำลังทำรายการอื่น กรุณาลองใหม่อีกครั้ง");
        }
        catch (Microsoft.Data.Sqlite.SqliteException error)
            when (error.SqliteErrorCode == 5 || error.SqliteErrorCode == 6)
        {
            await FriendlyError.Write(context, 409, "ระบบกำลังทำรายการอื่น กรุณาลองใหม่อีกครั้ง");
        }
    }
);
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapGet("/health/ready", () =>
    new
    {
        application = "ONE MORE BITE",
        database = app.Configuration["DatabaseProvider"] ?? "SqlServer",
    }
);
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    await Seed.Run(db);
}

if (args.Contains("--seed-only"))
{
    return;
}

app.Run();

public partial class Program { }
