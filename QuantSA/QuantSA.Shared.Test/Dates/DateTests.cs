using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuantSA.Shared.Dates;
using System;


namespace QuantSA.Shared.Test.Dates
{
    [TestClass]
    public class DateTest
    {
        [TestMethod]
        public void Function_FromYearMonthDay_InitializesCorrectly()
        {
            var date = new Date(2023, 12, 25);
            Assert.AreEqual(2023, date.Year);
            Assert.AreEqual(12, date.Month);
            Assert.AreEqual(25, date.Day);
        }

    }
}

