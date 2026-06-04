const CLIENT_TOKEN = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9zaWQiOiIxMCIsImh0dHA6Ly9zY2hlbWFzLm1pY3Jvc29mdC5jb20vd3MvMjAwOC8wNi9pZGVudGl0eS9jbGFpbXMvcm9sZSI6ImNsaWVudCIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL25hbWUiOiJqdWFuQHRlc3QuY29tIiwic2lkIjoiMTAiLCJyb2xlIjoiY2xpZW50IiwiZW1haWwiOiJqdWFuQHRlc3QuY29tIiwiZXhwIjoxNzgwNjQ0OTY1fQ.5Evi5KtC37PS3z-NCyxiV8tm-7v9AtyvfAr461plM6c";
const WORKER_TOKEN = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9zaWQiOiIyMCIsImh0dHA6Ly9zY2hlbWFzLm1pY3Jvc29mdC5jb20vd3MvMjAwOC8wNi9pZGVudGl0eS9jbGFpbXMvcm9sZSI6IndvcmtlciIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL25hbWUiOiJtYXJpYUB0ZXN0LmNvbSIsInNpZCI6IjIwIiwicm9sZSI6IndvcmtlciIsImVtYWlsIjoibWFyaWFAdGVzdC5jb20iLCJleHAiOjE3ODA2NDQ5NjV9.adPg_A2cc7S8dGx01JLZRRn_8fxvvnik2gkqP7cQ7UM";

const GATEWAY_URL = "http://localhost:5000";

async function runTest() {
    console.log("=== STARTING END-TO-END INTEGRATION TEST ===");

    // Step 1: Create a Booking
    console.log("\n[1] Creating a booking request via client token...");
    const createRes = await fetch(`${GATEWAY_URL}/api/bookings`, {
        method: "POST",
        headers: {
            "Authorization": `Bearer ${CLIENT_TOKEN}`,
            "Content-Type": "application/json"
        },
        body: JSON.stringify({
            workerId: 20,
            serviceType: "Limpieza profunda de cocina",
            date: "2026-06-20",
            startTime: "08:00",
            endTime: "12:00",
            hours: 4.0,
            paymentMethodId: 1,
            address: "Av. Larco 456, Miraflores",
            notes: "Traer implementos de desinfección"
        })
    });

    if (!createRes.ok) {
        console.error("Failed to create booking:", await createRes.text());
        return;
    }

    const booking = await createRes.json();
    console.log("Booking created successfully:", booking);
    const bookingId = booking.id;

    // Step 2: Accept the Booking (as Worker)
    console.log(`\n[2] Accepting the booking ID ${bookingId} as worker...`);
    const acceptRes = await fetch(`${GATEWAY_URL}/api/bookings/${bookingId}/status`, {
        method: "PATCH",
        headers: {
            "Authorization": `Bearer ${WORKER_TOKEN}`,
            "Content-Type": "application/json"
        },
        body: JSON.stringify({ status: "accepted" })
    });

    if (!acceptRes.ok) {
        console.error("Failed to accept booking:", await acceptRes.text());
        return;
    }
    const acceptedBooking = await acceptRes.json();
    console.log("Booking accepted status:", acceptedBooking.status);

    // Step 3: Complete the Booking (as Worker)
    console.log(`\n[3] Completing the booking ID ${bookingId} as worker...`);
    const completeRes = await fetch(`${GATEWAY_URL}/api/bookings/${bookingId}/status`, {
        method: "PATCH",
        headers: {
            "Authorization": `Bearer ${WORKER_TOKEN}`,
            "Content-Type": "application/json"
        },
        body: JSON.stringify({ status: "completed" })
    });

    if (!completeRes.ok) {
        console.error("Failed to complete booking:", await completeRes.text());
        return;
    }
    const completedBooking = await completeRes.json();
    console.log("Booking status updated to:", completedBooking.status);

    // Step 4: Pay for the Booking manually (as Client)
    console.log(`\n[4] Paying for booking ID ${bookingId} via manual payment (plin) using client token...`);
    const payRes = await fetch(`${GATEWAY_URL}/api/service-payments/booking/${bookingId}/pay-manual`, {
        method: "POST",
        headers: {
            "Authorization": `Bearer ${CLIENT_TOKEN}`,
            "Content-Type": "application/json"
        },
        body: JSON.stringify({ channel: "plin" })
    });

    if (!payRes.ok) {
        console.error("Failed to pay booking:", await payRes.text());
        return;
    }
    const payment = await payRes.json();
    console.log("Payment processed successfully:", payment);

    // Wait a brief moment to allow RabbitMQ and MassTransit to process the event
    console.log("\n[5] Waiting 3 seconds for RabbitMQ integration event to propagate...");
    await new Promise(resolve => setTimeout(resolve, 3000));

    // Step 5: Check if Booking status is now "IsPaid" = true
    console.log(`\n[6] Verification: Fetching bookings for client to check 'isPaid' status...`);
    const verifyRes = await fetch(`${GATEWAY_URL}/api/bookings`, {
        method: "GET",
        headers: {
            "Authorization": `Bearer ${CLIENT_TOKEN}`
        }
    });

    if (!verifyRes.ok) {
        console.error("Failed to fetch bookings:", await verifyRes.text());
        return;
    }
    const bookings = await verifyRes.json();
    const verifiedBooking = bookings.find(b => b.id === bookingId);
    
    if (verifiedBooking) {
        console.log("Fetched booking verification details:", {
            id: verifiedBooking.id,
            status: verifiedBooking.status,
            isPaid: verifiedBooking.isPaid
        });
        
        if (verifiedBooking.isPaid) {
            console.log("\n★★★ TEST PASSED: Integration Event successfully updated the Booking Service Database! ★★★");
        } else {
            console.error("\n❌ TEST FAILED: Booking isPaid state is still false.");
        }
    } else {
        console.error(`\n❌ TEST FAILED: Booking ID ${bookingId} was not found in listing.`);
    }
}

runTest();
