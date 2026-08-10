using BookingEngine.Domain.Availability;
using BookingEngine.Domain.Models;

namespace BookingEngine.Domain.Tests.Availability;

public sealed record AvailabilityCalendarTests
{
    // Thursday 2026-01-01T00:00:00Z. Every case is anchored here so the weekday is explicit.
    private static readonly DateTimeOffset Thursday = new(
        2026,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero
    );

    private static readonly Guid ResourceId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111"
    );

    private static Resource NewResource(
        TimeSpan? slotDuration = null,
        TimeSpan? minNotice = null,
        TimeSpan? maxHorizon = null
    )
    {
        return new Resource(
            ResourceId,
            Guid.Empty,
            "Room A",
            string.Empty,
            minNotice ?? TimeSpan.Zero,
            maxHorizon ?? TimeSpan.FromDays(365),
            slotDuration ?? TimeSpan.FromMinutes(30)
        );
    }

    private static OpeningHours NewOpeningHours(
        DayOfWeek dayOfWeek = DayOfWeek.Thursday,
        string startTime = "09:00",
        string endTime = "11:00"
    )
    {
        return new OpeningHours(
            Guid.NewGuid(),
            ResourceId,
            dayOfWeek,
            TimeOnly.Parse(startTime, null),
            TimeOnly.Parse(endTime, null)
        );
    }

    private static AvailabilityCalendar NewCalendar(
        Resource? resource = null,
        IReadOnlyCollection<OpeningHours>? openingHours = null,
        IReadOnlyCollection<Blackout>? blackouts = null,
        IReadOnlyCollection<Booking>? bookings = null
    )
    {
        return new AvailabilityCalendar(
            resource ?? NewResource(),
            openingHours ?? [NewOpeningHours()],
            blackouts ?? [],
            bookings ?? []
        );
    }

    private static DateTimeOffset At(string time)
    {
        return Thursday.Add(TimeOnly.Parse(time, null).ToTimeSpan());
    }

    private static IReadOnlyCollection<string> Starts(
        IReadOnlyCollection<AvailabilitySlot> slots
    )
    {
        return [.. slots.Select(x => x.StartsAt.ToString("HH:mm", null))];
    }

    [Fact]
    public void ShouldSliceOpeningBlockIntoFixedGridWhenNothingIsBusy()
    {
        IReadOnlyCollection<AvailabilitySlot> slots = NewCalendar()
            .Slots(Thursday, Thursday.AddDays(1), Thursday);

        Assert.Equal(["09:00", "09:30", "10:00", "10:30"], Starts(slots));
    }

    [Fact]
    public void ShouldKeepGridAnchoredToBlockStartWhenBlackoutIsNotSlotAligned()
    {
        // A blackout ending at 09:40 must not re-anchor the grid to 09:40.
        Blackout blackout = new(
            Guid.NewGuid(),
            ResourceId,
            At("09:30"),
            At("09:40"),
            "Cleaning"
        );

        IReadOnlyCollection<AvailabilitySlot> slots = NewCalendar(blackouts: [blackout])
            .Slots(Thursday, Thursday.AddDays(1), Thursday);

        Assert.Equal(["09:00", "10:00", "10:30"], Starts(slots));
    }

    [Fact]
    public void ShouldDropSlotsOverlappingConfirmedBooking()
    {
        Booking booking = new(
            Guid.NewGuid(),
            ResourceId,
            Guid.NewGuid(),
            At("10:00"),
            At("10:30"),
            BookingStatus.Confirmed
        );

        IReadOnlyCollection<AvailabilitySlot> slots = NewCalendar(bookings: [booking])
            .Slots(Thursday, Thursday.AddDays(1), Thursday);

        Assert.Equal(["09:00", "09:30", "10:30"], Starts(slots));
    }

    [Fact]
    public void ShouldIgnoreCancelledBookings()
    {
        Booking booking = new(
            Guid.NewGuid(),
            ResourceId,
            Guid.NewGuid(),
            At("10:00"),
            At("10:30"),
            BookingStatus.Cancelled
        );

        IReadOnlyCollection<AvailabilitySlot> slots = NewCalendar(bookings: [booking])
            .Slots(Thursday, Thursday.AddDays(1), Thursday);

        Assert.Equal(["09:00", "09:30", "10:00", "10:30"], Starts(slots));
    }

    [Fact]
    public void ShouldTreatAdjacentBookingsAsNonOverlapping()
    {
        // A booking ending exactly when a slot starts leaves that slot free.
        Booking booking = new(
            Guid.NewGuid(),
            ResourceId,
            Guid.NewGuid(),
            At("09:00"),
            At("09:30"),
            BookingStatus.Confirmed
        );

        IReadOnlyCollection<AvailabilitySlot> slots = NewCalendar(bookings: [booking])
            .Slots(Thursday, Thursday.AddDays(1), Thursday);

        Assert.Equal(["09:30", "10:00", "10:30"], Starts(slots));
    }

    [Fact]
    public void ShouldClampWindowStartToMinimumNotice()
    {
        Resource resource = NewResource(minNotice: TimeSpan.FromHours(10));

        IReadOnlyCollection<AvailabilitySlot> slots = NewCalendar(resource)
            .Slots(Thursday, Thursday.AddDays(1), Thursday);

        Assert.Equal(["10:00", "10:30"], Starts(slots));
    }

    [Fact]
    public void ShouldClampWindowEndToMaximumHorizon()
    {
        Resource resource = NewResource(maxHorizon: TimeSpan.FromHours(10));

        IReadOnlyCollection<AvailabilitySlot> slots = NewCalendar(resource)
            .Slots(Thursday, Thursday.AddDays(1), Thursday);

        Assert.Equal(["09:00", "09:30"], Starts(slots));
    }

    [Fact]
    public void ShouldReturnEmptyWhenNoticeExceedsHorizon()
    {
        Resource resource = NewResource(
            minNotice: TimeSpan.FromDays(2),
            maxHorizon: TimeSpan.FromDays(1)
        );

        IReadOnlyCollection<AvailabilitySlot> slots = NewCalendar(resource)
            .Slots(Thursday, Thursday.AddDays(7), Thursday);

        Assert.Empty(slots);
    }

    [Fact]
    public void ShouldDropTrailingRemainderWhenSlotDurationDoesNotDivideBlock()
    {
        // 09:00-11:00 with 45-minute slots yields 09:00 and 09:45; 10:30-11:15 overruns.
        Resource resource = NewResource(slotDuration: TimeSpan.FromMinutes(45));

        IReadOnlyCollection<AvailabilitySlot> slots = NewCalendar(resource)
            .Slots(Thursday, Thursday.AddDays(1), Thursday);

        Assert.Equal(["09:00", "09:45"], Starts(slots));
    }

    [Fact]
    public void ShouldReturnEmptyWhenResourceHasNoOpeningHours()
    {
        IReadOnlyCollection<AvailabilitySlot> slots = NewCalendar(openingHours: [])
            .Slots(Thursday, Thursday.AddDays(7), Thursday);

        Assert.Empty(slots);
    }

    [Fact]
    public void ShouldIgnoreOpeningHoursForOtherWeekdays()
    {
        IReadOnlyCollection<AvailabilitySlot> slots = NewCalendar(
                openingHours: [NewOpeningHours(DayOfWeek.Friday)]
            )
            .Slots(Thursday, Thursday.AddHours(23), Thursday);

        Assert.Empty(slots);
    }

    [Fact]
    public void ShouldIgnoreOpeningHoursThatDoNotEndAfterTheyStart()
    {
        IReadOnlyCollection<AvailabilitySlot> slots = NewCalendar(
                openingHours: [NewOpeningHours(endTime: "09:00")]
            )
            .Slots(Thursday, Thursday.AddDays(1), Thursday);

        Assert.Empty(slots);
    }

    [Fact]
    public void ShouldNotEmitDuplicateSlotsWhenOpeningHoursOverlap()
    {
        IReadOnlyCollection<AvailabilitySlot> slots = NewCalendar(
                openingHours: [NewOpeningHours(), NewOpeningHours(startTime: "09:30")]
            )
            .Slots(Thursday, Thursday.AddDays(1), Thursday);

        Assert.Equal(["09:00", "09:30", "10:00", "10:30"], Starts(slots));
    }

    [Fact]
    public void ShouldSpanMultipleDaysInOrder()
    {
        IReadOnlyCollection<AvailabilitySlot> slots = NewCalendar(
                openingHours:
                [
                    NewOpeningHours(DayOfWeek.Thursday, "09:00", "10:00"),
                    NewOpeningHours(DayOfWeek.Friday, "09:00", "10:00"),
                ]
            )
            .Slots(Thursday, Thursday.AddDays(2), Thursday);

        Assert.Equal(
            [
                At("09:00"),
                At("09:30"),
                At("09:00").AddDays(1),
                At("09:30").AddDays(1),
            ],
            slots.Select(x => x.StartsAt)
        );
    }

    [Fact]
    public void ShouldReturnEmptyWhenSlotDurationIsNotPositive()
    {
        Resource resource = NewResource(slotDuration: TimeSpan.Zero);

        IReadOnlyCollection<AvailabilitySlot> slots = NewCalendar(resource)
            .Slots(Thursday, Thursday.AddDays(1), Thursday);

        Assert.Empty(slots);
    }

    [Fact]
    public void ShouldConfirmAvailabilityWhenPeriodMatchesAFreeSlot()
    {
        Assert.True(NewCalendar().IsAvailable(At("09:30"), At("10:00"), Thursday));
    }

    [Fact]
    public void ShouldRejectAvailabilityWhenPeriodIsOffTheGrid()
    {
        Assert.False(NewCalendar().IsAvailable(At("09:15"), At("09:45"), Thursday));
    }

    [Fact]
    public void ShouldRejectAvailabilityWhenPeriodSpansSeveralSlots()
    {
        Assert.False(NewCalendar().IsAvailable(At("09:00"), At("10:00"), Thursday));
    }

    [Fact]
    public void ShouldRejectAvailabilityWhenSlotIsAlreadyBooked()
    {
        Booking booking = new(
            Guid.NewGuid(),
            ResourceId,
            Guid.NewGuid(),
            At("09:30"),
            At("10:00"),
            BookingStatus.Confirmed
        );

        Assert.False(
            NewCalendar(bookings: [booking])
                .IsAvailable(At("09:30"), At("10:00"), Thursday)
        );
    }

    [Fact]
    public void ShouldFreeTheSlotWhenExcludingTheBookingThatHoldsIt()
    {
        Booking booking = new(
            Guid.NewGuid(),
            ResourceId,
            Guid.NewGuid(),
            At("09:30"),
            At("10:00"),
            BookingStatus.Confirmed
        );

        AvailabilityCalendar calendar = NewCalendar(bookings: [booking]);

        Assert.False(calendar.IsAvailable(At("09:30"), At("10:00"), Thursday));
        Assert.True(
            calendar.Excluding(booking.Id).IsAvailable(At("09:30"), At("10:00"), Thursday)
        );
    }

    [Fact]
    public void ShouldKeepOtherBookingsWhenExcludingOne()
    {
        Booking excluded = new(
            Guid.NewGuid(),
            ResourceId,
            Guid.NewGuid(),
            At("09:00"),
            At("09:30"),
            BookingStatus.Confirmed
        );

        Booking kept = new(
            Guid.NewGuid(),
            ResourceId,
            Guid.NewGuid(),
            At("10:00"),
            At("10:30"),
            BookingStatus.Confirmed
        );

        IReadOnlyCollection<AvailabilitySlot> slots = NewCalendar(
                bookings: [excluded, kept]
            )
            .Excluding(excluded.Id)
            .Slots(Thursday, Thursday.AddDays(1), Thursday);

        Assert.Equal(["09:00", "09:30", "10:30"], Starts(slots));
    }
}
