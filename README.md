# booking-engine

Resource booking engine — ASP.NET Core 10, EF Core, PostgreSQL.

Administrators register bookable resources and declare when they are open; authenticated users
book slots on them. Two confirmed bookings never overlap on the same resource, because the whole
read-check-write of a booking runs inside a `SERIALIZABLE` transaction.

See [docs/CONCEPT.md](docs/CONCEPT.md) for the design and [docs/booking_erd.md](docs/booking_erd.md)
for the schema.

## Layout

```
src/
  BookingEngine.Domain           models + the pure availability calculator (no packages)
  BookingEngine.Infrastructure   EF Core contexts, configurations, migrations, Identity
  BookingEngine.ApplicationCore  availability and booking services
  BookingEngine.Api              attribute-routed controllers, middleware, composition root
  Tests/                         one xUnit project per production project
```

Dependencies run strictly top to bottom: `Api → ApplicationCore → Infrastructure → Domain`.

## Running

```bash
cd src
docker compose up -d
```

The API listens on `http://localhost:8080`; interactive docs are at
`http://localhost:8080/scalar/v1` in Development. Two PostgreSQL instances are started, one for
booking data and one for authentication.

Set `Identity__Admin__Email` and `Identity__Admin__Password` to seed an administrator on first
start; `docker-compose.yml` supplies development values.

## API

Times are UTC throughout. All identifiers are UUIDs.

### Authentication — anonymous

Standard ASP.NET Core Identity endpoints: `POST /auth/register`, `POST /auth/login`,
`POST /auth/refresh`, `GET|POST /auth/manage/info`, plus password reset and 2FA. Login returns an
opaque bearer token; send it as `Authorization: Bearer <accessToken>`.

Registration takes only an email and a password — fill in the profile afterwards with
`PATCH /users/current`.

### Catalogue — anonymous to read, administrator to change

| Route | Actions |
|---|---|
| `/resource-types` | `GET`, `GET {id}`, `POST`, `PUT {id}`, `PATCH {id}`, `DELETE {id}` |
| `/resources` | same six |
| `/opening-hours` | same six |
| `/blackouts` | same six |

Nested on a resource:

```
GET /resources/{resourceId}/opening-hours
GET /resources/{resourceId}/blackouts
GET /resources/{resourceId}/availability?from=&to=
GET /resources/{resourceId}/bookings          (administrator)
```

`availability` returns the free slots in the window, capped at 90 days. Each returned slot is
exactly what `POST /bookings` accepts as a period.

### Bookings — authenticated

```
GET    /bookings                 (administrator: everyone's)
GET    /bookings/current         (the caller's own)
GET    /bookings/{id}            (owner or administrator)
POST   /bookings
PATCH  /bookings/{id}            (reschedule, or cancel with {"status":"Cancelled"})
DELETE /bookings/{id}
```

An administrator may set `userId` to book on another user's behalf; for everyone else the booking
is placed for the caller.

### Users — administrator, except the two `current` actions

```
GET    /users
GET    /users/current            PATCH /users/current      (name, surname, phone)
GET    /users/{id}               PATCH /users/{id}
POST   /users/{userId}/block     DELETE /users/{userId}/block
POST   /users/{userId}/roles/{role}   DELETE /users/{userId}/roles/{role}
```

### Errors

Failures return `{ "traceId": …, "message": …, "detail": … }`, with `detail` populated only in
Development. `409 Conflict` means the slot is unavailable or another caller took it first;
`403 Forbidden` means the booking belongs to someone else.

## Building and testing

```bash
cd src
dotnet restore
dotnet build --no-restore -warnaserror
dotnet format --verify-no-changes
dotnet test --no-build --settings coverlet.runsettings
```

Integration tests use [Testcontainers](https://testcontainers.com/) and require a running Docker
daemon. `BookingEngine.Domain.Tests` needs nothing but xUnit — the availability calculator is pure.

Migrations are generated per context:

```bash
dotnet ef migrations add <Name> --project BookingEngine.Infrastructure \
  --startup-project BookingEngine.Api --context BookingDbContext \
  --output-dir Bookings/Migrations/Postgres
```
