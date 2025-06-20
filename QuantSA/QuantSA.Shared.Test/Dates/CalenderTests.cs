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
            _start = new Date(2024, 6, 3);
            _end = new Date(2024, 6, 7);

        }
        [TestMethod]
        public void BusinessDaysBetween_ReturnsCorrectly()
        {

            int result = _calendar.BusinessDaysBetween(_start, _end);
            
            Assert.AreEqual(4, result);
        }
    }
}
