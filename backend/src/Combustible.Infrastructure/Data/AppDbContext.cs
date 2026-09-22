using Combustible.Domain;
using Combustible.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Combustible.Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, FieldProtector protector)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    // RS-03: cédula, correo y móvil del empleado se guardan cifrados (AES-256-GCM). La unicidad de la
    // cédula usa un índice ciego (HMAC) porque el texto cifrado es aleatorio.
    public const string NationalIdPurpose = "Employee.NationalId";
    public const string EmailPurpose = "Employee.Email";
    public const string MobilePurpose = "Employee.Mobile";
    public const string NationalIdHash = "NationalIdHash";
    public string ProtectorKeyId => protector.KeyId;

    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<LoginSession> Sessions => Set<LoginSession>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<FuelType> FuelTypes => Set<FuelType>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Tank> Tanks => Set<Tank>();
    public DbSet<TicketSettings> TicketSettings => Set<TicketSettings>();
    public DbSet<TicketSequence> TicketSequences => Set<TicketSequence>();
    public DbSet<FuelRequest> FuelRequests => Set<FuelRequest>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketDelivery> TicketDeliveries => Set<TicketDelivery>();
    public DbSet<FuelSchedule> FuelSchedules => Set<FuelSchedule>();
    public DbSet<Dispatch> Dispatches => Set<Dispatch>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<FuelReceipt> FuelReceipts => Set<FuelReceipt>();
    public DbSet<DailyClose> DailyCloses => Set<DailyClose>();
    public DbSet<DailyCloseLine> DailyCloseLines => Set<DailyCloseLine>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationRead> NotificationReads => Set<NotificationRead>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        ConfigureFuel(builder);
        builder.Entity<AppUser>().Property(x => x.DisplayName).HasMaxLength(150);
        builder.Entity<AppUser>().HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.Entity<Department>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Code).IsUnique();
        });
        builder.Entity<Employee>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.NationalId).HasMaxLength(512)
                .HasConversion(v => protector.Protect(v, NationalIdPurpose), v => protector.Unprotect(v, NationalIdPurpose));
            e.Property(x => x.Position).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(512)
                .HasConversion(v => protector.Protect(v, EmailPurpose), v => protector.Unprotect(v, EmailPurpose));
            e.Property(x => x.Mobile).HasMaxLength(512)
                .HasConversion(v => protector.Protect(v, MobilePurpose), v => protector.Unprotect(v, MobilePurpose));
            e.Property<string>(NationalIdHash).HasMaxLength(64);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(NationalIdHash).IsUnique();
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Vehicle>(e =>
        {
            e.Property(x => x.Plate).HasMaxLength(20);
            e.Property(x => x.InternalCode).HasMaxLength(30);
            e.Property(x => x.Make).HasMaxLength(80);
            e.Property(x => x.Model).HasMaxLength(80);
            e.Property(x => x.Kind).HasMaxLength(80);
            e.Property(x => x.TankCapacity).HasPrecision(9, 3);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Plate).IsUnique();
            e.HasIndex(x => x.InternalCode).IsUnique();
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Vehicle_Capacity", "\"TankCapacity\" > 0");
                t.HasCheckConstraint("CK_Vehicle_Odometer", "\"Odometer\" >= 0");
                t.HasCheckConstraint("CK_Vehicle_Year", "\"Year\" BETWEEN 1900 AND 2200");
            });
        });
        builder.Entity<LoginSession>(e =>
        {
            e.Property(x => x.RefreshHash).HasMaxLength(64);
            e.Property(x => x.SecurityStamp).HasMaxLength(100);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.RefreshHash).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<AuditEvent>(e =>
        {
            e.Property(x => x.Actor).HasMaxLength(100);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.Entity).HasMaxLength(100);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.PreviousHash).HasMaxLength(64);
            e.Property(x => x.Hash).HasMaxLength(64);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampBlindIndexes();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampBlindIndexes();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampBlindIndexes()
    {
        foreach (var entry in ChangeTracker.Entries<Employee>().Where(x => x.State is EntityState.Added or EntityState.Modified))
            entry.Property(NationalIdHash).CurrentValue = protector.BlindIndex(entry.Entity.NationalId, NationalIdPurpose);
    }

    private static readonly string[] CloseAmounts = ["Opening", "Inputs", "Outputs", "Expected", "Counted", "Difference"];

    private static void ConfigureFuel(ModelBuilder builder)
    {
        builder.Entity<FuelType>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Code).IsUnique();
        });
        builder.Entity<Station>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Code).IsUnique();
        });
        builder.Entity<Tank>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.Capacity).HasPrecision(12, 3);
            e.Property(x => x.CriticalLevel).HasPrecision(12, 3);
            e.Property(x => x.Balance).HasPrecision(12, 3);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne<Station>().WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FuelType>().WithMany().HasForeignKey(x => x.FuelTypeId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Tank_Capacity", "\"Capacity\" > 0");
                t.HasCheckConstraint("CK_Tank_Critical", "\"CriticalLevel\" >= 0 AND \"CriticalLevel\" <= \"Capacity\"");
                t.HasCheckConstraint("CK_Tank_Balance", "\"Balance\" >= 0 AND \"Balance\" <= \"Capacity\"");
            });
        });
        builder.Entity<TicketSettings>(e =>
        {
            e.Property(x => x.Prefix).HasMaxLength(10);
            e.Property(x => x.Version).IsConcurrencyToken();
            // Valores iniciales de ADR-007 (H-03) y ejemplo de RF-08; editables por el Administrador.
            e.HasData(new TicketSettings
            {
                Id = Domain.TicketSettings.SingletonId, Prefix = "COM", ResetAnnually = true, ValidityDays = 7, WarningHours = 24,
                MaxActiveTicketsPerVehicle = 1, Version = new Guid("6f1c2d0e-5b7a-4c1e-9a2f-3d4b5c6e7f80"),
            });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Settings_Validity", "\"ValidityDays\" BETWEEN 1 AND 90");
                t.HasCheckConstraint("CK_Settings_Warning", "\"WarningHours\" BETWEEN 1 AND 720");
                t.HasCheckConstraint("CK_Settings_MaxActive", "\"MaxActiveTicketsPerVehicle\" BETWEEN 1 AND 20");
            });
        });
        builder.Entity<TicketSequence>(e =>
        {
            e.HasKey(x => x.Scope);
            e.Property(x => x.Scope).HasMaxLength(20);
        });
        builder.Entity<FuelRequest>(e =>
        {
            e.Property(x => x.AuthorizedQuantity).HasPrecision(12, 3);
            e.Property(x => x.Origin).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.RequestedBy).HasMaxLength(100);
            e.Property(x => x.DecidedBy).HasMaxLength(100);
            e.Property(x => x.DecisionReason).HasMaxLength(500);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.Status, x.RequestedAt });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FuelType>().WithMany().HasForeignKey(x => x.FuelTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FuelSchedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Request_Quantity", "\"AuthorizedQuantity\" > 0"));
        });
        builder.Entity<Ticket>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(40);
            e.Property(x => x.Scope).HasMaxLength(20);
            e.Property(x => x.ShortCode).HasMaxLength(8);
            e.Property(x => x.AuthorizedQuantity).HasPrecision(12, 3);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.TokenCipher).HasMaxLength(200);
            e.Property(x => x.Digest).HasMaxLength(64);
            e.Property(x => x.Signature).HasMaxLength(200);
            e.Property(x => x.IssuedBy).HasMaxLength(100);
            e.Property(x => x.VoidReason).HasMaxLength(500);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.Scope, x.Sequence }).IsUnique();
            e.HasIndex(x => x.ShortCode).IsUnique();
            e.HasIndex(x => x.RequestId).IsUnique();
            e.HasIndex(x => new { x.Status, x.ExpiresAt });
            e.HasOne<FuelRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FuelType>().WithMany().HasForeignKey(x => x.FuelTypeId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Ticket_Quantity", "\"AuthorizedQuantity\" > 0");
                t.HasCheckConstraint("CK_Ticket_Expiry", "\"ExpiresAt\" > \"IssuedAt\"");
                t.HasCheckConstraint("CK_Ticket_Consumed", "(\"Status\" = 'Consumed') = (\"ConsumedAt\" IS NOT NULL)");
            });
        });
        builder.Entity<TicketDelivery>(e =>
        {
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Result).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Destination).HasMaxLength(254);
            e.Property(x => x.Detail).HasMaxLength(500);
            e.Property(x => x.Actor).HasMaxLength(100);
            e.HasIndex(x => x.TicketId);
            e.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<FuelSchedule>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Quantity).HasPrecision(12, 3);
            e.Property(x => x.Rule).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Frequency).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.Active, x.NextRunAt });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FuelType>().WithMany().HasForeignKey(x => x.FuelTypeId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Schedule_Quantity", "(\"Rule\" = 'History') OR \"Quantity\" > 0");
                t.HasCheckConstraint("CK_Schedule_History", "\"HistorySize\" BETWEEN 1 AND 50");
            });
        });
        builder.Entity<Dispatch>(e =>
        {
            e.Property(x => x.Quantity).HasPrecision(12, 3);
            e.Property(x => x.Difference).HasPrecision(12, 3);
            e.Property(x => x.DifferenceReason).HasMaxLength(500);
            e.Property(x => x.Observations).HasMaxLength(500);
            // CA-2 en la base: un ticket solo puede tener un despacho.
            e.HasIndex(x => x.TicketId).IsUnique();
            e.HasIndex(x => x.OccurredAt);
            e.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Station>().WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Dispatch_Quantity", "\"Quantity\" > 0");
                t.HasCheckConstraint("CK_Dispatch_Difference", "\"Difference\" >= 0");
                t.HasCheckConstraint("CK_Dispatch_Identity", "\"IdentityConfirmed\"");
            });
        });
        builder.Entity<InventoryMovement>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Quantity).HasPrecision(12, 3);
            e.Property(x => x.BalanceAfter).HasPrecision(12, 3);
            e.Property(x => x.Actor).HasMaxLength(100);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasIndex(x => new { x.TankId, x.OccurredAt });
            e.HasIndex(x => x.DispatchId).IsUnique();
            e.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Dispatch>().WithMany().HasForeignKey(x => x.DispatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FuelReceipt>().WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Movement_NonZero", "\"Quantity\" <> 0");
                t.HasCheckConstraint("CK_Movement_Balance", "\"BalanceAfter\" >= 0");
                // Un movimiento de despacho no existe sin un despacho validado por QR (CA-2).
                t.HasCheckConstraint("CK_Movement_Dispatch", "(\"Kind\" = 'Dispatch') = (\"DispatchId\" IS NOT NULL)");
                t.HasCheckConstraint("CK_Movement_Sign",
                    "(\"Kind\" IN ('Receipt','Purchase','TransferIn','PositiveAdjustment') AND \"Quantity\" > 0) OR " +
                    "(\"Kind\" IN ('TransferOut','Dispatch','Shrinkage','NegativeAdjustment') AND \"Quantity\" < 0)");
            });
        });
        builder.Entity<FuelReceipt>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SupplierRnc).HasMaxLength(11);
            e.Property(x => x.SupplierName).HasMaxLength(200);
            e.Property(x => x.Invoice).HasMaxLength(50);
            e.Property(x => x.Quantity).HasPrecision(12, 3);
            e.Property(x => x.Actor).HasMaxLength(100);
            e.HasIndex(x => new { x.SupplierRnc, x.Invoice }).IsUnique();
            e.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Receipt_Quantity", "\"Quantity\" > 0");
                t.HasCheckConstraint("CK_Receipt_Kind", "\"Kind\" IN ('Receipt','Purchase')");
            });
        });
        builder.Entity<DailyClose>(e =>
        {
            e.Property(x => x.DispatchedVolume).HasPrecision(12, 3);
            e.Property(x => x.Actor).HasMaxLength(100);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasIndex(x => new { x.StationId, x.Day }).IsUnique();
            e.HasOne<Station>().WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.DailyCloseId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<DailyCloseLine>(e =>
        {
            foreach (var name in CloseAmounts)
                e.Property(name).HasPrecision(12, 3);
            e.HasIndex(x => new { x.DailyCloseId, x.TankId }).IsUnique();
            e.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Notification>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.DedupKey).HasMaxLength(150);
            e.Property(x => x.Message).HasMaxLength(500);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.HasIndex(x => x.DedupKey).IsUnique();
            e.HasIndex(x => x.CreatedAt);
        });
        builder.Entity<NotificationRead>(e =>
        {
            e.HasKey(x => new { x.NotificationId, x.UserId });
            e.HasOne<Notification>().WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

// Los convertidores capturan la clave: modelos distintos para claves distintas en el mismo proceso.
public sealed class ProtectorModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is AppDbContext app ? (context.GetType(), app.ProtectorKeyId, designTime) : (context.GetType(), designTime);
}
