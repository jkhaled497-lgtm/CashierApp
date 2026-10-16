namespace Cashier.Application;

public static class BusinessTime
{
    private static readonly TimeZoneInfo EgyptTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    public static DateTime ToEgyptLocal(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, EgyptTimeZone).DateTime;

    public static DateTimeOffset ToUtc(DateTime value)
    {
        var local = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, EgyptTimeZone), TimeSpan.Zero);
    }
}
