using System;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuantSA.Shared.Dates;
using Calendar = QuantSA.Shared.Dates.Calendar;
using System.Collections.Generic;


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

            _calendar.AddHoliday(_start);

            var holiday = new Date(2024, 6, 28);
            _calendar.AddHoliday(holiday);
        }

        [TestMethod]
        public void IsHoliday_ReturnsTrueCorrect()
        {
            Assert.IsTrue(_calendar.IsHoliday(_start));
        }

        [TestMethod]
        public void IsHoliday_ReturnsFalseCorrect()
        {
            Assert.IsFalse(_calendar.IsHoliday(_end));
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
        public void IsBusinessDay_ReturnsWeekdayAsExpected()
        {
            var weekday = new Date(2024, 6, 18);

            Assert.IsTrue(_calendar.IsBusinessDay(weekday));
        }

        [TestMethod]
        public void IsBusinessDay_ReturnsWeekendAsExpected()
        {
            var weekend = new Date(2024, 6, 22);

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

        [TestMethod]
        public void BusinessDaysBetween_ReturnsCorrectly()
        {
            var from = new Date(2024, 6, 17); 
            var to = new Date(2024, 6, 21);   

            var result = _calendar.BusinessDaysBetween(from, to);

            Assert.AreEqual(3, result); // This is because the BusinessDaysBetween method by default includes the from and excludes the to.
        }

        [TestMethod]
        public void BusinessDaysBetween_ReturnsNegative_WhenFromIsAfterTo()
        {
            var from = new Date(2024, 6, 21);
            var to = new Date(2024, 6, 17);  

            var result = _calendar.BusinessDaysBetween(from, to); 

            Assert.AreEqual(-4, result); 
        }

        [TestMethod]
        public void Constructor_WithHolidays_AddsHolidaysCorrectly()
        {
            var holidays = new List<Date>
            {
                new Date(2024, 12, 25),
                new Date(2024, 1, 1)
            };

            var calendar = new Calendar("HolidayCalendar", holidays);

            Assert.IsTrue(calendar.IsHoliday(new Date(2024, 12, 25)));
            Assert.IsTrue(calendar.IsHoliday(new Date(2024, 1, 1)));
            Assert.AreEqual("HolidayCalendar", calendar.GetName());
        }

        [TestMethod]
        public void GetName_ReturnsCalendarName()
        {
            var calendar = new Calendar("TestMarket");

            var name = calendar.GetName();

            Assert.AreEqual("TestMarket", name);
        }
    }
}
