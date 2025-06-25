using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuantSA.Shared.Dates;
using QuantSA.Shared.MarketObservables;
using QuantSA.Shared.Primitives;
using QuantSA.Core.Products.Rates;
using QuantSA.CoreExtensions.ProductPVs.Rates;
using QuantSA.Shared.MarketData;
using QuantSA.Shared.Conventions;
using QuantSA.Solution.Test;
using QuantSA.Core.Products;

namespace QuantSA.CoreExtensions.Test.whycantname.Rates
{
    [TestClass]
    public class FraPVTests
    {
        [TestMethod]
        public void CurvePv_PricesFRA_AsExpected()
        {
            // Arrange
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

            var discountCurve = new FlatDiscountCurve(valueDate, currency, marketRate);

            fra.SetValueDate(valueDate);
            fra.SetIndexValues(index, new[] { marketRate });

            // Act
            var pv = fra.CurvePv(discountCurve);

            // Expected
            var undiscountedPayoff = notional * (marketRate - fixedRate) * accrualFraction / (1 + marketRate * accrualFraction);
            var t = (nearDate - valueDate) / 365.0;
            var expectedPv = undiscountedPayoff * Math.Exp(-marketRate * t);

            // Assert
            Assert.AreEqual(expectedPv, pv, 1e-2);
        }

        // ✅ Local helper class for a flat discount curve
        private class FlatDiscountCurve : IDiscountingSource
        {
            private readonly Date _anchorDate;
            private readonly double _rate;
            private readonly Currency _currency;

            public FlatDiscountCurve(Date anchorDate, Currency currency, double rate)
            {
                _anchorDate = anchorDate;
                _rate = rate;
                _currency = currency;
            }

            public Date GetAnchorDate() => _anchorDate;

            public Currency GetCurrency() => _currency;

            public double GetDF(Date date)
            {
                var t = (date - _anchorDate) / 365.0;
                return Math.Exp(-_rate * t);
            }

            // Stub implementations for inherited interface (not needed for test)
            public bool CanBeA<T>(MarketDataDescription<T> description, IMarketDataContainer container) where T : class, IMarketDataSource => false;
            public T Get<T>(MarketDataDescription<T> description) where T : class, IMarketDataSource => default;


            public string GetName() => "FlatCurveTest";

            public bool TryCalibrate(Date anchorDate, IMarketDataContainer container) => false;
        }

    }
}
