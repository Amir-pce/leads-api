# amirhossein.dev

Personal site and the API behind its contact form.

The site is a static page. The API takes contact-form submissions, stores them in
SQL Server, and lets me read and triage them.

```
site/            the static site (source + built dist/)
src/Api/         ASP.NET Core 10 minimal API
tests/           end-to-end smoke test
```

## Stack

- .NET 10 · ASP.NET Core minimal APIs
- Entity Framework Core 10 · SQL Server 2025
- No frontend framework — the site is hand-written HTML and CSS

## Running it

Requires the .NET 10 SDK and a reachable SQL Server. The default connection string
points at a local default instance using Windows authentication.

```bash
# one-time
dotnet tool install --global dotnet-ef
dotnet user-secrets set "Admin:Key" "<a long random string>" --project src/Api

# create the database
dotnet ef database update --project src/Api

# run
dotnet run --project src/Api
```

The API listens on `http://localhost:5223`. `GET /health` reports whether it can
reach the database.

## Endpoints

| Method | Route                     | Auth      | Purpose                         |
| ------ | ------------------------- | --------- | ------------------------------- |
| POST   | `/api/leads`              | public    | Submit the contact form         |
| GET    | `/api/admin/leads`        | admin key | List enquiries, newest first    |
| GET    | `/api/admin/leads/{id}`   | admin key | Read one enquiry in full        |
| PATCH  | `/api/admin/leads/{id}`   | admin key | Change status, add a note       |
| GET    | `/health`                 | public    | Liveness plus a database check  |

Admin routes expect the key in an `X-Admin-Key` header. The key is compared in
constant time, and an unconfigured key returns 503 rather than letting requests
through.

In development, `GET /openapi/v1.json` returns the generated OpenAPI document.

## How the form is protected

Four layers, cheapest first:

1. **Honeypot.** A hidden `website` field. A browser leaves it empty; bots fill
   every input they find. A filled field returns 202 and stores nothing — telling a
   bot it failed just makes it retry with the field cleared.
2. **Rate limit.** Five attempts per minute per IP address. The limiter runs before
   validation, so a rejected attempt still costs a permit; five leaves room for
   someone who mistypes their address and corrects it.
3. **Duplicate window.** A second enquiry from the same sender within two minutes is
   refused. The sender is identified by a SHA-256 hash of the IP, not the address
   itself — enough to spot a flood, not personal data at rest.
4. **Validation.** Length and format rules on every field, returning 400 with the
   specific field errors.

## Notes on the data model

`Leads` carries two composite indexes, both `(column, CreatedAtUtc DESC)`:

- `IX_Leads_Status_CreatedAtUtc` — the admin list is always newest-first and often
  filtered by status, so this serves both the filtered and unfiltered read with no
  sort operator in the plan.
- `IX_Leads_SenderHash_CreatedAtUtc` — the duplicate check on submit, which looks at
  one sender inside a time window.

Reads project straight into DTOs and run `AsNoTracking`, so the query selects the
columns the response needs rather than whole entities, and EF builds no change
tracker for rows nobody is going to modify.

## Tests

```bash
powershell -File tests/api-smoke.ps1
```

Twelve cases against a running instance, asserting the status code of each — the
failures as well as the successes. It wipes the `Leads` table first, so point it at a
development database only.

## Configuration

| Key                      | Where it comes from            |
| ------------------------ | ------------------------------ |
| `ConnectionStrings:Default` | `appsettings.json`, or `ConnectionStrings__Default` |
| `Admin:Key`              | user-secrets locally, `Admin__Key` in production |
| `Cors:Origins`           | `appsettings.json` — an explicit list, never `*` |

`Admin:Key` is never committed. It is empty in `appsettings.json` on purpose, and an
empty key locks the admin API rather than opening it.
