import os
import json
import threading
import time
import pika

RABBITMQ_URL = os.getenv("RABBITMQ_URL", "amqp://guest:guest@rabbitmq:5672")

def get_connection():
    # If the URL is in standard amqp format, we can parse it
    try:
        parameters = pika.URLParameters(RABBITMQ_URL)
        return pika.BlockingConnection(parameters)
    except Exception as e:
        print(f"[RABBITMQ] Failed to connect using URL {RABBITMQ_URL}: {e}")
        # Try local fallback if needed
        try:
            return pika.BlockingConnection(pika.ConnectionParameters(host="localhost"))
        except Exception:
            raise e

def publish_payment_processed(booking_id: int, payment_id: int, amount: float, transaction_id: str, channel: str):
    try:
        connection = get_connection()
        connection_channel = connection.channel()
        
        # Declare the fanout exchange matching MassTransit's namespace format
        exchange_name = "InCleanHome.Shared.Contracts.Events:PaymentProcessedEvent"
        connection_channel.exchange_declare(exchange=exchange_name, exchange_type="fanout", durable=True)
        
        # Prepare the flat raw JSON message
        payload = {
            "bookingId": booking_id,
            "paymentId": payment_id,
            "amount": float(amount),
            "transactionId": transaction_id,
            "channel": channel
        }
        
        message_body = json.dumps(payload)
        connection_channel.basic_publish(
            exchange=exchange_name,
            routing_key="",
            body=message_body,
            properties=pika.BasicProperties(
                content_type="application/json",
                content_encoding="utf-8",
                delivery_mode=2 # Make message persistent
            )
        )
        print(f"[RABBITMQ] Published PaymentProcessedEvent to {exchange_name}: {payload}")
        connection.close()
    except Exception as e:
        print(f"[RABBITMQ] Error publishing PaymentProcessedEvent: {e}")

class RabbitMQConsumerThread(threading.Thread):
    def __init__(self):
        super().__init__()
        self.daemon = True
        self.stop_event = threading.Event()
        self.connection = None
        self.channel = None

    def run(self):
        # Retry connection loop
        while not self.stop_event.is_set():
            try:
                print("[RABBITMQ CONSUMER] Connecting to RabbitMQ...")
                self.connection = get_connection()
                self.channel = self.connection.channel()
                
                # We consume from the BookingCreatedEvent exchange
                booking_created_exchange = "InCleanHome.Shared.Contracts.Events:BookingCreatedEvent"
                self.channel.exchange_declare(exchange=booking_created_exchange, exchange_type="fanout", durable=True)
                
                # Declare a queue for the Payment service
                queue_name = "incleanhome-payment-service.booking-created"
                self.channel.queue_declare(queue=queue_name, durable=True)
                
                # Bind the queue to the exchange
                self.channel.queue_bind(exchange=booking_created_exchange, queue=queue_name)
                
                def callback(ch, method, properties, body):
                    try:
                        data = json.loads(body.decode("utf-8"))
                        print(f"\n[RABBITMQ CONSUMER:RMQ] Consumed BookingCreatedEvent: {data}\n")
                    except Exception as ex:
                        print(f"[RABBITMQ CONSUMER] Error processing message: {ex}")
                    ch.basic_ack(delivery_tag=method.delivery_tag)
                
                self.channel.basic_consume(queue=queue_name, on_message_callback=callback)
                print(f"[RABBITMQ CONSUMER] Successfully listening to {booking_created_exchange}")
                
                # Start consuming
                while not self.stop_event.is_set() and self.channel.is_open:
                    self.connection.process_data_events(time_limit=1.0)
                    
            except Exception as e:
                print(f"[RABBITMQ CONSUMER] Connection error, retrying in 5 seconds... Error: {e}")
                time.sleep(5)
                
        # Clean up
        self.close_connections()

    def close_connections(self):
        try:
            if self.channel and self.channel.is_open:
                self.channel.close()
        except Exception:
            pass
        try:
            if self.connection and self.connection.is_open:
                self.connection.close()
        except Exception:
            pass

    def stop(self):
        self.stop_event.set()
        self.close_connections()

_consumer_thread = None

def start_consumer():
    global _consumer_thread
    if _consumer_thread is None or not _consumer_thread.is_alive():
        _consumer_thread = RabbitMQConsumerThread()
        _consumer_thread.start()
        print("[RABBITMQ] Consumer background thread started.")

def stop_consumer():
    global _consumer_thread
    if _consumer_thread is not None:
        _consumer_thread.stop()
        _consumer_thread.join(timeout=2.0)
        _consumer_thread = None
        print("[RABBITMQ] Consumer background thread stopped.")
