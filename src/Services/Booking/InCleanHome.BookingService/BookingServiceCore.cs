using System.ComponentModel.DataAnnotations.Schema;
using EntityFrameworkCore.CreatedUpdatedDate.Contracts;
using Microsoft.EntityFrameworkCore;
using InCleanHome.API.Booking.Domain.Model.ValueObjects;
using InCleanHome.API.Booking.Domain.Model.Aggregates;
using InCleanHome.API.Booking.Domain.Model.Commands;
using InCleanHome.API.Booking.Domain.Model.Queries;
using InCleanHome.API.Booking.Domain.Repositories;
using InCleanHome.API.Booking.Domain.Services;
using InCleanHome.API.Shared.Domain.Repositories;
using InCleanHome.API.Shared.Infrastructure.Persistence.EFC.Repositories;
using InCleanHome.API.Profiles.Domain.Services;
using InCleanHome.API.Profiles.Interfaces.ACL;
using InCleanHome.API.Profiles.Domain.Model.Aggregates;
using InCleanHome.API.Notifications.Interfaces.ACL;
using InCleanHome.API.IAM.Interfaces.ACL;
using InCleanHome.API.IAM.Domain.Model.Aggregates;
using InCleanHome.API.Profiles.Domain.Model.Queries;

#region Mocks & DTOs for external contexts
namespace InCleanHome.API.Profiles.Domain.Model.Queries
{
    public record GetWorkerProfileByUserIdQuery(int WorkerUserId);
}
namespace InCleanHome.API.IAM.Domain.Model.Aggregates
{
    public class User
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool DocumentsVerified { get; set; } = true;
        public bool IsCurrentlySuspended() => false;
    }

    public static class UserRole
    {
        public const string Client = "client";
        public const string Worker = "worker";
        public const string Admin = "admin";
    }
}

namespace InCleanHome.API.Profiles.Domain.Model.Aggregates
{
    public class WorkerProfile
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; } = "Mock Worker";
        public decimal HourlyRate { get; set; } = 40.0m;
    }
}

namespace InCleanHome.API.IAM.Interfaces.ACL
{
    public interface IIamContextFacade
    {
        Task<int> FetchUserIdByEmail(string email);
        Task<string> FetchEmailByUserId(int userId);
        Task<string> FetchRoleByUserId(int userId);
        Task<bool> IsUserSuspended(int userId);
        Task<bool> IsWorkerApproved(int userId);
        Task SuspendUser(int userId, TimeSpan duration, string reason);
    }

    public class IamContextFacadeMock : IIamContextFacade
    {
        public Task<int> FetchUserIdByEmail(string email) => Task.FromResult(1);
        public Task<string> FetchEmailByUserId(int userId) => Task.FromResult("mock@user.com");
        public Task<string> FetchRoleByUserId(int userId) => Task.FromResult(userId % 2 == 0 ? "worker" : "client");
        public Task<bool> IsUserSuspended(int userId) => Task.FromResult(false);
        public Task<bool> IsWorkerApproved(int userId) => Task.FromResult(true);
        public Task SuspendUser(int userId, TimeSpan duration, string reason)
        {
            Console.WriteLine($"[IAM MOCK] Suspended user {userId} for {duration.TotalHours} hours. Reason: {reason}");
            return Task.CompletedTask;
        }
    }
}

namespace InCleanHome.API.Profiles.Interfaces.ACL
{
    public interface IProfilesContextFacade
    {
        Task<string> FetchUserNameByUserId(int userId);
        Task<decimal> FetchWorkerHourlyRateByUserId(int userId);
        Task RegisterWorkerCompletedService(int workerUserId, int rating);
        Task<string?> FetchWorkerPhotoByUserId(int userId);
        Task<string?> FetchClientPhotoByUserId(int userId);
    }

    public class ProfilesContextFacadeMock : IProfilesContextFacade
    {
        public Task<string> FetchUserNameByUserId(int userId) => Task.FromResult($"User {userId}");
        public Task<decimal> FetchWorkerHourlyRateByUserId(int userId) => Task.FromResult(40.0m);
        public Task RegisterWorkerCompletedService(int workerUserId, int rating) => Task.CompletedTask;
        public Task<string?> FetchWorkerPhotoByUserId(int userId) => Task.FromResult<string?>("https://placehold.co/100");
        public Task<string?> FetchClientPhotoByUserId(int userId) => Task.FromResult<string?>("https://placehold.co/100");
    }
}

namespace InCleanHome.API.Notifications.Interfaces.ACL
{
    public interface INotificationsContextFacade
    {
        Task CreateNotification(int userId, string type, string title, string body, string? link);
    }

    public class NotificationsContextFacadeMock : INotificationsContextFacade
    {
        public Task CreateNotification(int userId, string type, string title, string body, string? link)
        {
            Console.WriteLine($"[NOTIFICATION MOCK] Notify user {userId} type={type} title='{title}' body='{body}' link='{link}'");
            return Task.CompletedTask;
        }
    }
}

namespace InCleanHome.API.Profiles.Domain.Services
{
    public interface IWorkerProfileQueryService
    {
        Task<WorkerProfile?> Handle(GetWorkerProfileByUserIdQuery query);
    }

    public class WorkerProfileQueryServiceMock : IWorkerProfileQueryService
    {
        public Task<WorkerProfile?> Handle(GetWorkerProfileByUserIdQuery query)
        {
            return Task.FromResult<WorkerProfile?>(new WorkerProfile { UserId = query.WorkerUserId, Name = $"Worker {query.WorkerUserId}", HourlyRate = 35.0m });
        }
    }

    public interface IWorkerProfileCommandService
    {
        Task RegisterWorkerCompletedService(int workerUserId, int rating);
    }

    public class WorkerProfileCommandServiceMock : IWorkerProfileCommandService
    {
        public Task RegisterWorkerCompletedService(int workerUserId, int rating) => Task.CompletedTask;
    }
}
#endregion

#region Domain Model (Booking Request)
namespace InCleanHome.API.Booking.Domain.Model.ValueObjects
{
    public static class BookingStatus
    {
        public const string Pending   = "pending";
        public const string Accepted  = "accepted";
        public const string Rejected  = "rejected";
        public const string Cancelled = "cancelled";
        public const string Completed = "completed";

        public static readonly string[] All = { Pending, Accepted, Rejected, Cancelled, Completed };
        public static bool IsValid(string s) => All.Contains(s);
    }
}

namespace InCleanHome.API.Booking.Domain.Model.Aggregates
{
    public class BookingRequest : IEntityWithCreatedUpdatedDate
    {
        public const decimal PlatformFeeRate = 0.10m;

        public int Id { get; private set; }
        public int ClientId { get; private set; }
        public int WorkerId { get; private set; }

        public string ServiceType { get; private set; } = string.Empty;
        public DateOnly Date { get; private set; }
        public string StartTime { get; private set; } = "00:00";
        public string EndTime { get; private set; }   = "00:00";
        public decimal Hours { get; private set; }

        public int PaymentMethodId { get; private set; }
        public string Address { get; private set; } = string.Empty;
        public string Notes { get; private set; }   = string.Empty;

        public decimal HourlyRate { get; private set; }
        public decimal TotalAmount { get; private set; }
        public decimal PlatformFee { get; private set; }
        public decimal WorkerEarning { get; private set; }

        public string Status { get; private set; } = BookingStatus.Pending;
        public bool IsPaid { get; private set; } = false;

        [Column("CreatedAt")] public DateTimeOffset? CreatedDate { get; set; }
        [Column("UpdatedAt")] public DateTimeOffset? UpdatedDate { get; set; }

        public BookingRequest() { }

        public BookingRequest(
            int clientId, int workerId, string serviceType, DateOnly date,
            string startTime, string endTime, decimal hours, int paymentMethodId,
            string address, string notes, decimal hourlyRate)
        {
            ClientId        = clientId;
            WorkerId        = workerId;
            ServiceType     = serviceType;
            Date            = date;
            StartTime       = startTime;
            EndTime         = endTime;
            Hours           = hours;
            PaymentMethodId = paymentMethodId;
            Address         = address ?? string.Empty;
            Notes           = notes ?? string.Empty;
            HourlyRate      = hourlyRate;
            TotalAmount     = Math.Round(hourlyRate * hours, 2);
            PlatformFee     = Math.Round(TotalAmount * PlatformFeeRate, 2);
            WorkerEarning   = TotalAmount - PlatformFee;
            Status          = BookingStatus.Pending;
            IsPaid          = false;
        }

        public BookingRequest Accept()
        {
            if (Status != BookingStatus.Pending)
                throw new InvalidOperationException("Only pending bookings can be accepted.");
            Status = BookingStatus.Accepted;
            return this;
        }

        public BookingRequest Reject()
        {
            if (Status != BookingStatus.Pending)
                throw new InvalidOperationException("Only pending bookings can be rejected.");
            Status = BookingStatus.Rejected;
            return this;
        }

        public BookingRequest CancelByClient()
        {
            if (Status != BookingStatus.Pending && Status != BookingStatus.Accepted)
                throw new InvalidOperationException("Only pending or accepted bookings can be cancelled.");
            Status = BookingStatus.Cancelled;
            return this;
        }

        public BookingRequest CancelByWorker()
        {
            if (Status != BookingStatus.Pending && Status != BookingStatus.Accepted)
                throw new InvalidOperationException("Only pending or accepted bookings can be cancelled.");
            Status = BookingStatus.Cancelled;
            return this;
        }

        public int BusinessDaysUntilService()
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (Date <= today) return 0;
            var count = 0;
            var cursor = today;
            while (cursor < Date)
            {
                cursor = cursor.AddDays(1);
                if (cursor.DayOfWeek != DayOfWeek.Saturday && cursor.DayOfWeek != DayOfWeek.Sunday)
                    count++;
            }
            return count;
        }

        public bool IsLateCancellation(bool byWorker)
            => BusinessDaysUntilService() < (byWorker ? 7 : 3);

        public BookingRequest Complete()
        {
            if (Status != BookingStatus.Accepted)
                throw new InvalidOperationException("Only accepted bookings can be completed.");
            Status = BookingStatus.Completed;
            return this;
        }

        public BookingRequest Reschedule(DateOnly newDate, string newStart, string newEnd, decimal newHours, decimal hourlyRate)
        {
            if (Status != BookingStatus.Pending && Status != BookingStatus.Accepted)
                throw new InvalidOperationException("Solo reservas pendientes o aceptadas pueden reprogramarse.");
            if (newDate < DateOnly.FromDateTime(DateTime.UtcNow))
                throw new ArgumentException("La nueva fecha no puede estar en el pasado.");
            if (newHours <= 0)
                throw new ArgumentException("Las horas deben ser mayores a cero.");

            Date          = newDate;
            StartTime     = newStart;
            EndTime       = newEnd;
            Hours         = newHours;
            TotalAmount   = Math.Round(hourlyRate * newHours, 2);
            PlatformFee   = Math.Round(TotalAmount * 0.10m, 2);
            WorkerEarning = TotalAmount - PlatformFee;
            Status = BookingStatus.Pending;
            return this;
        }

        public void ConfirmPayment()
        {
            IsPaid = true;
        }
    }
}
#endregion

#region Domain Model (Commands & Queries)
namespace InCleanHome.API.Booking.Domain.Model.Commands
{
    public record CreateBookingCommand(
        int ClientId,
        int WorkerId,
        string ServiceType,
        DateOnly Date,
        string StartTime,
        string EndTime,
        decimal Hours,
        int PaymentMethodId,
        string Address,
        string? Notes);

    public record UpdateBookingStatusCommand(int BookingId, int RequesterUserId, string RequesterRole, string NewStatus);

    public record RescheduleBookingCommand(
        int BookingId,
        int RequesterUserId,
        DateOnly NewDate,
        string NewStartTime,
        string NewEndTime,
        decimal NewHours);
}

namespace InCleanHome.API.Booking.Domain.Model.Queries
{
    public record GetBookingByIdQuery(int Id);
    public record GetBookingsByClientUserIdQuery(int ClientUserId);
    public record GetBookingsByWorkerUserIdQuery(int WorkerUserId);
}
#endregion

#region Domain Services & Repositories Interfaces
namespace InCleanHome.API.Booking.Domain.Repositories
{
    public interface IBookingRequestRepository : IBaseRepository<BookingRequest>
    {
        Task<IEnumerable<BookingRequest>> FindByClientUserIdAsync(int clientUserId);
        Task<IEnumerable<BookingRequest>> FindByWorkerUserIdAsync(int workerUserId);
        Task<IEnumerable<BookingRequest>> FindWorkerOverlappingAsync(int workerUserId, DateOnly date, string startTime, string endTime);
    }
}

namespace InCleanHome.API.Booking.Domain.Services
{
    public interface IBookingRequestCommandService
    {
        Task<BookingRequest> Handle(CreateBookingCommand command);
        Task<BookingRequest?> Handle(UpdateBookingStatusCommand command);
        Task<BookingRequest?> Handle(RescheduleBookingCommand command);
    }

    public interface IBookingRequestQueryService
    {
        Task<BookingRequest?> Handle(GetBookingByIdQuery query);
        Task<IEnumerable<BookingRequest>> Handle(GetBookingsByClientUserIdQuery query);
        Task<IEnumerable<BookingRequest>> Handle(GetBookingsByWorkerUserIdQuery query);
    }
}
#endregion

#region Application Services Implementation
namespace InCleanHome.API.Booking.Application.Internal.CommandServices
{
    using MassTransit;
    using InCleanHome.Shared.Contracts.Events;

    public class BookingRequestCommandService(
        IBookingRequestRepository repository,
        IWorkerProfileQueryService workerQueryService,
        IProfilesContextFacade profilesFacade,
        INotificationsContextFacade notificationsFacade,
        IIamContextFacade iamFacade,
        IUnitOfWork unitOfWork,
        IPublishEndpoint? publishEndpoint = null) : IBookingRequestCommandService
    {
        public async Task<BookingRequest> Handle(CreateBookingCommand c)
        {
            if (await iamFacade.IsUserSuspended(c.ClientId))
                throw new InvalidOperationException("Tu cuenta está temporalmente suspendida por una cancelación tardía. No puedes reservar hasta que termine la sanción.");

            if (!await iamFacade.IsWorkerApproved(c.WorkerId))
                throw new InvalidOperationException("Trabajador(a) aún no ha sido aprobada por administración.");

            if (await iamFacade.IsUserSuspended(c.WorkerId))
                throw new InvalidOperationException("Trabajador(a) se encuentra temporalmente suspendida. Inténtalo más tarde.");

            var worker = await workerQueryService.Handle(new GetWorkerProfileByUserIdQuery(c.WorkerId))
                ?? throw new Exception("Worker not found");

            var overlapping = await repository.FindWorkerOverlappingAsync(c.WorkerId, c.Date, c.StartTime, c.EndTime);
            if (overlapping.Any())
                throw new InvalidOperationException("No puedes reservar a esta hora, ya que otro cliente ya reservó a esta hora con este/esta trabajador(a). Por favor elige otro horario o fecha.");

            var booking = new BookingRequest(
                c.ClientId, c.WorkerId, c.ServiceType, c.Date,
                c.StartTime, c.EndTime, c.Hours, c.PaymentMethodId,
                c.Address, c.Notes ?? string.Empty, worker.HourlyRate);

            await repository.AddAsync(booking);
            await unitOfWork.CompleteAsync();

            // Notify worker
            var clientName = await profilesFacade.FetchUserNameByUserId(c.ClientId);
            await notificationsFacade.CreateNotification(
                c.WorkerId, "pending", "Nueva solicitud",
                $"{clientName} solicitó un servicio para el {c.Date:yyyy-MM-dd}. Revísalo en tus solicitudes.", "/worker/requests");

            // Publish integration event
            if (publishEndpoint is not null)
            {
                await publishEndpoint.Publish(new BookingCreatedEvent
                {
                    BookingId = booking.Id,
                    ClientId = booking.ClientId,
                    WorkerId = booking.WorkerId,
                    Amount = booking.TotalAmount,
                    ServiceType = booking.ServiceType,
                    Address = booking.Address
                });
            }

            return booking;
        }

        public async Task<BookingRequest?> Handle(UpdateBookingStatusCommand c)
        {
            var booking = await repository.FindByIdAsync(c.BookingId);
            if (booking is null) return null;

            var isClient = c.RequesterRole == UserRole.Client && booking.ClientId == c.RequesterUserId;
            var isWorker = c.RequesterRole == UserRole.Worker && booking.WorkerId == c.RequesterUserId;
            var isAdmin  = c.RequesterRole == UserRole.Admin;
            if (!isClient && !isWorker && !isAdmin)
                throw new UnauthorizedAccessException("Not allowed to change this booking");

            switch (c.NewStatus)
            {
                case BookingStatus.Accepted:
                    if (!isWorker && !isAdmin) throw new UnauthorizedAccessException("Only workers can accept bookings");
                    booking.Accept();
                    break;
                case BookingStatus.Rejected:
                    if (!isWorker && !isAdmin) throw new UnauthorizedAccessException("Only workers can reject bookings");
                    booking.Reject();
                    break;
                case BookingStatus.Cancelled:
                    if (isWorker)
                    {
                        var late = booking.IsLateCancellation(byWorker: true);
                        booking.CancelByWorker();
                        if (late)
                            await iamFacade.SuspendUser(booking.WorkerId, TimeSpan.FromDays(7), "Cancelación tardía (menos de 7 días hábiles antes del servicio).");
                    }
                    else
                    {
                        var late = booking.IsLateCancellation(byWorker: false);
                        booking.CancelByClient();
                        if (late)
                            await iamFacade.SuspendUser(booking.ClientId, TimeSpan.FromHours(48), "Cancelación tardía (menos de 3 días hábiles antes del servicio).");
                    }

                    if (publishEndpoint is not null)
                    {
                        await publishEndpoint.Publish(new BookingCancelledEvent
                        {
                            BookingId = booking.Id,
                            ClientId = booking.ClientId,
                            WorkerId = booking.WorkerId
                        });
                    }
                    break;
                case BookingStatus.Completed:
                    if (!isWorker && !isAdmin) throw new UnauthorizedAccessException("Only workers can complete bookings");
                    booking.Complete();

                    // Publish completed event so reviews are enabled
                    if (publishEndpoint is not null)
                    {
                        await publishEndpoint.Publish(new BookingCompletedEvent
                        {
                            BookingId = booking.Id,
                            ClientId = booking.ClientId,
                            WorkerId = booking.WorkerId,
                            TotalAmount = booking.TotalAmount
                        });
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported status transition '{c.NewStatus}'");
            }

            repository.Update(booking);
            await unitOfWork.CompleteAsync();

            await NotifyStatusChange(booking, c.NewStatus, isWorker);

            return booking;
        }

        private async Task NotifyStatusChange(BookingRequest booking, string status, bool changedByWorker)
        {
            var workerName = await profilesFacade.FetchUserNameByUserId(booking.WorkerId);
            var clientName = await profilesFacade.FetchUserNameByUserId(booking.ClientId);

            switch (status)
            {
                case BookingStatus.Accepted:
                    await notificationsFacade.CreateNotification(booking.ClientId, "accepted", "Reserva aceptada", $"Trabajador(a) {workerName} aceptó tu reserva del {booking.Date:yyyy-MM-dd}.", "/client/bookings");
                    break;
                case BookingStatus.Rejected:
                    await notificationsFacade.CreateNotification(booking.ClientId, "rejected", "Reserva rechazada", $"Trabajador(a) {workerName} no pudo aceptar tu reserva del {booking.Date:yyyy-MM-dd}.", "/client/bookings");
                    break;
                case BookingStatus.Completed:
                    await notificationsFacade.CreateNotification(booking.ClientId, "completed", "Servicio completado", $"Tu servicio con el/la trabajador(a) {workerName} fue completado.", "/client/bookings");
                    break;
                case BookingStatus.Cancelled:
                    if (changedByWorker)
                        await notificationsFacade.CreateNotification(booking.ClientId, "cancelled", "Reserva cancelada", $"Trabajador(a) {workerName} canceló la reserva del {booking.Date:yyyy-MM-dd}.", "/client/bookings");
                    else
                        await notificationsFacade.CreateNotification(booking.WorkerId, "cancelled", "Reserva cancelada", $"{clientName} canceló la reserva del {booking.Date:yyyy-MM-dd}.", "/worker/requests");
                    break;
            }
        }

        public async Task<BookingRequest?> Handle(RescheduleBookingCommand c)
        {
            var booking = await repository.FindByIdAsync(c.BookingId);
            if (booking is null) return null;

            var isClient = booking.ClientId == c.RequesterUserId;
            var isWorker = booking.WorkerId == c.RequesterUserId;
            if (!isClient && !isWorker)
                throw new UnauthorizedAccessException("No tienes permiso para reprogramar esta reserva.");

            var worker = await workerQueryService.Handle(new GetWorkerProfileByUserIdQuery(booking.WorkerId))
                ?? throw new Exception("Worker not found");

            booking.Reschedule(c.NewDate, c.NewStartTime, c.NewEndTime, c.NewHours, worker.HourlyRate);
            repository.Update(booking);
            await unitOfWork.CompleteAsync();

            var workerName = await profilesFacade.FetchUserNameByUserId(booking.WorkerId);
            var clientName = await profilesFacade.FetchUserNameByUserId(booking.ClientId);
            if (isClient)
                await notificationsFacade.CreateNotification(booking.WorkerId, "pending", "Solicitud reprogramada", $"{clientName} reprogramó la reserva al {booking.Date:yyyy-MM-dd} ({booking.StartTime}–{booking.EndTime}). Necesita tu confirmación.", "/worker/requests");
            else
                await notificationsFacade.CreateNotification(booking.ClientId, "pending", "Reserva reprogramada", $"La trabajador(a) {workerName} reprogramó tu reserva al {booking.Date:yyyy-MM-dd} ({booking.StartTime}–{booking.EndTime}).", "/client/bookings");

            return booking;
        }
    }
}

namespace InCleanHome.API.Booking.Application.Internal.QueryServices
{
    public class BookingRequestQueryService(IBookingRequestRepository repository) : IBookingRequestQueryService
    {
        public async Task<BookingRequest?> Handle(GetBookingByIdQuery query)
            => await repository.FindByIdAsync(query.Id);

        public async Task<IEnumerable<BookingRequest>> Handle(GetBookingsByClientUserIdQuery query)
            => await repository.FindByClientUserIdAsync(query.ClientUserId);

        public async Task<IEnumerable<BookingRequest>> Handle(GetBookingsByWorkerUserIdQuery query)
            => await repository.FindByWorkerUserIdAsync(query.WorkerUserId);
    }
}
#endregion

#region Infrastructure Repositories Implementation
namespace InCleanHome.API.Booking.Infrastructure.Persistence.EFC.Repositories
{
    public class BookingRequestRepository(DbContext context)
        : BaseRepository<BookingRequest>(context), IBookingRequestRepository
    {
        public async Task<IEnumerable<BookingRequest>> FindByClientUserIdAsync(int clientUserId)
            => await Context.Set<BookingRequest>()
                .Where(b => b.ClientId == clientUserId)
                .OrderByDescending(b => b.CreatedDate)
                .ToListAsync();

        public async Task<IEnumerable<BookingRequest>> FindByWorkerUserIdAsync(int workerUserId)
            => await Context.Set<BookingRequest>()
                .Where(b => b.WorkerId == workerUserId)
                .OrderByDescending(b => b.CreatedDate)
                .ToListAsync();

        public async Task<IEnumerable<BookingRequest>> FindWorkerOverlappingAsync(
            int workerUserId, DateOnly date, string startTime, string endTime)
        {
            var activeStatuses = new[] { "pending", "accepted" };

            var dayBookings = await Context.Set<BookingRequest>()
                .Where(b =>
                    b.WorkerId == workerUserId &&
                    b.Date == date &&
                    activeStatuses.Contains(b.Status))
                .ToListAsync();

            return dayBookings.Where(b =>
                string.Compare(b.StartTime, endTime,   StringComparison.Ordinal) < 0 &&
                string.Compare(b.EndTime,   startTime, StringComparison.Ordinal) > 0);
        }
    }
}
#endregion
