# Payment Gateway

An API that lets a merchant take a card payment through an acquiring bank and look up previous payments.

## Running it

Requires the .NET 8 SDK and Docker.

```bash
docker compose up -d                          # starts the bank simulator on port 8080
dotnet run --project src/PaymentGateway.Api   # API on https://localhost:7092
dotnet test                                   # unit and HTTP-level tests; no simulator needed
```

[`PaymentGateway.Api.http`](src/PaymentGateway.Api/PaymentGateway.Api.http) has example requests for each outcome. The simulator decides by the card number's last digit: odd is authorized, even is declined, and 0 makes the bank return 503.

## API

| Request | Response |
|---|---|
| `POST /api/payments` | `200` with the payment if the bank authorized or declined it. `400` with status `Rejected` if the request failed validation (the bank isn't called and nothing is stored). `503` if the bank couldn't be reached or failed. |
| `GET /api/payments/{id}` | `200` with the payment, or `404`. |

A payment is returned as `id`, `status` (`Authorized`, `Declined` or `Rejected`), `cardNumberLastFour`, `expiryMonth`, `expiryYear`, `currency` and `amount`. The full card number and CVV are never returned, stored or logged.

Errors are returned as [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457) with a generic message, never exception details.

## Design

```
PaymentsController → PaymentService → BankClient → bank simulator
                                    → PaymentsRepository (in memory)
```

- **Controller:** HTTP only. Maps the outcome to a status code.
- **PaymentService:** validates the request, calls the bank, stores the result.
- **PostPaymentRequest.IsValid:** the validation rules, kept with the data they check.
- **BankClient:** the only class that knows the bank's API (snake_case JSON, `MM/YYYY` expiry). Turns every bank failure into one `BankUnavailableException`.
- **GlobalExceptionHandler:** turns exceptions into ProblemDetails: 503 for the bank, 500 for anything else.

## Assumptions and decisions

- **Validation:**
  - card number is 14–19 digits
  - CVV is 3–4 digits
  - currency is GBP, USD or EUR
  - amount is a positive integer in minor units (1050 = £10.50)
  - expiry month is 1–12, and the expiry must not be in the past
- **Card expiry:** a card is valid until the end of its expiry month, so a card expiring this month is accepted.
- **Card number type:** it's a string, because it's digits, not a number, and 19 digits can be larger than a `long` holds.
- **No retries on the bank call.** A payment isn't idempotent: retrying after a lost response could charge the card twice. The call has a 10-second timeout (`BankClient:TimeoutSeconds`).
- **The bank call isn't cancelled if the merchant disconnects.** Once the request is sent, the card may be charged, so the gateway always waits for the result and records it.
- **Storage is in memory,** as in the template. Payments are lost on restart.

## What I'd do next

- **Idempotency keys** on `POST`, so a merchant can retry safely.
- **A correlation ID** on every request and log line, returned to the merchant for support queries.
- **Validation errors that say which field failed,** returned with the `Rejected` response.
- **A real database** and **merchant authentication.**
