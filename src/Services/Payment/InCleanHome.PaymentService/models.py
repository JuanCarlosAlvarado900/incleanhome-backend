from sqlalchemy import Column, Integer, String, Boolean, Numeric, DateTime, func
from datetime import datetime, timezone
from database import Base

class PaymentMethod(Base):
    __tablename__ = "payment_methods"

    id = Column(Integer, primary_key=True, index=True)
    user_id = Column(Integer, nullable=False, index=True)
    type = Column(String(30), nullable=False)
    label = Column(String(80), nullable=False)
    details = Column(String(200), nullable=False, default="")
    is_default = Column(Boolean, nullable=False, default=False)
    created_at = Column(DateTime(timezone=True), server_default=func.now())
    updated_at = Column(DateTime(timezone=True), server_default=func.now(), onupdate=func.now())

class ServicePayment(Base):
    __tablename__ = "service_payments"

    id = Column(Integer, primary_key=True, index=True)
    booking_id = Column(Integer, nullable=False, unique=True, index=True)
    client_id = Column(Integer, nullable=False)
    worker_id = Column(Integer, nullable=False, index=True)
    amount = Column(Numeric(10, 2), nullable=False)
    platform_fee = Column(Numeric(10, 2), nullable=False)
    worker_earning = Column(Numeric(10, 2), nullable=False)
    channel = Column(String(30), nullable=False, default="cash")
    payout_status = Column(String(20), nullable=False, default="pending", index=True) # pending, completed, not_applicable
    paid_at = Column(DateTime(timezone=True), default=lambda: datetime.now(timezone.utc))
    payout_requested_at = Column(DateTime(timezone=True), nullable=True)
    payout_completed_at = Column(DateTime(timezone=True), nullable=True)
    izipay_order_id = Column(String(100), nullable=True)
    izipay_transaction_id = Column(String(100), nullable=True)
    paypal_order_id = Column(String(100), nullable=True)
    paypal_capture_id = Column(String(100), nullable=True)
    created_at = Column(DateTime(timezone=True), server_default=func.now())
    updated_at = Column(DateTime(timezone=True), server_default=func.now(), onupdate=func.now())
