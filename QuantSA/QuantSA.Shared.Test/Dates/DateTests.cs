using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuantSA.Shared.Dates;
using System;

namespace QuantSA.Shared.Test.Dates
{
    [TestClass]
    public class DateTests
    {
        private Date _date;

        [TestInitialize]
        public void Setup()
        {
            _date = new Date(2023, 12, 25);
        }

        [TestMethod]
        public void Constructor_WithValidDate_SetsCorrectMembers()
        {
            Assert.AreEqual(2023, _date.Year);
            Assert.AreEqual(12, _date.Month);
            Assert.AreEqual(25, _date.Day);
        }

        [TestMethod]
        public void IsLeapYear_WithLeapYear_ReturnsTrue()
        {
            Assert.IsTrue(Date.IsLeapYear(2020));
        }

        [TestMethod]
        public void IsLeapYear_WithNonLeapYear_ReturnsFalse()
        {
            Assert.IsFalse(Date.IsLeapYear(2021));
        }

        [TestMethod]
        public void DaysInMonth_ReturnsAsExpected()
        {
            Assert.AreEqual(31, Date.DaysInMonth(2023, 1));
            Assert.AreEqual(29, Date.DaysInMonth(2020, 2));
            Assert.AreEqual(28, Date.DaysInMonth(2023, 2));
        }

        [TestMethod]
        public void EndOfMonth_WithMidMonthDate_ReturnsAsExpected()
        {
            var midMonth = new Date(2023, 3, 15);
            var endOfMonth = Date.EndOfMonth(midMonth);

            Assert.AreEqual(31, endOfMonth.Day);
            Assert.AreEqual(3, endOfMonth.Month);
            Assert.AreEqual(2023, endOfMonth.Year);
        }

        [TestMethod]
        public void IsEndOfMonth_WithLastDayOfMonth_ReturnsTrue()
        {
            var lastDay = new Date(2023, 4, 30);

            Assert.IsTrue(Date.IsEndOfMonth(lastDay));
        }

        [TestMethod]
        public void IsEndOfMonth_WithNotLastDayOfMonth_ReturnsFalse()
        {
            var notLastDay = new Date(2023, 4, 29);

            Assert.IsFalse(Date.IsEndOfMonth(notLastDay));
        }
    }
}

