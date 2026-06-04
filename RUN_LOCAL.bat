@echo off
title InCleanHome Microservices Runner
echo ==================================================
echo       INCLEANHOME MICROSERVICES LOCAL RUNNER
echo ==================================================
echo.
echo Presiona una tecla para arrancar los servicios en ventanas independientes...
echo Asegurate de tener PostgreSQL corriendo en localhost:5432 y configurar una
echo URL de RabbitMQ (ej. de CloudAMQP o local) si es necesario.
echo.
pause

:: Configurar variables de entorno locales (modifica segun tu base de datos y RabbitMQ)
set DATABASE_URL_BOOKING=Host=localhost;Port=5432;Database=booking_db;Username=postgres;Password=root
set DATABASE_URL_PAYMENT=postgresql://postgres:root@localhost:5432/payment_db
set RABBITMQ_URL=amqp://guest:guest@localhost:5672
set BOOKING_SERVICE_URL=http://localhost:8081
set JWT_SECRET=InCleanHome_SuperSecretKey_AtLeast32CharactersLongAndVerySecure_2026

echo.
echo [1/3] Arrancando InCleanHome.BookingService (Puerto 8081)...
set DATABASE_URL=%DATABASE_URL_BOOKING%
start "Booking Service" cmd /k "cd src\Services\Booking\InCleanHome.BookingService && dotnet run --urls http://localhost:8081"

echo.
echo [2/3] Arrancando InCleanHome.PaymentService (Puerto 8000)...
set DATABASE_URL=%DATABASE_URL_PAYMENT%
start "Payment Service" cmd /k "cd src\Services\Payment\InCleanHome.PaymentService && pip install -r requirements.txt && uvicorn main:app --host 127.0.0.1 --port 8000"

echo.
echo [3/3] Arrancando InCleanHome.ApiGateway (Puerto 5000)...
:: Re-mapear el destino de YARP de los contenedores a localhost en modo local
set ReverseProxy__Clusters__booking-cluster__Destinations__destination1__Address=http://localhost:8081
set ReverseProxy__Clusters__payment-cluster__Destinations__destination1__Address=http://localhost:8000
start "API Gateway (YARP)" cmd /k "cd src\Gateway\InCleanHome.ApiGateway && dotnet run --urls http://localhost:5000"

echo.
echo ==================================================
echo Todos los servicios han sido lanzados.
echo * Gateway de YARP disponible en: http://localhost:5000
echo * Swagger de Booking disponible en: http://localhost:8081/swagger
echo * Swagger de Payments disponible en: http://localhost:8000/docs
echo ==================================================
pause
