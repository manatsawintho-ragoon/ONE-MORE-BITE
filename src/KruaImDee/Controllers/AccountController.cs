using System.Security.Claims;
using KruaImDee.Data;
using KruaImDee.Models;
using KruaImDee.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Controllers;

public class AccountController : Controller
{
    private readonly AppDb db;

    public AccountController(AppDb db)
    {
        this.db = db;
    }

    public IActionResult StaffLogin(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> StaffLogin(
        string email,
        string password,
        string? returnUrl = null
    )
    {
        ViewBag.Email = email;
        ViewBag.ReturnUrl = returnUrl;
        var user = await db.Users.SingleOrDefaultAsync(matchingUser =>
            matchingUser.Email == (email ?? "").Trim().ToLower() && matchingUser.Active
        );
        if (
            user == null
            || user.Role == "Customer"
            || string.IsNullOrEmpty(password)
            || new PasswordHasher<AppUser>().VerifyHashedPassword(user, user.PasswordHash, password)
                == PasswordVerificationResult.Failed
        )
        {
            ModelState.AddModelError("", "บัญชีพนักงานหรือรหัสผ่านไม่ถูกต้อง");
            return View();
        }

        await SignIn(user);
        if (Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl!);
        }
        else
        {
            return Redirect(user.Role == "Accountant" ? "/Finance" : "/Staff");
        }
    }

    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
    {
        ViewBag.Email = email;
        ViewBag.ReturnUrl = returnUrl;
        var user = await db.Users.SingleOrDefaultAsync(matchingUser =>
            matchingUser.Email == (email ?? "").Trim().ToLower() && matchingUser.Active
        );
        if (
            user == null
            || string.IsNullOrEmpty(password)
            || new PasswordHasher<AppUser>().VerifyHashedPassword(user, user.PasswordHash, password)
                == PasswordVerificationResult.Failed
        )
        {
            ModelState.AddModelError("", "อีเมลหรือรหัสผ่านไม่ถูกต้อง");
            return View();
        }

        await SignIn(user);
        if (Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl!);
        }
        if (user.Role == "Customer")
        {
            return Redirect("/Menu");
        }

        if (user.Role == "Accountant")
        {
            return Redirect("/Finance");
        }

        return Redirect("/Staff");
    }

    public IActionResult Register()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Register(
        string name,
        string email,
        string password,
        string phone
    )
    {
        email = (email ?? "").Trim().ToLower();
        ViewBag.Name = name;
        ViewBag.Email = email;
        ViewBag.Phone = phone;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            ModelState.AddModelError("name", "กรอกชื่อ 1 ถึง 100 ตัวอักษร");
        }

        if (
            email.Length > 180
            || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email)
        )
        {
            ModelState.AddModelError("email", "กรอกอีเมล เช่น example@gmail.com");
        }
        else if (await db.Users.AnyAsync(matchingUser => matchingUser.Email == email))
        {
            ModelState.AddModelError(
                "email",
                "อีเมลนี้มีบัญชีแล้ว กรุณาเข้าสู่ระบบหรือใช้อีเมลอื่น"
            );
        }

        if (string.IsNullOrEmpty(password) || password.Length < 8 || password.Length > 128)
        {
            ModelState.AddModelError("password", "กรอกรหัสผ่าน 8 ถึง 128 ตัวอักษร");
        }

        if (!ValidPhone(phone))
        {
            ModelState.AddModelError("phone", "กรอกเบอร์โทรศัพท์ 9 ถึง 10 หลัก เช่น 0812345678");
        }

        if (!ModelState.IsValid)
        {
            return View();
        }

        var user = new AppUser
        {
            Name = name.Trim(),
            Email = email,
            Phone = phone ?? "",
        };
        user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await SignIn(user);
        return Redirect("/Menu");
    }

    private async Task SignIn(AppUser user)
    {
        var claims = new List<Claim>();
        claims.Add(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
        claims.Add(new Claim(ClaimTypes.Name, user.Name));
        claims.Add(new Claim(ClaimTypes.Role, user.Role));
        claims.Add(
            new Claim(SessionSecurity.Claim, SessionSecurity.Fingerprint(user.PasswordHash))
        );
        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme
        );
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        HttpContext.Session.Clear();
        return Redirect("/");
    }

    [Authorize]
    public async Task<IActionResult> Profile()
    {
        return View(
            await db.Users.FindAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!))
        );
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> Profile(string name, string phone)
    {
        var user = await db.Users.FindAsync(
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
        );
        ViewBag.Name = name;
        ViewBag.Phone = phone;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            ModelState.AddModelError("name", "กรอกชื่อ 1 ถึง 100 ตัวอักษร");
        }

        if (!ValidPhone(phone))
        {
            ModelState.AddModelError("phone", "กรอกเบอร์โทรศัพท์ 9 ถึง 10 หลัก เช่น 0812345678");
        }

        if (!ModelState.IsValid)
        {
            return View(user);
        }

        user!.Name = name.Trim();
        user.Phone = phone ?? "";
        await db.SaveChangesAsync();
        await SignIn(user);
        TempData["Success"] = "บันทึกข้อมูลแล้ว";
        return RedirectToAction(nameof(Profile));
    }

    private static bool ValidPhone(string? phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 9 || phone.Length > 10)
        {
            return false;
        }

        foreach (char digit in phone)
        {
            if (digit < '0' || digit > '9')
            {
                return false;
            }
        }

        return true;
    }

    public IActionResult Denied()
    {
        Response.StatusCode = 403;
        return View();
    }
}
