using System.Globalization;
using System.Net.Mime;
using InCleanHome.API.Booking.Domain.Model.Commands;
using InCleanHome.API.Booking.Domain.Model.Queries;
using InCleanHome.API.Booking.Domain.Model.ValueObjects;
using InCleanHome.API.Booking.Domain.Services;
using InCleanHome.API.Booking.Interfaces.REST.Resources;
using InCleanHome.API.Booking.Interfaces.REST.Transform;
using InCleanHome.API.IAM.Domain.Model.Aggregates;
using InCleanHome.API.Profiles.Interfaces.ACL;
using InCleanHome.API.Shared.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Microsoft.AspNetCore.Authorization;
using InCleanHome.API.Booking.Domain.Repositories;

namespace InCleanHome.API.Booking.Interfaces.REST.Resources
{
    public record CreateBookingResource(
        int WorkerId,
        string ServiceType,
        string Date,           // "yyyy-MM-dd"
        string StartTime,      // "HH:mm"
        string EndTime,        // "HH:mm"
        decimal Hours,
        int PaymentMethodId,
        string Address,
        string? Notes);

    public record UpdateBookingStatusResource(string Status);

    public record RescheduleBookingResource(
        DateOnly Date,
        string StartTime,
        string EndTime,
        decimal Hours);

    public record BookingResource(
        int Id,
        int ClientId,
        int WorkerId,
        string ClientName,
        string WorkerName,
        string? WorkerPhotoUrl,
        string? ClientPhotoUrl,
        string ServiceType,
        string Date,
        string StartTime,
        string EndTime,
        decimal Hours,
        int PaymentMethodId,
        string Address,
        string Notes,
        decimal HourlyRate,
        decimal TotalAmount,
        decimal PlatformFee,
        decimal WorkerEarning,
        string Status,
        bool IsPaid,
        DateTimeOffset? CreatedAt);
}

namespace InCleanHome.API.Booking.Interfaces.REST.Transform
{
    using InCleanHome.API.Booking.Domain.Model.Aggregates;
    
    public static class BookingResourceFromEntityAssembler
    {
        public static BookingResource ToResourceFromEntity(BookingRequest b, string clientName, string workerName,
            string? workerPhotoUrl = null, string? clientPhotoUrl = null, bool isPaid = false)
            => new(
                b.Id,
                b.ClientId,
                b.WorkerId,
                clientName,
                workerName,
                workerPhotoUrl,
                clientPhotoUrl,
                b.ServiceType,
                b.Date.ToString("yyyy-MM-dd"),
                b.StartTime,
                b.EndTime,
                b.Hours,
                b.PaymentMethodId,
                b.Address,
                b.Notes,
                b.HourlyRate,
                b.TotalAmount,
                b.PlatformFee,
                b.WorkerEarning,
                b.Status,
                isPaid,
                b.CreatedDate);
    }
}

namespace InCleanHome.API.Booking.Interfaces.REST.Controllers
{
    using InCleanHome.API.Booking.Domain.Model.Aggregates;

    [ApiController]
    [Route("api/bookings")]
    [Produces(MediaTypeNames.Application.Json)]
    [SwaggerTag("Bookings — full hiring lifecycle")]
    public class BookingsController(
        IBookingRequestCommandService commandService,
        IBookingRequestQueryService queryService,
        IProfilesContextFacade profilesFacade,
        IBookingRequestRepository repository,
        IUnitOfWork unitOfWork) : ControllerBase
    {
        [HttpPost]
        [SwaggerOperation("Create Booking", "A client creates a booking against a worker.")]
        public async Task<IActionResult> Create([FromBody] CreateBookingResource resource)
        {
            var current = (User?)HttpContext.Items["User"];
            if (current is null) return Unauthorized();
            if (current.Role != UserRole.Client) return Forbid();

            if (!DateOnly.TryParseExact(resource.Date, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return BadRequest(new { error = "Invalid date format (expected yyyy-MM-dd)" });

            try
            {
                var booking = await commandService.Handle(new CreateBookingCommand(
                    current.Id, resource.WorkerId, resource.ServiceType, date,
                    resource.StartTime, resource.EndTime, resource.Hours, resource.PaymentMethodId,
                    resource.Address, resource.Notes));

                var clientName = await profilesFacade.FetchUserNameByUserId(booking.ClientId);
                var workerName = await profilesFacade.FetchUserNameByUserId(booking.WorkerId);
                var workerPhoto = await profilesFacade.FetchWorkerPhotoByUserId(booking.WorkerId);
                var clientPhoto = await profilesFacade.FetchClientPhotoByUserId(booking.ClientId);
                return Ok(BookingResourceFromEntityAssembler.ToResourceFromEntity(booking, clientName, workerName, workerPhoto, clientPhoto, booking.IsPaid));
            }
            catch (Exception e)
            {
                return BadRequest(new { error = e.Message });
            }
        }

        [HttpGet]
        [SwaggerOperation("List My Bookings", "Returns bookings for the current user (client or worker view).")]
        public async Task<IActionResult> ListMine()
        {
            var current = (User?)HttpContext.Items["User"];
            if (current is null) return Unauthorized();

            var bookings = current.Role switch
            {
                UserRole.Worker => await queryService.Handle(new GetBookingsByWorkerUserIdQuery(current.Id)),
                UserRole.Client => await queryService.Handle(new GetBookingsByClientUserIdQuery(current.Id)),
                _               => Enumerable.Empty<BookingRequest>()
            };

            var result = new List<BookingResource>();
            foreach (var b in bookings)
            {
                var clientName = await profilesFacade.FetchUserNameByUserId(b.ClientId);
                var workerName = await profilesFacade.FetchUserNameByUserId(b.WorkerId);
                var workerPhoto = await profilesFacade.FetchWorkerPhotoByUserId(b.WorkerId);
                var clientPhoto = await profilesFacade.FetchClientPhotoByUserId(b.ClientId);
                result.Add(BookingResourceFromEntityAssembler.ToResourceFromEntity(b, clientName, workerName, workerPhoto, clientPhoto, b.IsPaid));
            }
            return Ok(result);
        }

        [HttpPatch("{id:int}/status")]
        [SwaggerOperation("Update Booking Status", "Transitions a booking through its lifecycle (accepted/rejected/cancelled/completed).")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateBookingStatusResource resource)
        {
            var current = (User?)HttpContext.Items["User"];
            if (current is null) return Unauthorized();

            if (!BookingStatus.IsValid(resource.Status))
                return BadRequest(new { error = "Invalid status" });

            try
            {
                var booking = await commandService.Handle(
                    new UpdateBookingStatusCommand(id, current.Id, current.Role, resource.Status));
                if (booking is null) return NotFound();

                var clientName = await profilesFacade.FetchUserNameByUserId(booking.ClientId);
                var workerName = await profilesFacade.FetchUserNameByUserId(booking.WorkerId);
                var workerPhoto = await profilesFacade.FetchWorkerPhotoByUserId(booking.WorkerId);
                var clientPhoto = await profilesFacade.FetchClientPhotoByUserId(booking.ClientId);
                return Ok(BookingResourceFromEntityAssembler.ToResourceFromEntity(booking, clientName, workerName, workerPhoto, clientPhoto, booking.IsPaid));
            }
            catch (UnauthorizedAccessException e) { return StatusCode(403, new { error = e.Message }); }
            catch (Exception e)                   { return BadRequest(new { error = e.Message }); }
        }

        [HttpGet("{id:int}/receipt")]
        [SwaggerOperation("Get Booking Receipt", "Returns the receipt data for a completed booking.")]
        public async Task<IActionResult> GetReceipt(int id)
        {
            var current = (User?)HttpContext.Items["User"];
            if (current is null) return Unauthorized();

            var booking = await queryService.Handle(new GetBookingByIdQuery(id));
            if (booking is null) return NotFound();

            if (booking.ClientId != current.Id && booking.WorkerId != current.Id)
                return Forbid();

            if (booking.Status != BookingStatus.Completed)
                return BadRequest(new { error = "Receipt is only available for completed services." });

            var clientName = await profilesFacade.FetchUserNameByUserId(booking.ClientId);
            var workerName = await profilesFacade.FetchUserNameByUserId(booking.WorkerId);
            var workerPhoto = await profilesFacade.FetchWorkerPhotoByUserId(booking.WorkerId);
            var clientPhoto = await profilesFacade.FetchClientPhotoByUserId(booking.ClientId);
            return Ok(BookingResourceFromEntityAssembler.ToResourceFromEntity(booking, clientName, workerName, workerPhoto, clientPhoto, booking.IsPaid));
        }

        [HttpPatch("{id:int}/reschedule")]
        [SwaggerOperation("Reschedule Booking", "Moves a pending or accepted booking to a new date/time.")]
        public async Task<IActionResult> Reschedule(int id, [FromBody] RescheduleBookingResource body)
        {
            var current = (User?)HttpContext.Items["User"];
            if (current is null) return Unauthorized();
            try
            {
                var updated = await commandService.Handle(new RescheduleBookingCommand(
                    id, current.Id, body.Date, body.StartTime, body.EndTime, body.Hours));
                if (updated is null) return NotFound();
                var clientName = await profilesFacade.FetchUserNameByUserId(updated.ClientId);
                var workerName = await profilesFacade.FetchUserNameByUserId(updated.WorkerId);
                var workerPhoto = await profilesFacade.FetchWorkerPhotoByUserId(updated.WorkerId);
                var clientPhoto = await profilesFacade.FetchClientPhotoByUserId(updated.ClientId);
                return Ok(BookingResourceFromEntityAssembler.ToResourceFromEntity(updated, clientName, workerName, workerPhoto, clientPhoto, updated.IsPaid));
            }
            catch (UnauthorizedAccessException e) { return StatusCode(403, new { error = e.Message }); }
            catch (Exception e)                   { return BadRequest(new { error = e.Message }); }
        }

        #region Microservice-specific integration endpoints (REST/HTTP)
        
        [HttpGet("{id:int}/payment-info")]
        [AllowAnonymous]
        [SwaggerOperation("Get Booking Info for Payment", "Returns amount and validation properties needed by the Payment Service.")]
        public async Task<IActionResult> GetPaymentInfo(int id)
        {
            var booking = await queryService.Handle(new GetBookingByIdQuery(id));
            if (booking is null) return NotFound(new { error = "Booking not found" });

            return Ok(new
            {
                bookingId = booking.Id,
                clientId = booking.ClientId,
                workerId = booking.WorkerId,
                totalAmount = booking.TotalAmount,
                status = booking.Status
            });
        }

        [HttpPost("{id:int}/confirm-payment")]
        [AllowAnonymous]
        [SwaggerOperation("Confirm Booking Payment", "Marks the booking as paid. Called by the Payment Service.")]
        public async Task<IActionResult> ConfirmPayment(int id)
        {
            var booking = await repository.FindByIdAsync(id);
            if (booking is null) return NotFound(new { error = "Booking not found" });

            booking.ConfirmPayment();
            repository.Update(booking);
            await unitOfWork.CompleteAsync();

            Console.WriteLine($"[BOOKING SERVICE] Confirmed payment for booking {id}. Status updated to Paid.");
            return Ok(new { message = "Booking payment confirmed successfully", bookingId = id, isPaid = true });
        }
        
        #endregion
    }
}
