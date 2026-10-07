using Microsoft.EntityFrameworkCore;
using BookingService.Models;
using System.Dynamic;

namespace BookingService.Data;

public class BookingDbContext : DbContext
{
    public BookingDbContext(DbContextOptions<BookingDbContext> options) : base(options) { }

    public DbSet<Booking> Bookings => Set<Booking>();
}
