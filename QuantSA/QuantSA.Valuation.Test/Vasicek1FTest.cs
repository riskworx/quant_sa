using System;
using System.Collections.Generic;
using System.Linq;
using Accord.Statistics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuantSA.Shared.Dates;
using QuantSA.Valuation.Models.Rates;
using QuantSA.TestUtils;

namespace QuantSA.Valuation.Test
{
    [TestClass]
    public class Vasicek1FTest
    {
        private Date _valueDate;
        private Date _maturity;

        private double _a = 0.1;
        private double _b = 0.05;
        private double _sigma = 0.01;
        private double _r0 = 0.05;
        private Vasicek1F _vasicek;

        [TestInitialize]
        public void Setup()
        {
            _valueDate = new Date(2024, 1, 1);
            _maturity = new Date(2026, 1, 1);
            _a = 0.1;
            _b = 0.05;
            _sigma = 0.01;
            _r0 = 0.05;

            _vasicek = new Vasicek1F(TestHelpers.ZAR, _a, _b, _sigma, _r0);
        }

        [TestMethod]
        public void Vasicek1F_BondPriceMatchesClosedForm()
        {
            _vasicek.Reset();
            _vasicek.SetNumeraireDates(new List<Date> {_maturity});
            _vasicek.Prepare(_valueDate);

            int numberOfSims = 10_000;
            double[] simulatedprices = new double[numberOfSims];

            for (int i = 0; i < numberOfSims; i++)
            {
                _vasicek.RunSimulation(i);
                simulatedprices[i] = 1 / _vasicek.Numeraire(_maturity);
            }

            double mcBondPrice = simulatedprices.Mean();

            double T = (_maturity - _valueDate) / 365.0;
            double B = (1 - Math.Exp(-_a * T)) / _a;
            double A = Math.Exp((B - T) * (_a * _a * _b - 0.5 * _sigma * _sigma) / (_a * _a) - (_sigma * _sigma * B * B) / (4 * _a));
            double expected = A * Math.Exp(-B * _r0);

            Assert.AreEqual(expected, mcBondPrice, 5e-3, "Simulated bond price should match closed-form Vasicek price");
        }
    }
}
