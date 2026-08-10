namespace BookingEngine.Infrastructure.Tests;

[CollectionDefinition(nameof(BookingDbTestSet))]
public sealed class BookingDbTestSet : ICollectionFixture<BookingDbFixture>;
