using Microsoft.EntityFrameworkCore;
using InCleanHome.API.Booking.Domain.Model.Aggregates;
using InCleanHome.API.Shared.Infrastructure.Persistence.EFC.Configuration.Extensions;
using EntityFrameworkCore.CreatedUpdatedDate.Extensions;

namespace InCleanHome.API.Shared.Infrastructure.Persistence.EFC.Configuration
{
    public class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
    {
        protected override void OnConfiguring(DbContextOptionsBuilder builder)
        {
            builder.AddCreatedUpdatedInterceptor();
            base.OnConfiguring(builder);
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<BookingRequest>().HasKey(b => b.Id);
            builder.Entity<BookingRequest>().Property(b => b.Id).IsRequired().ValueGeneratedOnAdd();
            builder.Entity<BookingRequest>().Property(b => b.ClientId).IsRequired();
            builder.Entity<BookingRequest>().Property(b => b.WorkerId).IsRequired();
            builder.Entity<BookingRequest>().Property(b => b.ServiceType).IsRequired().HasMaxLength(40);
            builder.Entity<BookingRequest>().Property(b => b.Date).IsRequired();
            builder.Entity<BookingRequest>().Property(b => b.StartTime).IsRequired().HasMaxLength(5);
            builder.Entity<BookingRequest>().Property(b => b.EndTime).IsRequired().HasMaxLength(5);
            builder.Entity<BookingRequest>().Property(b => b.Hours).HasPrecision(5, 2);
            builder.Entity<BookingRequest>().Property(b => b.PaymentMethodId);
            builder.Entity<BookingRequest>().Property(b => b.Address).HasMaxLength(300);
            builder.Entity<BookingRequest>().Property(b => b.Notes).HasMaxLength(1000);
            builder.Entity<BookingRequest>().Property(b => b.HourlyRate).HasPrecision(10, 2);
            builder.Entity<BookingRequest>().Property(b => b.TotalAmount).HasPrecision(10, 2);
            builder.Entity<BookingRequest>().Property(b => b.PlatformFee).HasPrecision(10, 2);
            builder.Entity<BookingRequest>().Property(b => b.WorkerEarning).HasPrecision(10, 2);
            builder.Entity<BookingRequest>().Property(b => b.Status).IsRequired().HasMaxLength(30);
            builder.Entity<BookingRequest>().Property(b => b.IsPaid).HasDefaultValue(false);
            builder.Entity<BookingRequest>().HasIndex(b => b.ClientId);
            builder.Entity<BookingRequest>().HasIndex(b => b.WorkerId);

            // Apply snake_case naming convention
            builder.UseSnakeCaseNamingConvention();
        }
    }
}
