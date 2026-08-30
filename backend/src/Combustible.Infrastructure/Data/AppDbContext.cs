using Combustible.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Combustible.Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<LoginSession> Sessions => Set<LoginSession>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
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
            e.Property(x => x.NationalId).HasMaxLength(11);
            e.Property(x => x.Position).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Mobile).HasMaxLength(25);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.NationalId).IsUnique();
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
}
