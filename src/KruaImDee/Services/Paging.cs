using KruaImDee.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Services;

// T คือชนิดข้อมูลของแต่ละหน้า เช่น Order หรือ AppUser
// นับจำนวนทั้งหมด แต่ใช้ Skip และ Take อ่านเฉพาะหน้าที่ต้องแสดง
public static class Paging
{
    public static async Task<List<T>> Load<T>(
        Controller controller,
        IQueryable<T> query,
        int page,
        int pageSize = 12
    )
    {
        int count = await query.CountAsync();
        var pagination = new Pagination(page, pageSize, count);
        controller.ViewBag.Pagination = pagination;
        int skip = (pagination.Page - 1) * pageSize;
        return await query.Skip(skip).Take(pageSize).ToListAsync();
    }
}
