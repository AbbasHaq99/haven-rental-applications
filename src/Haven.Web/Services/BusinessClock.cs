namespace Haven.Web.Services;

public class BusinessClock(TimeProvider time, IConfiguration config)
{
    public DateTimeOffset Now => time.GetUtcNow();
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now,
        TimeZoneInfo.FindSystemTimeZoneById(config["BusinessTimeZone"] ?? "America/Chicago")).DateTime);
}
