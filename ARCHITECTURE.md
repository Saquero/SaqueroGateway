# Architecture - SaqueroGateway

## Overview

SaqueroGateway is a purpose-built API Gateway. It has no domain logic, no database, and no business rules. Its sole responsibility is to act as a secure, observable, resilient entry point for the Saquero backend ecosystem.

---

## Why no Clean Architecture or DDD?

Clean Architecture and DDD exist to manage domain complexity. A gateway has no domain - it has infrastructure concerns only: routing, authentication, rate limiting, logging, resilience.

Applying Clean Architecture here would mean creating Application, Domain and Infrastructure layers with nothing meaningful to put in them. That is not architecture - it is ceremony.

The correct pattern for a gateway is a flat, well-organized infrastructure project with clear separation of concerns via middleware pipeline. That is what this project implements.

---

## Project Structure

```text
SaqueroGateway.Api/
+-- Configuration/
|   +-- JwtSettings.cs                  -- strongly-typed config with DataAnnotations
+-- Extensions/
|   +-- AuthExtensions.cs               -- AddGatewayAuth
|   +-- HealthCheckExtensions.cs        -- AddGatewayHealthChecks
|   +-- RateLimitingExtensions.cs       -- AddGatewayRateLimiting
|   +-- ResilienceExtensions.cs         -- AddGatewayResilience
+-- HealthChecks/
|   +-- DownstreamHealthCheck.cs        -- IHealthCheck with latency tracking
+-- Middleware/
|   +-- CorrelationIdMiddleware.cs      -- X-Correlation-Id generation + propagation
|   +-- ErrorHandlingMiddleware.cs      -- uniform error contract
|   +-- ClaimsForwardingMiddleware.cs   -- identity headers to downstream
|   +-- RequestLoggingMiddleware.cs     -- structured request/response logging
+-- Models/
|   +-- ErrorResponse.cs               -- uniform error response record
+-- RateLimiting/
|   +-- TenantRateLimitingPolicy.cs     -- plan-based partitioned rate limiting
+-- appsettings.json                    -- YARP routes + cluster config
+-- Program.cs                          -- composition root
```

---

## Middleware Pipeline

```text
1. ErrorHandlingMiddleware
   Wraps the entire pipeline in a try/catch.
   Returns uniform ErrorResponse JSON with correlationId on any unhandled exception.

2. CorrelationIdMiddleware
   Reads X-Correlation-Id from incoming request. Generates GUID if absent.
   Writes to HttpContext.Items and response header.
   Pushes into Serilog LogContext - every log line in the request carries it.

3. RequestLoggingMiddleware
   Stopwatch starts before next(). Logs after response:
   Method, Path, StatusCode, Duration, CorrelationId.
   Structured output - compatible with Seq, ELK, Datadog, Application Insights.

4. Authentication
   Validates JWT: signature (HS256), issuer, audience, expiry.
   Does NOT issue tokens - that is SaqueroCloud's responsibility.
   Populates HttpContext.User with claims for downstream middleware.

5. Authorization
   Enforces "authenticated" policy on all /gateway/** routes.
   Returns 401 (no token) or 403 (invalid token).

6. RateLimiter
   Runs after auth so HttpContext.User is populated with real claims.
   Partition key: {plan}:{userId} - each user has an independent counter.
   free: 30/min | premium: 200/min | admin: 500/min
   Returns structured 429 JSON on rejection, logs userId + plan.

7. YARP ReverseProxy
   Routes request to downstream cluster based on appsettings.json config.
   Strips /gateway/{service} prefix before forwarding.

   Inside YARP pipeline:
   ClaimsForwardingMiddleware
     Extracts identity from validated JWT claims.
     Forwards: X-User-Id, X-User-Email, X-User-Role, X-User-Plan,
               X-Tenant-Id, X-Forwarded-For, X-Correlation-Id.
     Downstream services trust these headers without re-validating the token.
```

---

## Tenant-Aware Rate Limiting Design

Rate limiting runs after authentication deliberately.

Before auth: only IP-based limiting is possible. IPs are spoofable and shared (NAT, proxies).
After auth: user identity is known. Each user gets their own partition regardless of IP.

The partition key is {plan}:{userId}. This means:
- Two free users never share a limit bucket.
- A premium user is never affected by a free user's traffic.
- Plan upgrades are reflected immediately on next token issuance.

```csharp
RateLimitPartition.GetFixedWindowLimiter(
    partitionKey: $"{plan}:{userId}",
    factory: _ => new FixedWindowRateLimiterOptions { ... }
)
```

---

## Modular Startup

Each infrastructure concern is isolated in a dedicated extension method.
Program.cs is a composition root - it wires, it does not implement.

```csharp
builder.Services
    .AddGatewayAuth(builder.Configuration)
    .AddGatewayRateLimiting()
    .AddGatewayResilience()
    .AddGatewayHealthChecks();
```

Adding a new infrastructure concern means adding a new extension file.
No existing code is touched. Open/Closed in practice.

---

## Resilience Pipeline

Health check HTTP clients use Microsoft.Extensions.Http.Resilience
with AddStandardResilienceHandler.

Three layers of protection:

Retry - transient failures are retried before reporting unhealthy.
  2 attempts, 200ms delay.

Circuit Breaker - if a downstream fails consistently (50% failure rate
  over 30s, minimum 3 requests), the circuit opens. For 15 seconds,
  calls fail immediately without attempting a connection.
  Prevents thread exhaustion from slow downstreams cascading into
  gateway slowdowns.

Attempt Timeout - each individual attempt has a 4-second hard timeout,
  independent of the HttpClient global timeout.

---

## JWT Strategy

| Concern           | Owner          | Reason                                      |
| ----------------- | -------------- | ------------------------------------------- |
| Token issuance    | SaqueroCloud   | Auth is a business concern, not gateway     |
| Token validation  | SaqueroGateway | Every request validated at the edge once    |
| Claims forwarding | SaqueroGateway | Downstream trusts gateway identity headers  |
| Token storage     | Client         | Stateless - no session on the gateway       |

Secret key managed via dotnet user-secrets in development.
Production: environment variable or Azure Key Vault / AWS Secrets Manager.

Algorithm: HS256 (symmetric, single issuer).
Multi-issuer production setup would use RS256 with JWKS endpoint discovery.

---

## Startup Validation

At boot, the gateway validates all required configuration before accepting traffic.
JwtSettings uses DataAnnotations with ValidateOnStart():

- SecretKey: required, minimum 32 characters
- Issuer: required
- Audience: required

Missing configuration throws InvalidOperationException at startup.
A gateway that starts without a signing key would silently accept unsigned tokens.
That is a security hole, not a degraded mode.

---

## Health Check Design

/health - gateway self-check. Returns 200 if the process is running. Version included.

/health/downstream - polls each downstream independently via resilience-backed
HttpClient. Always returns HTTP 200. Per-service status, description, latency,
and status code in body.

Why always 200? A gateway that returns 503 when a downstream is down tells load
balancers the gateway itself is unhealthy. That is incorrect - the gateway is
running fine. The body contains the full truth. HTTP status reflects gateway
health, not downstream health.

---

## Correlation ID Flow

```text
Client                    Gateway                      Downstream
  |                          |                              |
  |-- request (no corr-id) ->|                              |
  |                          |-- generate GUID              |
  |                          |   push to Serilog context    |
  |                          |                              |
  |                          |-- forward + X-Correlation-Id |
  |                          |                         ---> |
  |                          |<-- response ----------------  |
  |<-- response + header ----|                              |
  |   X-Correlation-Id: abc                                 |
```

If client sends X-Correlation-Id, gateway reuses it.
End-to-end tracing from client through gateway to all downstream services
without a full observability stack.

---

## Error Contract

All unhandled exceptions return:

```json
{
  "code": "INTERNAL_ERROR",
  "message": "An unexpected error occurred.",
  "detail": "...",
  "correlationId": "abc-123"
}
```

Downstream 4xx/5xx responses pass through as-is.
The gateway does not rewrite downstream errors.

---

## YARP Configuration

Routes and clusters are fully declarative in appsettings.json.
Adding a new downstream service requires zero code changes.

```json
"new-service-route": {
  "ClusterId": "new-service-cluster",
  "AuthorizationPolicy": "authenticated",
  "Match": { "Path": "/gateway/new-service/{**catch-all}" },
  "Transforms": [{ "PathRemovePrefix": "/gateway/new-service" }]
},
"new-service-cluster": {
  "Destinations": {
    "new-service-destination": { "Address": "http://localhost:XXXX" }
  }
}
```

---

## Future Improvements

- OpenTelemetry traces exported to Jaeger / Zipkin
- RS256 JWT with JWKS discovery (multi-issuer)
- Docker Compose for full ecosystem
- Integration tests with TestContainers
- Prometheus /metrics endpoint
- HTTPS termination