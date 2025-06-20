using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuantSA.Shared.Dates;
using Calendar = QuantSA.Shared.Dates.Calendar;

namespace QuantSA.Shared.Test.Dates
{
    [TestClass]
    public class CalenderTests
    {
        private Calendar _calendar;
        private Date _start;
        private Date _end;

        [TestInitialize]
        public void Setup()
        {
            _calendar = new Calendar("TestCalender");
            _start = new Date(2024, 6, 17);
            _end = new Date(2024, 6, 21);

        }
        [TestMethod]
        public void BusinessDaysBetween_ReturnsCorrectly()
        {

            var result = _calendar.BusinessDaysBetween(_start, _end);
            
            Assert.AreEqual(4, result);
        }

        [TestMethod]
        public void IsHoliday_ReturnsCorrectly()
        {
            Assert.IsFalse(_calendar.IsHoliday(_end));
            Assert.IsTrue(_calendar.IsHoliday(_start));
        }

        [TestMethod]
        public void IsEndOfMonth_ReturnsCorrectly()
        {
            var isLastDay = new Date(2024, 1, 31);
            var isNotLastDay = new Date(2024, 1, 30);

            Assert.IsTrue(_calendar.IsEndOfMonth(isLastDay));
            Assert.IsFalse(_calendar.IsEndOfMonth(isNotLastDay));
        }
    }
}
