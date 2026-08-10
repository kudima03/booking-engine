# booking-engine

Resource booking engine — ASP.NET Core 10, EF Core, PostgreSQL.

Administrators register bookable resources and declare when they are open; users book time slots
on them. Double-booking is prevented by running the whole read-check-write of a booking inside a
`SERIALIZABLE` transaction.

## Layout

```
src/
  BookingEngine.Domain           models + the pure availability calculator (no packages)
  BookingEngine.Infrastructure   EF Core contexts, configurations, migrations, Identity
  BookingEngine.ApplicationCore  availability and booking services
  BookingEngine.Api              attribute-routed controllers, middleware, composition root
  Tests/                         one xUnit project per production project
```

Dependencies run strictly top to bottom:
`Api → ApplicationCore → Infrastructure → Domain`.

## Running

```bash
cd src
docker compose up -d
```

The API listens on `http://localhost:8080`; interactive docs are at
`http://localhost:8080/scalar/v1` in Development.

## Building and testing

```bash
cd src
dotnet restore
dotnet build --no-restore -warnaserror
dotnet format --verify-no-changes
dotnet test --no-build --settings coverlet.runsettings
```

Integration tests use [Testcontainers](https://testcontainers.com/) and require a running Docker
daemon.
