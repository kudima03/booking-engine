# Concept

Booking Engine is a service for reserving time on shared resources. Administrators describe what
can be booked and when; authenticated users take slots on it. The one hard guarantee is that two
confirmed bookings never overlap on the same resource.

## Model

```
ResourceType ──< Resource ──< OpeningHours
                    │     └─< Blackout
                    └─────< Booking ──> (a user, in the other database)
```

- **ResourceType** — a category, such as "Meeting room".
- **Resource** — the bookable thing. Carries the three durations that define its grid:
  `SlotDuration` (how long one slot is), `MinNotice` (how soon from now a booking may start) and
  `MaxHorizon` (how far ahead bookings are accepted).
- **OpeningHours** — a weekly recurring window, e.g. Thursday 09:00–17:00. Never crosses
  midnight; cover an overnight period with two windows.
- **Blackout** — a one-off closure that overrides opening hours.
- **Booking** — a user holding one slot. Either `Confirmed` or `Cancelled`.

Every relationship is a plain `Guid` foreign key with **no navigation properties**, so the model
type graph has no edges and cannot contain a cycle. The same records serve as domain models, EF
entities and API response bodies; only request bodies get their own types (`*WithNoId`,
`*WithPartialUpdate`), because those genuinely differ.

## Everything is UTC

There is no timezone field and no conversion anywhere in the codebase. Opening hours are UTC wall
clock; blackouts and bookings are UTC instants stored as `timestamptz`. A caller may send a
non-UTC offset and it is normalized on the way in — Npgsql rejects a `timestamptz` parameter whose
offset is not zero, so every `DateTimeOffset` property carries a `ToUniversalTime` converter.

## Availability

`AvailabilityCalendar` in the domain layer is a pure value object: no database, no clock, no DI.
"Now" is an explicit parameter, which is why the bulk of the test suite needs nothing but xUnit.

It **slices each opening block into a fixed grid anchored at the block's start, then discards
slots overlapping a blackout or confirmed booking** — not the other way round. Subtracting busy
periods first and slicing what remains would re-anchor the grid to whatever time a blackout
happened to end:

| | slots |
|---|---|
| 09:00–11:00, 30-minute slots | 09:00, 09:30, 10:00, 10:30 |
| …with a 09:30–09:40 blackout | 09:00, **10:00**, 10:30 |
| …if we subtracted first | 09:00, ~~09:40, 10:10, 10:40~~ |

Slicing first also removes the need for interval-merge and interval-difference routines entirely.

The window is then clamped to `[now + MinNotice, now + MaxHorizon]`, and slots outside it dropped.

A slot returned by `GET /resources/{id}/availability` is exactly what `POST /bookings` accepts as
a period, so a client never computes the grid itself.

## Concurrency

**A serializable transaction is the only protection, deliberately.** There are no `xmin`
concurrency tokens and no PostgreSQL exclusion constraint.

Every booking mutation runs its whole read-check-write — load the calendar, confirm the period is
free, insert or update — inside one `SERIALIZABLE` transaction. Two callers racing for the same
slot form a read/write conflict on the same rows, and PostgreSQL's serializable snapshot isolation
aborts one of them with `40001`.

The transaction is opened **inside** the retrying execution strategy:

```csharp
IExecutionStrategy strategy = DbContext.Database.CreateExecutionStrategy();

return strategy.ExecuteAsync(async () =>
{
    DbContext.ChangeTracker.Clear();
    await using IDbContextTransaction transaction =
        await DbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    ...
});
```

- Opening it outside would throw: `EnableRetryOnFailure` installs a retrying strategy, and EF
  refuses a user transaction under one. That constraint is also what we want — the retry re-runs
  the entire availability check rather than just the final save.
- `ChangeTracker.Clear()` is mandatory on each attempt, or a retry re-sends the previous attempt's
  pending insert and writes the booking twice.
- Nothing catches `40001`. It must escape so the strategy retries it; only an exhausted retry
  budget reaches the middleware, which maps it to `409` because the caller genuinely lost.

`Npgsql`'s transient-exception detector already treats `40001` and `40P01` as retryable, so there
is no hand-written retry loop.

### What this does not cover

- **Writes that bypass the application** — a direct `psql` insert can create an overlap. A GiST
  `EXCLUDE` constraint would prevent that; it was deliberately left out.
- **Lost updates on plain CRUD** — `PATCH /resources/{id}` and friends are last-writer-wins, since
  they carry no concurrency token. This is a consequence of the same decision, not a bug.

## Authentication

ASP.NET Core Identity in its standard shape: `AddIdentityApiEndpoints` plus `MapIdentityApi`
mounted at `/auth`. That supplies register, login, refresh, password reset, email confirmation and
2FA without hand-written code. It is the one place the service uses Minimal API instead of a
controller.

**Tokens are Identity's opaque bearer tokens, not JWTs.** `AddIdentityApiEndpoints` installs
`IdentityConstants.BearerScheme` as the default authenticate scheme — nothing else may call
`AddAuthentication` with a different default, or every `[Authorize]` silently stops working.
`AddRoles` still materializes role claims, so `[Authorize(Roles = ...)]` behaves normally.

Users and roles live in **a separate database** from the booking data. `Booking.UserId` crosses
that boundary as a plain identifier with no foreign key, which is what makes the split possible.

Registration accepts only an email and a password, so `Name`, `Surname` and `PhoneNumber` are
completed afterwards through `PATCH /users/current`. `RoleAssigningUserManager` overrides
`CreateAsync` to grant the `User` role, since the packaged endpoint offers no hook.

### Blocking has a known window

Blocking sets an indefinite lockout and refreshes the security stamp, which stops both sign-in and
`/auth/refresh`. It **cannot revoke an access token already issued** — the bearer handler validates
in-process without consulting the store. Access tokens therefore expire after fifteen minutes, so a
blocked user loses access at their next refresh at the latest.

## Authorization

| Area | Anonymous | User | Admin |
|---|---|---|---|
| `/auth/*` | ✔ | ✔ | ✔ |
| Read resource types, resources, opening hours, blackouts, availability | ✔ | ✔ | ✔ |
| Write the catalogue | ✗ | ✗ | ✔ |
| Create, read, change, delete **own** bookings | ✗ | ✔ | ✔ |
| Act on **anyone's** bookings | ✗ | ✗ | ✔ |
| List, edit, block, assign roles, delete users | ✗ | ✗ | ✔ |
| Read and edit own profile | ✗ | ✔ | ✔ |

Catalogue controllers carry `[Authorize(Roles = Admin)]` at class level and open individual reads
with `[AllowAnonymous]`, so an action added later is locked down by default.

## Layering

```
Api  →  ApplicationCore  →  Infrastructure  →  Domain
```

Strictly linear and acyclic. `Domain` has no package references at all.

There are **no repositories, no unit of work, no ports and no MediatR**. Services hold
`BookingDbContext` directly, and the CRUD controllers hold it directly too. Only two things in this
domain are genuine business logic — computing availability, and the no-overlap invariant — and only
those get a service. Everything else is plumbing with no rules beyond field validation, so
wrapping it would be ceremony.

The trade-off is explicit: dependency inversion is gone, and `ApplicationCore` depends on
`Infrastructure` rather than the reverse.

## Errors

Endpoints signal failure by **throwing**; `ExceptionHandlingMiddleware` is the single place status
codes are decided.

| Thrown | Status |
|---|---|
| `BookingConflictException`, `DbUpdateConcurrencyException` | 409 |
| `PostgresException` `40001`/`40P01` (serialization, deadlock) | 409 |
| `PostgresException` `23505`/`23503` (unique, foreign key) | 409 |
| `ForbiddenException` | 403 |
| `ArgumentException` | 400 |
| `EntityNotFoundException`, `KeyNotFoundException` | 404 |
| anything else | 500 |

Responses use a fixed `ErrorResponse(TraceId, Message, Detail)` envelope, with `Detail` populated
only in Development.

## Deviations from Projector

This repository follows Projector's structure, style and pipelines, with these deliberate
differences:

- **Layer graph is linear** (`Api → ApplicationCore → Infrastructure → Domain`) rather than
  `ApplicationCore` and `Infrastructure` as siblings over `Domain`. Dropping the ports means the
  services must see EF.
- **No `Pure.Primitives`**, so no `*EFCoreModel` types and no `Materialized*` DTOs — plain BCL
  records serve all three roles.
- **Migrations are left exactly as generated**, with an `.editorconfig` block exempting them from
  the analyzers rather than reformatting them by hand.
- **`MapIdentityApi` is Minimal API**, the sole exception to the controllers-only convention.
- **No `deploy.yml`** — build/test and GHCR publish only.
