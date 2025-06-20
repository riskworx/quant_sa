using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuantSA.Shared.Dates;
using System;


namespace QuantSA.Shared.Test.Dates
{
    [TestClass]
    public class DateTest
    {
        private Date _date;

        [TestInitialize]
        public void Setup()
        {
            _date = new Date(2023, 12, 25);
        }

        [TestMethod]
        public void Date_FromYearMonthDay_MembersAreCorrect()
        {
            Assert.AreEqual(2023, _date.Year);
            Assert.AreEqual(12, _date.Month);
            Assert.AreEqual(25, _date.Day);
        }

        [TestMethod]
        public void IsLeapYear_ReturnsCorrectly()
        {
            Assert.IsTrue(Date.IsLeapYear(2020));
            Assert.IsFalse(Date.IsLeapYear(2021));
        }
    }
}

