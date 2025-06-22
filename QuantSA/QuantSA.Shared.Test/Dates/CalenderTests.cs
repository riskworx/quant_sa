using System;
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

            //this is needed for the IsHoliday test
            _calendar.AddHoliday(_start);

            //this is needed for the EndOfMonth test
            var holiday = new Date(2024, 6, 28);
            _calendar.AddHoliday(holiday);
        }

        [TestMethod]
        public void IsHoliday_ReturnsCorrect()
        {
            Assert.IsFalse(_calendar.IsHoliday(_end));
            Assert.IsTrue(_calendar.IsHoliday(_start));
        }

        [TestMethod]
        public void IsEndOfMonth_ReturnsCorrect()
        {
            var isLastDay = new Date(2024, 1, 31);
            var actualEndOfMonth = _calendar.EndOfMonth(isLastDay);

            Assert.IsTrue(_calendar.IsEndOfMonth(isLastDay));
        }

        [TestMethod]
        public void EndOfMonth_ReturnsCorrectly()
        {
            var input = new Date(2024, 6, 15);
            var result = _calendar.EndOfMonth(input);

            var expected = new Date(2024, 6, 27);
            Assert.AreEqual(expected,result);
        }

        [TestMethod]
        public void IsBusinessDay_ReturnsCorrectly()
        {
            var weekday = new Date(2024, 6, 18);
            var weekend = new Date(2024, 6, 22);

            Assert.IsFalse(_calendar.IsBusinessDay(_start));
            Assert.IsTrue(_calendar.IsBusinessDay(weekday));
            Assert.IsFalse(_calendar.IsBusinessDay(weekend));
        }

        [TestMethod]
        public void IsWeekend_ReturnsCorrectly()
        {
            var saturday = DayOfWeek.Saturday;
            var sunday = DayOfWeek.Sunday;
            var monday = DayOfWeek.Monday;

            Assert.IsTrue(Calendar.IsWeekend(saturday));
            Assert.IsTrue(Calendar.IsWeekend(sunday));
            Assert.IsFalse(Calendar.IsWeekend(monday));
        }
    }
}
