namespace InCleanHome.Shared.Contracts.Events;

public record BookingCreatedEvent
{
    public int BookingId { get; init; }
    public int ClientId { get; init; }
    public int WorkerId { get; init; }
    public decimal Amount { get; init; }
    public string ServiceType { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
}

public record PaymentProcessedEvent
{
    public int BookingId { get; init; }
    public int PaymentId { get; init; }
    public decimal Amount { get; init; }
    public string TransactionId { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
}

public record PaymentFailedEvent
{
    public int BookingId { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public record BookingCompletedEvent
{
    public int BookingId { get; init; }
    public int ClientId { get; init; }
    public int WorkerId { get; init; }
    public decimal TotalAmount { get; init; }
}

public record BookingCancelledEvent
{
    public int BookingId { get; init; }
    public int ClientId { get; init; }
    public int WorkerId { get; init; }
}
