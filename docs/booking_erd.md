# Entity relationships

## Booking database

```mermaid
erDiagram
    ResourceTypes {
        uuid Id PK "Unique identifier of the resource type"
        string Name UK "Display name of the category, unique"
        string Description "What belongs to this category"
    }

    Resources {
        uuid Id PK "Unique identifier of the resource"
        uuid TypeId FK "Reference to the resource type"
        string Name "Display name of the resource"
        string Description "Description of the resource"
        interval MinNotice "How far ahead a booking must be made"
        interval MaxHorizon "How far into the future bookings are accepted"
        interval SlotDuration "Length of one bookable slot"
    }

    OpeningHours {
        uuid Id PK "Unique identifier of the opening window"
        uuid ResourceId FK "Reference to the resource"
        int DayOfWeek "Day of the week, 0 Sunday to 6 Saturday"
        time StartTime "UTC time the resource opens"
        time EndTime "UTC time the resource closes, later than StartTime"
    }

    Blackouts {
        uuid Id PK "Unique identifier of the blackout"
        uuid ResourceId FK "Reference to the resource"
        timestamptz StartsAt "UTC instant the closure begins, inclusive"
        timestamptz EndsAt "UTC instant the closure ends, exclusive"
        string Reason "Why the resource is unavailable"
    }

    Bookings {
        uuid Id PK "Unique identifier of the booking"
        uuid ResourceId FK "Reference to the resource"
        uuid UserId "User in the authentication database, no foreign key"
        timestamptz StartsAt "UTC instant the booking begins, inclusive"
        timestamptz EndsAt "UTC instant the booking ends, exclusive"
        string Status "Confirmed or Cancelled, stored as text"
    }

    ResourceTypes ||--o{ Resources : "categorises"
    Resources ||--o{ OpeningHours : "is open during"
    Resources ||--o{ Blackouts : "is closed during"
    Resources ||--o{ Bookings : "is booked by"
```

Deleting a `Resource` cascades to its opening hours, blackouts and bookings. Deleting a
`ResourceType` is restricted while any resource still references it.

There are no navigation properties on the model types — the relationships above exist in the
database only, expressed in code as plain `Guid` columns.

## Authentication database

Standard ASP.NET Core Identity schema (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`,
`AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins`, `AspNetUserTokens`), keyed by `uuid`.

```mermaid
erDiagram
    AspNetUsers {
        uuid Id PK "Unique identifier of the user"
        string UserName "Login name, the email address"
        string Email "Email address"
        string PhoneNumber "Contact telephone number"
        string Name "Given name, set after registration"
        string Surname "Family name, set after registration"
        timestamptz LockoutEnd "Set far in the future while blocked"
        string SecurityStamp "Refreshed on block to invalidate refresh"
    }

    AspNetRoles {
        uuid Id PK "Unique identifier of the role"
        string Name "Admin or User"
    }

    AspNetUserRoles {
        uuid UserId PK,FK "Reference to the user"
        uuid RoleId PK,FK "Reference to the role"
    }

    AspNetUsers ||--o{ AspNetUserRoles : "holds"
    AspNetRoles ||--o{ AspNetUserRoles : "granted through"
```

`Bookings.UserId` refers to `AspNetUsers.Id` across the database boundary, so it is an identifier
rather than a foreign key.
