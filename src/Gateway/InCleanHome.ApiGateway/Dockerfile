FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy csproj for restoring dependencies
COPY src/Gateway/InCleanHome.ApiGateway/InCleanHome.ApiGateway.csproj src/Gateway/InCleanHome.ApiGateway/

RUN dotnet restore src/Gateway/InCleanHome.ApiGateway/InCleanHome.ApiGateway.csproj

# Copy all source files and publish
COPY . .
WORKDIR /app/src/Gateway/InCleanHome.ApiGateway
RUN dotnet publish -c Release -o /out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /out .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "InCleanHome.ApiGateway.dll"]
