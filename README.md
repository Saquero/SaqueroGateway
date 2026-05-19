<p align="center">
  <img src="assets/logo-saquero-gateway.svg" alt="SaqueroGateway" width="180"/>
</p>

<h1 align="center">SaqueroGateway</h1>
<p align="center">API Gateway â€” .NET 8 Â· YARP Â· JWT Validation Â· Rate Limiting Â· Correlation ID</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet" />
  <img src="https://img.shields.io/badge/YARP-2.3-blueviolet?style=flat-square" />
  <img src="https://img.shields.io/badge/Auth-JWT-green?style=flat-square" />
  <img src="https://img.shields.io/badge/Logging-Serilog-informational?style=flat-square" />
  <img src="https://img.shields.io/badge/Status-Active-success?style=flat-square" />
</p>

---

## What is SaqueroGateway?

SaqueroGateway is the single entry point for the Saquero backend ecosystem.

It acts as a production-style API Gateway that validates JWT tokens, applies rate limiting, propagates correlation IDs, logs every request and routes traffic to the correct downstream service â€” all before a single line of business logic runs.

This is not a CRUD. It is infrastructure that demonstrates real system design thinking.

---

## Ecosystem Architecture

```text
                        â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
                        â”‚   SaqueroGateway     â”‚
                        â”‚   :5100              â”‚
                        â”‚                      â”‚
                        â”‚  âœ“ JWT Validation    â”‚
                        â”‚  âœ“ Rate Limiting     â”‚
                        â”‚  âœ“ Correlation ID    â”‚
                        â”‚  âœ“ Request Logging   â”‚
                        â”‚  âœ“ Error Handling    â”‚
                        â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
                                   â”‚
              â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¼â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
              â”‚                    â”‚                    â”‚
   â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â–¼â”€â”€â”€â”€â”€â”€â”€â”€â” â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â–¼â”€â”€â”€â”€â”€â”€â”€â”€â” â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â–¼â”€â”€â”€â”€â”€â”€â”€â”€â”
   â”‚  SaqueroCloud      â”‚ â”‚ SaqueroOrderCoreâ”‚ â”‚  SaqueroJobs    â”‚
   â”‚  :5000             â”‚ â”‚ :8080           â”‚ â”‚  :5200          â”‚
   â”‚  .NET 8 + React    â”‚ â”‚ Java 21 +       â”‚ â”‚  .NET 8         â”‚
   â”‚  SaaS Platform     â”‚ â”‚ Spring Boot 3   â”‚ â”‚  Job Engine     â”‚
   â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜ â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜ â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

---

## Preview

### Gateway health check

![Health Check](assets/health-check.png)

### Downstream services monitoring

![Downstream Health](assets/downstream-health.png)

### Routing request through gateway

![Routing](assets/routing.png)

---

## Key Design Decisions

**YARP over custom proxy.** Microsoft YARP (Yet Another Reverse Proxy) is used in production .NET systems. It provides declarative route configuration, transform pipeline, and full ASP.NET Core middleware integration â€” no need to reinvent routing logic.

**JWT validation only, no issuance.** The gateway validates tokens but never creates them. Token issuance is the responsibility of SaqueroCloud. This separation mirrors real microservice auth patterns.

**Correlation ID propagates across the chain.** Every request gets a `X-Correlation-Id` header â€” generated if absent, reused if present. It appears in logs and response headers, making distributed tracing possible without a full observability stack.

**Middleware order is intentional.** Error handling wraps everything. Correlation ID is set before logging. Rate limiting runs before auth. This order mirrors production gateway pipelines.

**Downstream health is observable.** `/health/downstream` checks all three services independently and always returns 200 with per-service status â€” degraded services are visible without breaking the health endpoint itself.

---

## Tech Stack

| Technology      | Version | Role                        |
| --------------- | ------- | --------------------------- |
| .NET            | 8.0     | Runtime                     |
| C#              | 12      | Language                    |
| ASP.NET Core    | 8.0     | Web framework               |
| YARP            | 2.3.0   | Reverse proxy / routing     |
| JWT Bearer      | 8.0.0   | Token validation            |
| Serilog         | 4.x     | Structured logging          |
| Rate Limiter    | built-in| Fixed window rate limiting  |
| Health Checks   | built-in| Downstream service monitoring|

---

## Routing

| Gateway Route                    | Destination                         |
| -------------------------------- | ----------------------------------- |
| `/gateway/cloud/{**catch-all}`   | `http://localhost:5000/{path}`      |
| `/gateway/orders/{**catch-all}`  | `http://localhost:8080/{path}`      |
| `/gateway/jobs/{**catch-all}`    | `http://localhost:5200/{path}`      |

All routes require a valid JWT. Public routes (`/health`, `/health/downstream`) are anonymous.

---

## API Endpoints

| Method | Endpoint               | Auth     | Description                        |
| ------ | ---------------------- | -------- | ---------------------------------- |
| GET    | /health                | None     | Gateway health check               |
| GET    | /health/downstream     | None     | All downstream services status     |
| ANY    | /gateway/cloud/**      | JWT      | Proxy to SaqueroCloud              |
| ANY    | /gateway/orders/**     | JWT      | Proxy to SaqueroOrderCore          |
| ANY    | /gateway/jobs/**       | JWT      | Proxy to SaqueroJobs               |

---

## Getting Started

### Requirements

- .NET 8 SDK
- SaqueroCloud running on :5000 (for JWT token generation)

### Run

```bash
git clone https://github.com/Saquero/SaqueroGateway.git
cd SaqueroGateway/SaqueroGateway.Api
dotnet user-secrets set "JwtSettings:SecretKey" "your-secret-key"
dotnet run --launch-profile http
```

Gateway available at: `http://localhost:5100`

Health check: `http://localhost:5100/health`

Downstream health: `http://localhost:5100/health/downstream`

### Example Requests

```powershell
# Get a JWT token from SaqueroCloud
$token = (Invoke-RestMethod -Method POST -Uri "http://localhost:5000/api/auth/login" `
  -ContentType "application/json" `
  -Body '{"email":"admin@saquero.com","password":"Admin123!"}' ).token

# Route request through gateway to SaqueroCloud
Invoke-RestMethod -Uri "http://localhost:5100/gateway/cloud/api/subscription-plans" `
  -Headers @{ Authorization = "Bearer $token" }

# Route request through gateway to SaqueroJobs
Invoke-RestMethod -Uri "http://localhost:5100/gateway/jobs/api/jobs" `
  -Headers @{ Authorization = "Bearer $token" }

# Check downstream health
Invoke-RestMethod -Uri "http://localhost:5100/health/downstream"
```

---

## Middleware Pipeline

```text
Request
  â”‚
  â–¼
ErrorHandlingMiddleware     â† catches all unhandled exceptions
  â”‚
  â–¼
CorrelationIdMiddleware     â† assigns X-Correlation-Id
  â”‚
  â–¼
RequestLoggingMiddleware    â† logs method, path, status, duration
  â”‚
  â–¼
RateLimiter                 â† 100 req/min per IP (fixed window)
  â”‚
  â–¼
Authentication              â† validates JWT signature + claims
  â”‚
  â–¼
Authorization               â† enforces "authenticated" policy
  â”‚
  â–¼
YARP ReverseProxy           â† routes to downstream service
```

---

## Rate Limiting

| Policy | Limit        | Scope    | Rejection |
| ------ | ------------ | -------- | --------- |
| global | 100 req/min  | Per IP   | 429       |

---

## Part of the Saquero Backend Ecosystem

| Project                                                         | Stack                   | Description                                            |
| --------------------------------------------------------------- | ----------------------- | ------------------------------------------------------ |
| [SaqueroCloud](https://github.com/Saquero/SaqueroCloud)         | .NET 8 + React          | SaaS admin platform, JWT auth, subscription management |
| [SaqueroOrderCore](https://github.com/Saquero/SaqueroOrderCore) | Java 21 + Spring Boot 3 | Order lifecycle backend, DDD, Hexagonal                |
| [SaqueroJobs](https://github.com/Saquero/SaqueroJobs)           | .NET 8                  | Background job processing engine                       |
| SaqueroGateway                                                  | .NET 8                  | API Gateway â€” single entry point                       |

---

## Ecosystem Health

| Service          | Port | Health              |
| ---------------- | ---- | ------------------- |
| SaqueroGateway   | 5100 | /health âœ…          |
| SaqueroCloud     | 5000 | /health âœ…          |
| SaqueroOrderCore | 8080 | /actuator/health âœ… |
| SaqueroJobs      | 5200 | /health âœ…          |

---

## Future Improvements

- Header propagation (X-User-Id, X-Tenant-Id) to downstream services
- Per-user rate limiting using JWT claims
- Request/response transformation pipeline
- Docker Compose for full ecosystem
- Integration tests with TestContainers
- Prometheus metrics endpoint

---

<p align="center">
  <a href="https://linkedin.com/in/manusaquero">
    <img src="https://img.shields.io/badge/LinkedIn-0077B5?style=for-the-badge&logo=linkedin&logoColor=white" />
  </a>
  <a href="mailto:manusaquero@gmail.com">
    <img src="https://img.shields.io/badge/Email-D14836?style=for-the-badge&logo=gmail&logoColor=white" />
  </a>
  <a href="https://github.com/Saquero">
    <img src="https://img.shields.io/badge/GitHub-100000?style=for-the-badge&logo=github&logoColor=white" />
  </a>
</p>
