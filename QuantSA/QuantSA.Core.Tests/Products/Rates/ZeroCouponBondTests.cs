using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuantSA.Core.Products.Rates;
using QuantSA.Shared.Dates;
using QuantSA.Shared.Primitives;
using QuantSA.Solution.Test;

namespace QuantSA.Core.Tests.Products.Rates
{
    [TestClass]
    public class ZeroCouponBondTests
    {
        [TestMethod]
        public void GetCfs_MaturityAfterValueDate_ReturnsExpectedCashflow()
        {
            var maturity = new Date(2028, 12, 31);
            double notional = 1_000_000;
            var currency = new Currency("ZAR");
            //var currency = TestHelpers.ZAR;

            var bond = new ZeroCouponBond(maturity, notional, currency);

            bond.SetValueDate(new Date(2024, 01, 02));
            var cashflows = bond.GetCFs();

            Assert.AreEqual(1, cashflows.Count);
            Assert.AreEqual(maturity, cashflows[0].Date);
            Assert.AreEqual(notional, cashflows[0].Amount);
            Assert.AreEqual(currency, cashflows[0].Currency);
        }

        [TestMethod]
        public void GetCfs_MaturityBeforeValueDate_ReturnsNoCashflows()
        {
            var maturity = new Date(2024, 01, 01);
            double notional = 1_000_000;
            var currency = new Currency("ZAR");
            //var currency = TestHelpers.ZAR;

            var bond = new ZeroCouponBond(maturity, notional, currency);

            bond.SetValueDate(new Date(2024, 01, 02));
            var cashflows = bond.GetCFs();

            Assert.AreEqual(0, cashflows.Count);

        }
    }
}