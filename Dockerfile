# Claude-Mem Worker Dockerfile
# Multi-stage build for minimal image size

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files
COPY src/ClaudeMem.Core/ClaudeMem.Core.csproj src/ClaudeMem.Core/
COPY src/ClaudeMem.Worker/ClaudeMem.Worker.csproj src/ClaudeMem.Worker/
COPY nuget.config .

# Restore dependencies
RUN dotnet restore src/ClaudeMem.Worker/ClaudeMem.Worker.csproj

# Copy source code
COPY src/ src/

# Build and publish
RUN dotnet publish src/ClaudeMem.Worker/ClaudeMem.Worker.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Create non-root user
RUN adduser --disabled-password --gecos '' appuser

# Copy published app
COPY --from=build /app/publish .

# Create data directory
RUN mkdir -p /home/appuser/.claude-mem && chown -R appuser:appuser /home/appuser

# Switch to non-root user
USER appuser

# Environment variables
ENV ASPNETCORE_URLS=http://+:37777
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

# Expose port
EXPOSE 37777

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:37777/health || exit 1

# Run the app
ENTRYPOINT ["./ClaudeMem.Worker"]
