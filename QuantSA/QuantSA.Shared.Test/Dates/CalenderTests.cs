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

            _calendar.AddHoliday(_start); //this is needed for the IsHoliday 

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
    }
}
