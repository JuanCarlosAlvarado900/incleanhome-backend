FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy csproj files for restoring dependencies
COPY src/Services/Booking/InCleanHome.BookingService/InCleanHome.BookingService.csproj src/Services/Booking/InCleanHome.BookingService/
COPY src/BuildingBlocks/InCleanHome.Shared.Infrastructure/InCleanHome.Shared.Infrastructure.csproj src/BuildingBlocks/InCleanHome.Shared.Infrastructure/
COPY src/BuildingBlocks/InCleanHome.Shared.Contracts/InCleanHome.Shared.Contracts.csproj src/BuildingBlocks/InCleanHome.Shared.Contracts/

RUN dotnet restore src/Services/Booking/InCleanHome.BookingService/InCleanHome.BookingService.csproj

# Copy all source files and publish
COPY . .
WORKDIR /app/src/Services/Booking/InCleanHome.BookingService
RUN dotnet publish -c Release -o /out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /out .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "InCleanHome.BookingService.dll"]
