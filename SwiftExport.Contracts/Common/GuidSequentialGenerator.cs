using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.Common
{
    public static class GuidSequentialGenerator
    {
        public static Guid NewGuid()
        {
            var guidArray = Guid.NewGuid().ToByteArray();

            var now = DateTime.UtcNow;
            var days = BitConverter.GetBytes(now.Ticks / TimeSpan.TicksPerDay);
            var msecs = BitConverter.GetBytes((long)(now.TimeOfDay.TotalMilliseconds));

            // SQL Server 顺序 GUID 格式（尾部递增）
            Array.Reverse(days);
            Array.Reverse(msecs);

            Array.Copy(days, days.Length - 2, guidArray, guidArray.Length - 6, 2);
            Array.Copy(msecs, msecs.Length - 4, guidArray, guidArray.Length - 4, 4);

            return new Guid(guidArray);
        }
    }

}
