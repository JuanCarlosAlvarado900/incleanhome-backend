import os
import uuid
import httpx
from datetime import datetime, timezone
from typing import List, Optional
from decimal import Decimal

from fastapi import FastAPI, Depends, HTTPException, Header, status, Request
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field
from sqlalchemy.orm import Session
import jose.jwt

import database
import models
import rabbitmq_manager

app = FastAPI(title="InCleanHome.PaymentService", version="1.0.0")

# CORS middleware config
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

JWT_SECRET = os.getenv("JWT_SECRET", "InCleanHome_SuperSecretKey_AtLeast32CharactersLongAndVerySecure_2026")
BOOKING_SERVICE_URL = os.getenv("BOOKING_SERVICE_URL", "http://booking-service:8080")

# Startup and Shutdown events
@app.on_event("startup")
def startup_event():
    # Create database tables if they do not exist
    models.Base.metadata.create_all(bind=database.engine)
    print("[DATABASE] PostgreSQL tables verified/created successfully.")
    
    # Start RabbitMQ consumer thread
    rabbitmq_manager.start_consumer()

@app.on_event("shutdown")
def shutdown_event():
    # Stop RabbitMQ consumer thread
    rabbitmq_manager.stop_consumer()

# Helper to decode JWT and retrieve user identity
def get_current_user(authorization: str = Header(...)) -> dict:
    if not authorization.startswith("Bearer "):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid authorization header format"
        )
    token = authorization.split(" ")[1]
    try:
        payload = jose.jwt.decode(token, JWT_SECRET, algorithms=["HS256"])
        
        # Match standard .NET claims or fallback to standard JWT claim names
        user_id = payload.get("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/sid") or payload.get("sid") or payload.get("sub")
        email = payload.get("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name") or payload.get("name") or payload.get("email")
        role = payload.get("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/role") or payload.get("role")
        
        if not user_id or not role:
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="Required claims missing from token"
            )
            
        return {
            "id": int(user_id),
            "email": email,
            "role": role
        }
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail=f"Token validation failed: {str(e)}"
        )

# Pydantic schemas for PaymentMethod
class PaymentMethodRegister(BaseModel):
    type: str
    label: str
    details: str = ""
    isDefault: bool = False

class PaymentMethodResponse(BaseModel):
    id: int
    userId: int
    type: str
    label: str
    details: str
    isDefault: bool

    class Config:
        from_attributes = True

# Pydantic schemas for ServicePayment
class PayBookingManualRequest(BaseModel):
    channel: str

class ServicePaymentResponse(BaseModel):
    id: int
    bookingId: int
    clientId: int
    workerId: int
    amount: float
    platformFee: float
    workerEarning: float
    channel: str
    payoutStatus: str
    paidAt: datetime
    payoutCompletedAt: Optional[datetime] = None
    izipayOrderId: Optional[str] = None

    class Config:
        from_attributes = True

class WorkerBalanceResponse(BaseModel):
    totalEarnings: float
    platformFeeTotal: float
    netEarnings: float
    pendingPayout: float
    pendingPayoutCount: int
    completedServices: int

class IzipayCreateChargeRequest(BaseModel):
    bookingId: int

class IzipayConfirmSimulationRequest(BaseModel):
    bookingId: int
    orderId: str
    success: bool

class PaypalCreateOrderRequest(BaseModel):
    bookingId: int

class PaypalCaptureOrderRequest(BaseModel):
    bookingId: int
    orderId: str

# Helper mapper to output model in camelCase
def to_pm_response(pm: models.PaymentMethod) -> PaymentMethodResponse:
    return PaymentMethodResponse(
        id=pm.id,
        userId=pm.user_id,
        type=pm.type,
        label=pm.label,
        details=pm.details,
        isDefault=pm.is_default
    )

def to_sp_response(sp: models.ServicePayment) -> ServicePaymentResponse:
    return ServicePaymentResponse(
        id=sp.id,
        bookingId=sp.booking_id,
        clientId=sp.client_id,
        workerId=sp.worker_id,
        amount=float(sp.amount),
        platformFee=float(sp.platform_fee),
        workerEarning=float(sp.worker_earning),
        channel=sp.channel,
        payoutStatus=sp.payout_status,
        paidAt=sp.paid_at,
        payoutCompletedAt=sp.payout_completed_at,
        izipayOrderId=sp.izipay_order_id
    )

# ── API: Payment Methods ───────────────────────────────────────────────────

@app.get("/api/payments/methods", response_model=List[PaymentMethodResponse])
def list_my_payment_methods(
    current_user: dict = Depends(get_current_user),
    db: Session = Depends(database.get_db)
):
    pms = db.query(models.PaymentMethod).filter(models.PaymentMethod.user_id == current_user["id"]).all()
    return [to_pm_response(pm) for pm in pms]

@app.get("/api/payments/methods/worker/{workerId}", response_model=List[PaymentMethodResponse])
def get_worker_payment_methods(
    workerId: int,
    db: Session = Depends(database.get_db)
):
    # Publicly accessible for clients to know where to transfer
    pms = db.query(models.PaymentMethod).filter(models.PaymentMethod.user_id == workerId).all()
    return [to_pm_response(pm) for pm in pms]

@app.post("/api/payments/methods", response_model=PaymentMethodResponse)
def register_payment_method(
    resource: PaymentMethodRegister,
    current_user: dict = Depends(get_current_user),
    db: Session = Depends(database.get_db)
):
    # If set to default, clear default on other methods for this user
    if resource.isDefault:
        db.query(models.PaymentMethod).filter(
            models.PaymentMethod.user_id == current_user["id"]
        ).update({"is_default": False})

    pm = models.PaymentMethod(
        user_id=current_user["id"],
        type=resource.type,
        label=resource.label,
        details=resource.details,
        is_default=resource.isDefault
    )
    db.add(pm)
    db.commit()
    db.refresh(pm)
    return to_pm_response(pm)

@app.patch("/api/payments/methods/{pm_id}/default", response_model=PaymentMethodResponse)
def set_default_payment_method(
    pm_id: int,
    current_user: dict = Depends(get_current_user),
    db: Session = Depends(database.get_db)
):
    pm = db.query(models.PaymentMethod).filter(
        models.PaymentMethod.id == pm_id,
        models.PaymentMethod.user_id == current_user["id"]
    ).first()
    
    if not pm:
        raise HTTPException(status_code=404, detail="Payment method not found")

    # Clear other default flags
    db.query(models.PaymentMethod).filter(
        models.PaymentMethod.user_id == current_user["id"]
    ).update({"is_default": False})

    pm.is_default = True
    db.commit()
    db.refresh(pm)
    return to_pm_response(pm)

@app.delete("/api/payments/methods/{pm_id}", status_code=204)
def delete_payment_method(
    pm_id: int,
    current_user: dict = Depends(get_current_user),
    db: Session = Depends(database.get_db)
):
    pm = db.query(models.PaymentMethod).filter(
        models.PaymentMethod.id == pm_id,
        models.PaymentMethod.user_id == current_user["id"]
    ).first()
    
    if not pm:
        raise HTTPException(status_code=404, detail="Payment method not found")

    db.delete(pm)
    db.commit()
    return

# ── API: Service Payments (Manual/Gateway) ──────────────────────────────────

# Sync checker to call Booking service
async def fetch_booking_info(booking_id: int) -> dict:
    url = f"{BOOKING_SERVICE_URL}/api/bookings/{booking_id}/payment-info"
    async with httpx.AsyncClient() as client:
        try:
            response = await client.get(url, timeout=5.0)
            if response.status_code == 404:
                raise HTTPException(status_code=404, detail="Booking not found in Booking Service")
            response.raise_for_status()
            return response.json()
        except httpx.HTTPError as e:
            raise HTTPException(status_code=400, detail=f"Booking validation failed: {str(e)}")

async def notify_booking_payment(booking_id: int):
    url = f"{BOOKING_SERVICE_URL}/api/bookings/{booking_id}/confirm-payment"
    async with httpx.AsyncClient() as client:
        try:
            response = await client.post(url, timeout=5.0)
            response.raise_for_status()
        except httpx.HTTPError as e:
            print(f"[HTTP WARNING] Failed to notify Booking Service directly: {e}")

@app.post("/api/service-payments/booking/{bookingId}/pay-manual", response_model=ServicePaymentResponse)
async def pay_booking_manual(
    bookingId: int,
    body: PayBookingManualRequest,
    current_user: dict = Depends(get_current_user),
    db: Session = Depends(database.get_db)
):
    if current_user["role"] != "client":
        raise HTTPException(status_code=403, detail="Only clients can pay bookings")

    channel = body.channel.lower()
    valid_manual_channels = ["yape", "plin", "bank_transfer", "cash"]
    if channel not in valid_manual_channels:
        raise HTTPException(status_code=400, detail="Invalid manual payment channel. For cards use izipay flow.")

    # Validate with booking service
    booking_info = await fetch_booking_info(bookingId)
    
    if booking_info["clientId"] != current_user["id"]:
        raise HTTPException(status_code=403, detail="This booking does not belong to you")
    if booking_info["status"] != "completed":
        raise HTTPException(status_code=400, detail="Booking must be completed before payment")

    # Check for existing payment
    existing = db.query(models.ServicePayment).filter(models.ServicePayment.booking_id == bookingId).first()
    if existing:
        raise HTTPException(status_code=400, detail="This booking has already been paid")

    amount = Decimal(str(booking_info["totalAmount"]))
    
    # Calculate fee (0% for cash, 10% for others)
    if channel == "cash":
        fee = Decimal("0.00")
    else:
        fee = round(amount * Decimal("0.10"), 2)
    earning = amount - fee

    payment = models.ServicePayment(
        booking_id=bookingId,
        client_id=current_user["id"],
        worker_id=booking_info["workerId"],
        amount=amount,
        platform_fee=fee,
        worker_earning=earning,
        channel=channel,
        payout_status="not_applicable",
        paid_at=datetime.now(timezone.utc)
    )
    
    db.add(payment)
    db.commit()
    db.refresh(payment)

    # Sync notification call (fail-safe)
    await notify_booking_payment(bookingId)

    # Publish to RabbitMQ
    rabbitmq_manager.publish_payment_processed(
        booking_id=bookingId,
        payment_id=payment.id,
        amount=float(amount),
        transaction_id=f"manual-{payment.id}",
        channel=channel
    )

    return to_sp_response(payment)

@app.get("/api/service-payments/booking/{bookingId}", response_model=ServicePaymentResponse)
def get_service_payment_by_booking(
    bookingId: int,
    current_user: dict = Depends(get_current_user),
    db: Session = Depends(database.get_db)
):
    payment = db.query(models.ServicePayment).filter(models.ServicePayment.booking_id == bookingId).first()
    if not payment:
        raise HTTPException(status_code=404, detail="Payment not found")

    if payment.client_id != current_user["id"] and payment.worker_id != current_user["id"] and current_user["role"] != "admin":
        raise HTTPException(status_code=403, detail="Forbidden")

    return to_sp_response(payment)

@app.get("/api/service-payments/worker", response_model=List[ServicePaymentResponse])
def list_my_worker_payments(
    current_user: dict = Depends(get_current_user),
    db: Session = Depends(database.get_db)
):
    if current_user["role"] != "worker":
        raise HTTPException(status_code=403, detail="Workers only")

    payments = db.query(models.ServicePayment).filter(
        models.ServicePayment.worker_id == current_user["id"]
    ).order_by(models.ServicePayment.paid_at.desc()).all()
    
    return [to_sp_response(p) for p in payments]

@app.get("/api/service-payments/worker/balance", response_model=WorkerBalanceResponse)
def get_worker_balance(
    current_user: dict = Depends(get_current_user),
    db: Session = Depends(database.get_db)
):
    if current_user["role"] != "worker":
        raise HTTPException(status_code=403, detail="Workers only")

    payments = db.query(models.ServicePayment).filter(
        models.ServicePayment.worker_id == current_user["id"]
    ).all()

    total_earnings = sum(float(p.amount) for p in payments)
    platform_fee_total = sum(float(p.platform_fee) for p in payments)
    net_earnings = sum(float(p.worker_earning) for p in payments)
    
    pending_payouts = [p for p in payments if p.payout_status == "pending"]
    pending_payout = sum(float(p.worker_earning) for p in pending_payouts)
    pending_payout_count = len(pending_payouts)
    completed_services = len(payments)

    return WorkerBalanceResponse(
        totalEarnings=total_earnings,
        platformFeeTotal=platform_fee_total,
        netEarnings=net_earnings,
        pendingPayout=pending_payout,
        pendingPayoutCount=pending_payout_count,
        completedServices=completed_services
    )

@app.post("/api/service-payments/worker/request-payout")
def request_worker_payout(
    current_user: dict = Depends(get_current_user),
    db: Session = Depends(database.get_db)
):
    if current_user["role"] != "worker":
        raise HTTPException(status_code=403, detail="Workers only")

    pending = db.query(models.ServicePayment).filter(
        models.ServicePayment.worker_id == current_user["id"],
        models.ServicePayment.payout_status == "pending"
    ).all()

    now = datetime.now(timezone.utc)
    for p in pending:
        p.payout_status = "completed"
        p.payout_requested_at = now
        p.payout_completed_at = now

    db.commit()
    return {"payoutsProcessed": len(pending)}

# ── API: Izipay Sandbox Card Payment ──────────────────────────────────────────

@app.post("/api/payments/izipay/create-charge")
async def izipay_create_charge(
    body: IzipayCreateChargeRequest,
    current_user: dict = Depends(get_current_user),
    db: Session = Depends(database.get_db)
):
    # Validate booking info via HTTP
    booking_info = await fetch_booking_info(body.bookingId)
    
    if booking_info["clientId"] != current_user["id"]:
        raise HTTPException(status_code=403, detail="This booking does not belong to you")
    if booking_info["status"] != "completed":
        raise HTTPException(status_code=400, detail="Booking must be completed before payment")

    existing = db.query(models.ServicePayment).filter(models.ServicePayment.booking_id == body.bookingId).first()
    if existing:
        raise HTTPException(status_code=400, detail="This booking has already been paid")

    order_id = f"izp-{body.bookingId}-{uuid.uuid4().hex[:8]}"
    amount = float(booking_info["totalAmount"])

    return {
        "formToken": f"dummy-form-token-{uuid.uuid4().hex}",
        "publicKey": "dummy-public-key",
        "endpoint": "https://api.mic.izipay.pe",
        "orderId": order_id,
        "amount": amount,
        "simulated": True  # Force frontend to show simulator buttons
    }

@app.post("/api/payments/izipay/confirm-simulation", response_model=ServicePaymentResponse)
async def izipay_confirm_simulation(
    body: IzipayConfirmSimulationRequest,
    current_user: dict = Depends(get_current_user),
    db: Session = Depends(database.get_db)
):
    if current_user["role"] != "client":
        raise HTTPException(status_code=403, detail="Only clients can pay bookings")

    if not body.success:
        raise HTTPException(status_code=400, detail="Payment rejected in simulation")

    booking_info = await fetch_booking_info(body.bookingId)
    
    if booking_info["clientId"] != current_user["id"]:
        raise HTTPException(status_code=403, detail="This booking does not belong to you")
    if booking_info["status"] != "completed":
        raise HTTPException(status_code=400, detail="Booking must be completed before payment")

    existing = db.query(models.ServicePayment).filter(models.ServicePayment.booking_id == body.bookingId).first()
    if existing:
        raise HTTPException(status_code=400, detail="This booking has already been paid")

    amount = Decimal(str(booking_info["totalAmount"]))
    fee = round(amount * Decimal("0.10"), 2)
    earning = amount - fee
    txn_id = f"txn-izp-{uuid.uuid4().hex[:12]}"

    payment = models.ServicePayment(
        booking_id=body.bookingId,
        client_id=current_user["id"],
        worker_id=booking_info["workerId"],
        amount=amount,
        platform_fee=fee,
        worker_earning=earning,
        channel="izipay_card",
        payout_status="pending", # Izipay card payments need payout
        izipay_order_id=body.orderId,
        izipay_transaction_id=txn_id,
        paid_at=datetime.now(timezone.utc)
    )

    db.add(payment)
    db.commit()
    db.refresh(payment)

    # Sync notification call (fail-safe)
    await notify_booking_payment(body.bookingId)

    # Publish to RabbitMQ
    rabbitmq_manager.publish_payment_processed(
        booking_id=body.bookingId,
        payment_id=payment.id,
        amount=float(amount),
        transaction_id=txn_id,
        channel="izipay_card"
    )

    return to_sp_response(payment)

# ── API: PayPal Gateway Payment ───────────────────────────────────────────────

@app.post("/api/payments/paypal/create-order")
async def paypal_create_order(
    request: Request,
    body: PaypalCreateOrderRequest,
    current_user: dict = Depends(get_current_user),
    db: Session = Depends(database.get_db)
):
    booking_info = await fetch_booking_info(body.bookingId)
    
    if booking_info["clientId"] != current_user["id"]:
        raise HTTPException(status_code=403, detail="This booking does not belong to you")
    if booking_info["status"] != "completed":
        raise HTTPException(status_code=400, detail="Booking must be completed before payment")

    existing = db.query(models.ServicePayment).filter(models.ServicePayment.booking_id == body.bookingId).first()
    if existing:
        raise HTTPException(status_code=400, detail="This booking has already been paid")

    order_id = f"PAY-{uuid.uuid4().hex[:12].upper()}"
    
    # Dynamically find the origin from the request headers to construct the callback URL.
    # Fallback to local Vite dev server port.
    referer = request.headers.get("referer") or request.headers.get("origin")
    if referer:
        # Strip trailing slash if present
        origin_url = referer.rstrip("/")
    else:
        origin_url = "http://localhost:5173"
        
    # The success page handles triggering the capture order on mount.
    approve_link = f"{origin_url}/client/payment-success?token={order_id}"

    return {
        "orderId": order_id,
        "approveLink": approve_link
    }

@app.post("/api/payments/paypal/capture-order")
async def paypal_capture_order(
    body: PaypalCaptureOrderRequest,
    current_user: dict = Depends(get_current_user),
    db: Session = Depends(database.get_db)
):
    if current_user["role"] != "client":
        raise HTTPException(status_code=403, detail="Only clients can pay bookings")

    booking_info = await fetch_booking_info(body.bookingId)
    
    if booking_info["clientId"] != current_user["id"]:
        raise HTTPException(status_code=403, detail="This booking does not belong to you")
    if booking_info["status"] != "completed":
        raise HTTPException(status_code=400, detail="Booking must be completed before payment")

    existing = db.query(models.ServicePayment).filter(models.ServicePayment.booking_id == body.bookingId).first()
    if existing:
        raise HTTPException(status_code=400, detail="This booking has already been paid")

    amount = Decimal(str(booking_info["totalAmount"]))
    fee = round(amount * Decimal("0.10"), 2)
    earning = amount - fee
    capture_id = f"cap-{uuid.uuid4().hex[:12]}"

    payment = models.ServicePayment(
        booking_id=body.bookingId,
        client_id=current_user["id"],
        worker_id=booking_info["workerId"],
        amount=amount,
        platform_fee=fee,
        worker_earning=earning,
        channel="paypal",
        payout_status="pending", # PayPal payments need payout simulation too
        paypal_order_id=body.orderId,
        paypal_capture_id=capture_id,
        paid_at=datetime.now(timezone.utc)
    )

    db.add(payment)
    db.commit()
    db.refresh(payment)

    # Sync notification call (fail-safe)
    await notify_booking_payment(body.bookingId)

    # Publish to RabbitMQ
    rabbitmq_manager.publish_payment_processed(
        booking_id=body.bookingId,
        payment_id=payment.id,
        amount=float(amount),
        transaction_id=capture_id,
        channel="paypal"
    )

    return {
        "captureId": capture_id,
        "amount": float(amount)
    }
