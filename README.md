# BGT-Test

ASP.NET Core Web API (`net10.0`) for creating orders. Uses EF Core with SQL Server, and returns RFC 7807 `ProblemDetails` for errors.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server with `Customers`, `Products`, `Orders` and `OrderItems` tables

## Quick start

```bash
dotnet restore
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your SQL Server connection string>" --project BGT-Test
dotnet run --project BGT-Test
```

The app fails on startup if `ConnectionStrings:DefaultConnection` is not configured. Keep the connection string in user-secrets or environment variables (`ConnectionStrings__DefaultConnection`), not in `appsettings*.json`.

Default URLs (see [launchSettings.json](BGT-Test/Properties/launchSettings.json)):

| Profile | URL |
|---------|-----|
| `http`  | http://localhost:5286 |
| `https` | https://localhost:7052 |

In Development the OpenAPI document is served at `/openapi/v1.json`.

### Test data

[BGT-Test/Data/seed-test-data.sql](BGT-Test/Data/seed-test-data.sql) upserts two customers and five products that cover each response case below. It is safe to re-run, and re-running resets stock. Ready-made requests are in [BGT-Test.http](BGT-Test/BGT-Test.http) and the Postman collection.

## API

### `POST /api/orders`

Creates an order, decrements stock and returns the new order id.

Request:

```json
{
  "customerId": 1,
  "items": [
    { "productId": 1, "quantity": 2 },
    { "productId": 5, "quantity": 1 }
  ]
}
```

| Field | Rules |
|-------|-------|
| `customerId` | integer, ≥ 1 |
| `items` | required, 1–100 entries, each `productId` at most once |
| `items[].productId` | integer, ≥ 1 |
| `items[].quantity` | integer, 1–10,000 |

Success: `201 Created` with a `Location` header (`/api/orders/{orderId}`) and body `{ "orderId": 123 }`. The order starts in status `Pending`, and each line's unit price is taken from the product's current price.

Errors (all `ProblemDetails`):

| Status | Cause |
|--------|-------|
| 400 | Invalid body, or the same product appears twice in `items` |
| 404 | Customer or product not found |
| 409 | Insufficient stock |
| 422 | Product is not active |
| 500 | Unexpected error (handled by `GlobalExceptionHandler`, no stack trace is returned) |

Stock is decremented with a conditional `UPDATE` inside a transaction, so concurrent orders cannot oversell. If any line fails, the whole order is rolled back.

## Project layout

```
BGT-Test/            Web API
  Controllers/       Thin HTTP layer (OrdersController)
  Services/          Business logic (IOrderService, OrderService)
  Models/            Entities and request/response DTOs
  Data/              AppDbContext, AddInfrastructure, seed SQL
  Middleware/        GlobalExceptionHandler
BGT-Test.Tests/      xUnit tests
```

## Development

```bash
dotnet build       # must finish with zero warnings
dotnet test
dotnet format
```

Coding and architecture conventions are in [CLAUDE.md](CLAUDE.md) and [.claude/rules/](.claude/rules/).
