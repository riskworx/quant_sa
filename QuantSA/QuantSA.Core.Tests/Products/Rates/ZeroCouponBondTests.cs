using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuantSA.Core.Products.Rates;
using QuantSA.Shared.Dates;
using QuantSA.Shared.Primitives;
using QuantSA.TestUtils;

namespace QuantSA.Core.Tests.Products.Rates
{
    [TestClass]
    public class ZeroCouponBondTests
    {
        private double _notional;
        private Currency _currency;

        [TestInitialize]
        public void Setup()
        {
            _notional = 1_000_000;
            _currency = TestHelpers.ZAR;
        }
        [TestMethod]
        public void GetCfs_MaturityAfterValueDate_ReturnsExpectedCashflow()
        {
            var maturity = new Date(2028, 12, 31);
            
            var bond = new ZeroCouponBond(maturity, _notional, _currency);

            bond.SetValueDate(new Date(2024, 01, 02));
            var cashflows = bond.GetCFs();

            Assert.AreEqual(1, cashflows.Count);
            Assert.AreEqual(maturity, cashflows[0].Date);
            Assert.AreEqual(_notional, cashflows[0].Amount);
            Assert.AreEqual(_currency, cashflows[0].Currency);
        }

        [TestMethod]
        public void GetCfs_MaturityBeforeValueDate_ReturnsNoCashflows()
        {
            var maturity = new Date(2024, 01, 01);

            var bond = new ZeroCouponBond(maturity, _notional, _currency);

            bond.SetValueDate(new Date(2024, 01, 02));
            var cashflows = bond.GetCFs();

            Assert.AreEqual(0, cashflows.Count);
        }
    }
}