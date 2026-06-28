using System;

namespace {{ProjectName}}.Contracts.Common;

public static class GuidSequentialGenerator
{
    public static Guid NewGuid()
    {
        var guidArray = Guid.NewGuid().ToByteArray();
        var now = DateTime.UtcNow;
        var days = BitConverter.GetBytes(now.Ticks / TimeSpan.TicksPerDay);
        var milliseconds = BitConverter.GetBytes((long)now.TimeOfDay.TotalMilliseconds);

        Array.Reverse(days);
        Array.Reverse(milliseconds);

        Array.Copy(days, days.Length - 2, guidArray, guidArray.Length - 6, 2);
        Array.Copy(milliseconds, milliseconds.Length - 4, guidArray, guidArray.Length - 4, 4);

        return new Guid(guidArray);
    }
}
