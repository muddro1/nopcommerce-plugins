using Nop.Services.Helpers;

namespace Nop.Plugin.Misc.CustomerProfile.Logic;

/// <summary>Dates for expiry and due-date rules, which are calendar days in the store time zone</summary>
public interface IStoreClock
{
    DateTime UtcNow { get; }

    /// <summary>Today's date in the store time zone</summary>
    DateTime Today { get; }

    /// <summary>The store-time-zone date of a UTC instant</summary>
    DateTime ToStoreDate(DateTime utc);
}

public class StoreClock : IStoreClock
{
    private readonly IDateTimeHelper _dateTimeHelper;

    public StoreClock(IDateTimeHelper dateTimeHelper)
    {
        _dateTimeHelper = dateTimeHelper;
    }

    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime Today => ToStoreDate(DateTime.UtcNow);

    public DateTime ToStoreDate(DateTime utc) =>
        _dateTimeHelper.ConvertToUserTime(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Utc, _dateTimeHelper.DefaultStoreTimeZone).Date;
}
