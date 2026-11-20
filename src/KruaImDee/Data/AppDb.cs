using KruaImDee.Models;
using Microsoft.EntityFrameworkCore;

namespace KruaImDee.Data;

public class AppDb : DbContext
{
    public DbSet<AppNotification> Notifications
    {
        get { return Set<AppNotification>(); }
    }

    public AppDb(DbContextOptions<AppDb> options)
        : base(options) { }

    public DbSet<AppUser> Users
    {
        get { return Set<AppUser>(); }
    }

    public DbSet<Category> Categories
    {
        get { return Set<Category>(); }
    }

    public DbSet<MenuItem> MenuItems
    {
        get { return Set<MenuItem>(); }
    }

    public DbSet<DiningTable> DiningTables
    {
        get { return Set<DiningTable>(); }
    }

    public DbSet<Promotion> Promotions
    {
        get { return Set<Promotion>(); }
    }

    public DbSet<Order> Orders
    {
        get { return Set<Order>(); }
    }

    public DbSet<OrderItem> OrderItems
    {
        get { return Set<OrderItem>(); }
    }

    public DbSet<Payment> Payments
    {
        get { return Set<Payment>(); }
    }

    public DbSet<Reservation> Reservations
    {
        get { return Set<Reservation>(); }
    }

    public DbSet<AuditLog> AuditLogs
    {
        get { return Set<AuditLog>(); }
    }

    public DbSet<RestaurantSetting> Settings
    {
        get { return Set<RestaurantSetting>(); }
    }

    public DbSet<Review> Reviews
    {
        get { return Set<Review>(); }
    }

    public DbSet<TaxInvoice> TaxInvoices
    {
        get { return Set<TaxInvoice>(); }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (Database.IsSqlite())
        {
            modelBuilder
                .Entity<RefundRequest>()
                .Property(refund => refund.RowVersion)
                .IsConcurrencyToken()
                .ValueGeneratedNever();
        }
        modelBuilder.Entity<AppNotification>().ToTable("Notifications");
        modelBuilder
            .Entity<AppNotification>()
            .HasIndex(item => new { item.UserId, item.EventKey })
            .IsUnique();
        modelBuilder
            .Entity<AppNotification>()
            .HasIndex(item => new
            {
                item.UserId,
                item.ReadAtUtc,
                item.Id,
            });
        modelBuilder
            .Entity<AppNotification>()
            .HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(item => item.UserId);
        modelBuilder
            .Entity<RefundRequest>()
            .HasIndex(matchingRefund => matchingRefund.ActivePaymentId)
            .IsUnique()
            .HasFilter("[ActivePaymentId] IS NOT NULL");
        modelBuilder
            .Entity<RefundRequest>()
            .HasIndex(matchingRefund => new
            {
                matchingRefund.RequestedBy,
                matchingRefund.RequestKey,
            })
            .IsUnique();
        modelBuilder
            .Entity<RefundRequest>()
            .HasIndex(matchingRefund => matchingRefund.RefundedAtUtc);
        modelBuilder
            .Entity<RefundRequest>()
            .HasOne(matchingRefund => matchingRefund.Payment)
            .WithMany()
            .HasForeignKey(matchingRefund => matchingRefund.PaymentId);
        foreach (string field in new[] { "RequestedBy", "DecidedBy", "ClaimedBy", "CompletedBy" })
        {
            modelBuilder.Entity<RefundRequest>().HasOne<AppUser>().WithMany().HasForeignKey(field);
        }

        modelBuilder
            .Entity<RefundRequest>()
            .ToTable(t =>
            {
                t.HasCheckConstraint("CK_Refunds_Amount", "[Amount] > 0");
                t.HasCheckConstraint(
                    "CK_Refunds_Active",
                    "([Status] IN ('PendingApproval','Approved','Paying','Completed') AND [ActivePaymentId] IS NOT NULL AND [ActivePaymentId]=[PaymentId]) OR ([Status] IN ('Rejected','Withdrawn','Revoked') AND [ActivePaymentId] IS NULL)"
                );
            });
        modelBuilder
            .Entity<Reconciliation>()
            .HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(matchingReconciliation => matchingReconciliation.RecordedBy);
        modelBuilder
            .Entity<Reconciliation>()
            .HasIndex(matchingReconciliation => matchingReconciliation.BusinessDate);
        modelBuilder.Entity<AppUser>().HasIndex(matchingUser => matchingUser.Email).IsUnique();
        modelBuilder
            .Entity<TaxInvoice>()
            .HasIndex(matchingInvoice => matchingInvoice.OrderId)
            .IsUnique();
        modelBuilder
            .Entity<TaxInvoice>()
            .HasIndex(matchingInvoice => matchingInvoice.Number)
            .IsUnique();
        modelBuilder
            .Entity<Promotion>()
            .HasIndex(matchingPromotion => matchingPromotion.Code)
            .IsUnique();
        modelBuilder
            .Entity<Payment>()
            .HasIndex(matchingPayment => matchingPayment.OrderId)
            .IsUnique();
        modelBuilder
            .Entity<Payment>()
            .HasOne(matchingPayment => matchingPayment.Order)
            .WithOne(matchingOrder => matchingOrder.Payment)
            .HasForeignKey<Payment>(matchingPayment => matchingPayment.OrderId);
        modelBuilder
            .Entity<Review>()
            .HasOne(matchingReview => matchingReview.MenuItem)
            .WithMany(matchingMenuItem => matchingMenuItem.Reviews)
            .HasForeignKey(matchingReview => matchingReview.MenuItemId);
        modelBuilder
            .Entity<Review>()
            .HasIndex(matchingReview => new { matchingReview.OrderId, matchingReview.MenuItemId })
            .IsUnique();
        modelBuilder
            .Entity<Review>()
            .ToTable(table =>
                table.HasCheckConstraint("CK_Reviews_Rating", "[Rating] >= 1 AND [Rating] <= 5")
            );
        modelBuilder
            .Entity<Order>()
            .HasIndex(matchingOrder => matchingOrder.DiningTableId)
            .IsUnique()
            .HasFilter("[DiningTableId] IS NOT NULL AND [Closed] = 0");
        modelBuilder
            .Entity<Order>()
            .HasIndex(matchingOrder => matchingOrder.PosRequestKey)
            .IsUnique()
            .HasFilter("[PosRequestKey] IS NOT NULL");
        modelBuilder
            .Entity<Reservation>()
            .HasIndex(matchingReservation => new
            {
                matchingReservation.DiningTableId,
                matchingReservation.StartsAt,
            });
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // จำนวนเงินมีทศนิยมสองตำแหน่งเหมือนกันทุกตาราง
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(decimal))
                {
                    property.SetPrecision(12);
                    property.SetScale(2);
                }
            }

            // ไม่ลบข้อมูลลูกตามอัตโนมัติ เพื่อรักษาประวัติธุรกรรม
            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }
    }

    private void UpdateSqliteVersions()
    {
        if (!Database.IsSqlite())
        {
            return;
        }
        // SQLite ไม่มี rowversion จึงสร้างค่าใหม่ทุกครั้งและตรวจค่าเดิมตอน UPDATE
        foreach (var entry in ChangeTracker.Entries<RefundRequest>())
        {
            if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            {
                entry.Entity.RowVersion = Guid.NewGuid().ToByteArray();
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        UpdateSqliteVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        UpdateSqliteVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public DbSet<RefundRequest> RefundRequests
    {
        get { return Set<RefundRequest>(); }
    }

    public DbSet<Reconciliation> Reconciliations
    {
        get { return Set<Reconciliation>(); }
    }
}
