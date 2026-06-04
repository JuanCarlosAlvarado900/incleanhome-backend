const GATEWAY_URL = "https://incleanhome-api-gateway.onrender.com";

async function probe() {
    console.log("=== PROBING DEPLOYED SERVICES ===");
    
    // 1. Probe Gateway Health
    try {
        console.log(`\nProbing Gateway Health at ${GATEWAY_URL}/health...`);
        const res = await fetch(`${GATEWAY_URL}/health`);
        console.log(`Gateway Health Status: ${res.status}`);
        console.log(`Gateway Health Response: ${await res.text()}`);
    } catch (e) {
        console.error(`Gateway probe failed: ${e.message}`);
    }

    // 2. Test making a direct request to booking-service through the gateway
    try {
        console.log(`\nTesting GET ${GATEWAY_URL}/api/bookings (should return 401 Unauthorized)...`);
        const res = await fetch(`${GATEWAY_URL}/api/bookings`);
        console.log(`Gateway -> Booking Status: ${res.status}`);
        console.log(`Gateway -> Booking Response: ${await res.text()}`);
    } catch (e) {
        console.error(`Gateway -> Booking probe failed: ${e.message}`);
    }

    // 3. Test making a direct request to payment-service through the gateway
    try {
        console.log(`\nTesting GET ${GATEWAY_URL}/api/payments/methods (should return 401 Unauthorized)...`);
        const res = await fetch(`${GATEWAY_URL}/api/payments/methods`);
        console.log(`Gateway -> Payment Status: ${res.status}`);
        console.log(`Gateway -> Payment Response: ${await res.text()}`);
    } catch (e) {
        console.error(`Gateway -> Payment probe failed: ${e.message}`);
    }
}

probe();
