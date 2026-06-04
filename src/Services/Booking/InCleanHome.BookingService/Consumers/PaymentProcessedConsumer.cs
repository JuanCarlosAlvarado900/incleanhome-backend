using MassTransit;
using InCleanHome.Shared.Contracts.Events;
using InCleanHome.API.Booking.Domain.Repositories;
using InCleanHome.API.Shared.Domain.Repositories;

namespace InCleanHome.BookingService.Consumers
{
    public class PaymentProcessedConsumer(
        IBookingRequestRepository repository,
        IUnitOfWork unitOfWork) : IConsumer<PaymentProcessedEvent>
    {
        public async Task Consume(ConsumeContext<PaymentProcessedEvent> context)
        {
            var eventData = context.Message;
            Console.WriteLine($"[BOOKING SERVICE:RMQ] Consumed PaymentProcessedEvent: BookingId={eventData.BookingId}, Amount={eventData.Amount}");

            var booking = await repository.FindByIdAsync(eventData.BookingId);
            if (booking == null)
            {
                Console.WriteLine($"[BOOKING SERVICE:RMQ] Booking with ID {eventData.BookingId} not found.");
                return;
            }

            if (!booking.IsPaid)
            {
                booking.ConfirmPayment();
                repository.Update(booking);
                await unitOfWork.CompleteAsync();
                Console.WriteLine($"[BOOKING SERVICE:RMQ] Booking {booking.Id} successfully marked as Paid via RabbitMQ event.");
            }
            else
            {
                Console.WriteLine($"[BOOKING SERVICE:RMQ] Booking {booking.Id} was already marked as Paid.");
            }
        }
    }
}
