using System;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuantSA.Shared.Dates;
using Calendar = QuantSA.Shared.Dates.Calendar;
using System.Collections.Generic;


namespace QuantSA.Shared.Test.Dates
{
    [TestClass]
    public class CalendarTests
    {
        private Calendar _calendar;
        private Date _start;
        private Date _end;

        [TestInitialize]
        public void Setup()
        {
            _calendar = new Calendar("TestCalendar");
            _start = new Date(2024, 6, 17);
            _end = new Date(2024, 6, 21);

            _calendar.AddHoliday(_start);

            var holiday = new Date(2024, 6, 28);
            _calendar.AddHoliday(holiday);
        }

        [TestMethod]
        public void IsHoliday_DaysIsHoliday_ReturnsTrue()
        {
            Assert.IsTrue(_calendar.IsHoliday(_start));
        }

        [TestMethod]
        public void IsHoliday_DaysIsNotHoliday_ReturnsFalse()
        {
            Assert.IsFalse(_calendar.IsHoliday(_end));
        }

        [TestMethod]
        public void IsEndOfMonth_ReturnsExpectedValue()
        {
            var isLastDay = new Date(2024, 1, 31);
            var actualEndOfMonth = _calendar.EndOfMonth(isLastDay);

            Assert.IsTrue(_calendar.IsEndOfMonth(isLastDay));
        }

        [TestMethod]
        public void EndOfMonth_ReturnsExpectedValue()
        {
            var input = new Date(2024, 6, 15);
            var result = _calendar.EndOfMonth(input);

            var expected = new Date(2024, 6, 27);
            Assert.AreEqual(expected,result);
        }

        [TestMethod]
        public void IsBusinessDay_Weekday_ReturnsTrue()
        {
            var weekday = new Date(2024, 6, 18);

            Assert.IsTrue(_calendar.IsBusinessDay(weekday));
        }

        [TestMethod]
        public void IsBusinessDay_Weekend_ReturnsFalse()
        {
            var weekend = new Date(2024, 6, 22);

            Assert.IsFalse(_calendar.IsBusinessDay(weekend));
        }

        [TestMethod]
        public void IsWeekend_Monday_ReturnsFalse()
        {
            var monday = DayOfWeek.Monday;

            Assert.IsFalse(Calendar.IsWeekend(monday));
        }

        [TestMethod]
        public void IsWeekend_Saturday_ReturnsFalse()
        {
            var saturday = DayOfWeek.Saturday;

            Assert.IsTrue(Calendar.IsWeekend(saturday));
        }

        [TestMethod]
        public void IsWeekend_Sunday_ReturnsFalse()
        {
            var sunday = DayOfWeek.Sunday;

            Assert.IsTrue(Calendar.IsWeekend(sunday));
        }

        [TestMethod]
        public void BusinessDaysBetween_FromLessThanTo_ReturnsExpectedValue()
        {
            var from = new Date(2024, 6, 17); 
            var to = new Date(2024, 6, 21);   

            var result = _calendar.BusinessDaysBetween(from, to);

            Assert.AreEqual(3, result); // This is because the BusinessDaysBetween method by default includes the from and excludes the to.
        }

        [TestMethod]
        public void BusinessDaysBetween_FromMoreThanTo_ReturnsExpectedNegativeValue()
        {
            var from = new Date(2024, 6, 21);
            var to = new Date(2024, 6, 17);  

            var result = _calendar.BusinessDaysBetween(from, to); 

            Assert.AreEqual(-4, result); 
        }

        [TestMethod]
        public void ReturnsExpectedValue()
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
        public void GetName_ReturnsExpectedValue()
        {
            var calendar = new Calendar("TestMarket");

            var name = calendar.GetName();

            Assert.AreEqual("TestMarket", name);
        }
    }
}
