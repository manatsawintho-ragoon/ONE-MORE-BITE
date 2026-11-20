using KruaImDee.Data;
using KruaImDee.Models;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Services;

// Combine current orders and reservations before offering tables.
public static class TableAvailability
{
    public static async Task<List<int>> Selectable(
        AppDb db,
        List<DiningTable> tables,
        int userId,
        bool staff
    )
    {
        var openTables = await db
            .Orders.Where(order => !order.Closed && order.DiningTableId != null)
            .Select(order => order.DiningTableId!.Value)
            .ToListAsync();
        var reservations = await db
            .Reservations.Where(reservation =>
                reservation.DiningTableId != null
                && reservation.Status != "Cancelled"
                && reservation.Status != "Completed"
                && (
                    reservation.Status == "Seated"
                    || reservation.StartsAt < DateTime.Now.AddMinutes(90)
                        && reservation.EndsAt > DateTime.Now
                )
            )
            .ToListAsync();
        var selectable = new List<int>();
        foreach (DiningTable table in tables)
        {
            if (openTables.Contains(table.Id))
            {
                continue;
            }

            var held = reservations
                .Where(reservation => reservation.DiningTableId == table.Id)
                .ToList();
            int owner = userId;
            var seated = held.FirstOrDefault(reservation => reservation.Status == "Seated");
            if (staff && seated != null)
            {
                owner = seated.UserId;
            }

            if (held.Any(reservation => reservation.UserId != owner))
            {
                continue;
            }

            selectable.Add(table.Id);
        }

        return selectable;
    }

    public static async Task<Dictionary<int, string>> Read(AppDb db, List<DiningTable> tables)
    {
        var openTables = await db
            .Orders.Where(order => !order.Closed && order.DiningTableId != null)
            .Select(order => order.DiningTableId!.Value)
            .ToListAsync();
        var reservedTables = await db
            .Reservations.Where(reservation =>
                reservation.DiningTableId != null
                && reservation.Status != "Cancelled"
                && reservation.Status != "Completed"
                && (
                    reservation.Status == "Seated"
                    || reservation.StartsAt < DateTime.Now.AddMinutes(90)
                        && reservation.EndsAt > DateTime.Now
                )
            )
            .Select(reservation => reservation.DiningTableId!.Value)
            .ToListAsync();
        var states = new Dictionary<int, string>();
        foreach (DiningTable table in tables)
        {
            string state = "Available";
            if (reservedTables.Contains(table.Id))
            {
                state = "Reserved";
            }

            if (openTables.Contains(table.Id))
            {
                state = "Occupied";
            }

            states[table.Id] = state;
        }

        return states;
    }
}
