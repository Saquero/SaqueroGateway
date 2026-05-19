# Architecture — SaqueroGateway

## Overview

SaqueroGateway is a purpose-built API Gateway. It has no domain logic, no database, and no business rules. Its sole responsibility is to act as a secure, observable entry point for the Saquero backend ecosystem.

This document explains the architectural decisions behind every component.

---

## Why no Clean Architecture or DDD?

Clean Architecture and DDD exist to manage domain complexity. A gateway has no domain — it has infrastructure concerns only: routing, authentication, rate limiting, logging.

Applying Clean Architecture here would mean creating Application, Domain and Infrastructure layers with nothing meaningful to put in them. That is not architecture — it is ceremony.

The correct pattern for a gateway is a flat, well-organized infrastructure project with clear separation of concerns via middleware pipeline. That is what this project implements.

---

## Project Structure

```text
SaqueroGateway/
├── SaqueroGateway.Api/
│   ├── Configuration/
│   │   └── JwtSettings.cs          -- strongly-typed config binding
│   ├── HealthChecks/
│   │   └── DownstreamHealthCheck.cs -- IHealthCheck implementation per service
│   ├── Middleware/
│   │   ├── CorrelationIdMiddleware.cs
│   │   ├── ErrorHandlingMiddleware.cs
│   │   └── RequestLoggingMiddleware.cs
│   ├── Models/
│   │   └── ErrorResponse.cs        -- uniform error contract
│   ├── appsettings.json            -- YARP routes + cluster config
│   └── Program.cs                  -- composition root
```

---

## Middleware Pipeline

Every HTTP request passes through this pipeline in order:

```text
1. ErrorHandlingMiddleware
   Wraps the entire pipeline in a try/catch.
   Any unhandled exception returns a uniform ErrorResponse JSON.
   Always runs -- positioned first so nothing escapes unhandled.

2. CorrelationIdMiddleware
   Reads X-Correlation-Id from the incoming request header.
   If absent, generates a new GUID.
   Writes it to HttpContext.Items and the response header.
   Pushes it into Serilog LogContext so every log line carries it.

3. RequestLoggingMiddleware
   Starts a Stopwatch before calling next().
   After the response is written, logs: Method, Path, StatusCode, Duration, CorrelationId.
   Structured log -- ready for any log aggregator (Seq, ELK, Datadog).

4. RateLimiter
   Fixed window: 100 requests per minute per IP.
   Runs before authentication -- unauthenticated abuse is stopped here.
   Returns 429 Too Many Requests on limit breach.

5. Authentication
   Validates JWT signature using the shared HS256 secret.
   Validates issuer, audience, and expiry.
   Does NOT issue tokens -- that is SaqueroCloud's responsibility.

6. Authorization
   Enforces the "authenticated" policy on all /gateway/** routes.
   Returns 401 if no token, 403 if token is invalid.

7. YARP ReverseProxy
   Routes the request to the correct downstream cluster.
   Strips the /gateway/{service} prefix before forwarding.
   Downstream services receive a clean request as if called directly.
```

---

## YARP Configuration

Routes and clusters are declared in `appsettings.json` -- no code changes needed to add a new service.

```json
{
  "ReverseProxy": {
    "Routes": {
      "cloud-route": {
        "ClusterId": "cloud-cluster",
        "AuthorizationPolicy": "authenticated",
        "Match": { "Path": "/gateway/cloud/{**catch-all}" },
        "Transforms": [{ "PathRemovePrefix": "/gateway/cloud" }]
      }
    },
    "Clusters": {
      "cloud-cluster": {
        "Destinations": {
          "cloud-destination": { "Address": "http://localhost:5000" }
        }
      }
    }
  }
}
```

Adding a new downstream service requires only a new route + cluster entry. Zero code changes.

---

## JWT Strategy

| Concern          | Owner          | Reason                                      |
| ---------------- | -------------- | ------------------------------------------- |
| Token issuance   | SaqueroCloud   | Auth is a business concern, not gateway     |
| Token validation | SaqueroGateway | Every request must be validated at the edge |
| Token storage    | Client         | Stateless -- no session on the gateway      |

The gateway shares the signing key with SaqueroCloud via `dotnet user-secrets` in development. In production this would be an environment variable or a secrets manager (Azure Key Vault, AWS Secrets Manager).

Algorithm: HS256 (symmetric). Production systems with multiple issuers would use RS256 with JWKS endpoint discovery.

---

## Health Check Design

`/health` -- gateway self-check. Always returns 200 if the process is running.

`/health/downstream` -- polls each downstream service independently. Always returns 200 regardless of downstream status. Per-service status is in the response body.

This design is intentional: a health endpoint that returns 503 when a downstream is down makes the gateway itself appear unhealthy to load balancers, which is incorrect. The gateway is healthy -- a downstream is not.

```json
{
  "status": "Unhealthy",
  "services": [
    {
      "name": "saquero-cloud",
      "status": "Healthy",
      "description": "SaqueroCloud is reachable."
    },
    {
      "name": "saquero-orders",
      "status": "Unhealthy",
      "description": "SaqueroOrderCore is unreachable."
    },
    {
      "name": "saquero-jobs",
      "status": "Unhealthy",
      "description": "SaqueroJobs is unreachable."
    }
  ]
}
```

---

## Correlation ID Flow

```text
Client                  Gateway                 Downstream
  |                       |                         |
  |-- GET /gateway/cloud  |                         |
  |   (no correlation id) |                         |
  |                       |-- generate GUID         |
  |                       |   add to LogContext     |
  |                       |                         |
  |                       |-- forward request ----> |
  |                       |   X-Correlation-Id: abc |
  |                       |                         |
  |                       |<-- response ----------- |
  |                       |                         |
  |<-- response ----------|                         |
  |   X-Correlation-Id: abc                         |
```

If the client sends `X-Correlation-Id`, the gateway reuses it. This allows end-to-end tracing from client to downstream across the full call chain.

---

## Error Contract

All unhandled errors return a uniform JSON structure:

```json
{
  "code": "INTERNAL_ERROR",
  "message": "An unexpected error occurred.",
  "detail": "...",
  "correlationId": "abc-123"
}
```

Downstream errors (4xx, 5xx from proxied services) are passed through as-is -- the gateway does not rewrite downstream error responses.

---

## Intentional Limitations (Portfolio Scope)

These are known gaps, documented as future work:

- **No header propagation** -- X-User-Id, X-Tenant-Id are not forwarded to downstream services yet.
- **No per-user rate limiting** -- current policy is per-IP only.
- **No request/response transformation** -- YARP supports this; not implemented.
- **No HTTPS** -- HTTP only in development. Production would terminate TLS at the gateway.
- **No integration tests** -- unit tests cover middleware; integration tests with TestContainers are planned.
- **Symmetric JWT** -- HS256 is fine for a single issuer. RS256 with JWKS would be used in production.
