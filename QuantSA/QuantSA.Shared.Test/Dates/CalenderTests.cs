using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuantSA.Shared.Dates;
using Calendar = QuantSA.Shared.Dates.Calendar;

namespace QuantSA.Shared.Test.Dates
{
    [TestClass]
    public class CalenderTests
    {
        [TestMethod]
        public void BusinessDaysBetween_ExcludesWeekends()
        {
            var calender = new Calendar("TestCalender");
            var start = new Date(2024, 6, 3);
            var end = new Date(2024, 6, 7);
            int result = calender.BusinessDaysBetween(start, end);
            Assert.AreEqual(5, result);
        }
    }
}
