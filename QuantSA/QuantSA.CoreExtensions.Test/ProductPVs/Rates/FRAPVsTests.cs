using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuantSA.Core.MarketData;
using QuantSA.Core.Products.Rates;
using QuantSA.CoreExtensions.ProductPVs.Rates;
using QuantSA.Shared.Dates;
using QuantSA.Shared.MarketData;
using QuantSA.Shared.MarketObservables;
using QuantSA.Shared.Primitives;
using QuantSA.TestUtils;

namespace QuantSA.CoreExtensions.Test.ProductPVs.Rates
{
    [TestClass]
    public class FraPVTests
    {
        [TestMethod]
        public void CurvePv_PricesFRA_AsExpected()
        {
            var valueDate = new Date(2025, 1, 1);
            var nearDate = new Date(2025, 6, 30);
            var farDate = new Date(2025, 12, 31);
            var notional = 1_000_000.0;
            var fixedRate = 0.05;
            var accrualFraction = 0.5;
            var marketRate = 0.04;
            var payFixed = true;
            var currency = TestHelpers.ZAR;


            var index = new FloatRateIndex("DummyIndex", currency, "JIBAR", Tenor.FromMonths(3));
            var fra = new FRA(notional, accrualFraction, fixedRate, payFixed, nearDate, farDate, index);

            var discountCurve = new SingleRate(marketRate,valueDate,currency);

            fra.SetValueDate(valueDate);
            fra.SetIndexValues(index, new[] { marketRate });

            var pv = fra.CurvePv(discountCurve);

            var undiscountedPayoff = notional * (marketRate - fixedRate) * accrualFraction / (1 + marketRate * accrualFraction);
            var t = (nearDate - valueDate) / 365.0;
            var expectedPv = undiscountedPayoff * Math.Exp(-marketRate * t);

            Assert.AreEqual(expectedPv, pv, 1e-2);
        }
    }
}
