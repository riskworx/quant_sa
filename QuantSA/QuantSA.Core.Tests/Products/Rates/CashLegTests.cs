using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using QuantSA.Core.Products.Rates;
using QuantSA.Shared.Dates;
using QuantSA.Shared.MarketObservables;
using QuantSA.Shared.Primitives;
using QuantSA.TestUtils;
using Assert = NUnit.Framework.Assert;

namespace QuantSA.Core.Tests.Products.Rates
{
    [TestClass]
    public class CashLegTests
    {
        private readonly Currency _zar = TestHelpers.ZAR;
        private readonly Currency _usd = TestHelpers.USD;
        private readonly Currency _eur = TestHelpers.EUR;
        private readonly Date _valueDate = new Date("2020-01-15");

        [TestMethod]
        public void CashLeg_Constructor_SingleCashflow()
        {
            // Arrange
            var dates = new[] { new Date("2020-06-15") };
            var amounts = new[] { 1000.0 };
            var currencies = new[] { _zar };

            // Act
            var cashLeg = new CashLeg(dates, amounts, currencies);

            // Assert
            Assert.IsNotNull(cashLeg);
            var cfs = cashLeg.GetCFs();
            Assert.That(cfs, Has.Count.EqualTo(1));
            Assert.AreEqual(dates[0], cfs[0].Date);
            Assert.AreEqual(amounts[0], cfs[0].Amount, 1e-10);
            Assert.AreEqual(currencies[0], cfs[0].Currency);
        }

        [TestMethod]
        public void CashLeg_Constructor_MultipleCashflows()
        {
            // Arrange
            var dates = new[] 
            { 
                new Date("2020-03-15"), 
                new Date("2020-06-15"), 
                new Date("2020-09-15") 
            };
            var amounts = new[] { 100.0, 200.0, 300.0 };
            var currencies = new[] { _zar, _zar, _zar };

            // Act
            var cashLeg = new CashLeg(dates, amounts, currencies);

            // Assert
            Assert.IsNotNull(cashLeg);
            var cfs = cashLeg.GetCFs();
            Assert.That(cfs, Has.Count.EqualTo(3));
            for (var i = 0; i < dates.Length; i++)
            {
                Assert.AreEqual(dates[i], cfs[i].Date);
                Assert.AreEqual(amounts[i], cfs[i].Amount, 1e-10);
                Assert.AreEqual(currencies[i], cfs[i].Currency);
            }
        }

        [TestMethod]
        public void CashLeg_Constructor_MultipleCurrencies()
        {
            // Arrange
            var dates = new[] 
            { 
                new Date("2020-03-15"), 
                new Date("2020-06-15"), 
                new Date("2020-09-15") 
            };
            var amounts = new[] { 1000.0, 2000.0, 3000.0 };
            var currencies = new[] { _zar, _usd, _eur };

            // Act
            var cashLeg = new CashLeg(dates, amounts, currencies);

            // Assert
            Assert.IsNotNull(cashLeg);
            var cfs = cashLeg.GetCFs();
            Assert.That(cfs, Has.Count.EqualTo(3));
            Assert.AreEqual(_zar, cfs[0].Currency);
            Assert.AreEqual(_usd, cfs[1].Currency);
            Assert.AreEqual(_eur, cfs[2].Currency);
        }

        [TestMethod]
        public void CashLeg_GetCashflowCurrencies_SingleCurrency()
        {
            // Arrange
            var dates = new[] 
            { 
                new Date("2020-03-15"), 
                new Date("2020-06-15") 
            };
            var amounts = new[] { 100.0, 200.0 };
            var currencies = new[] { _zar, _zar };
            var cashLeg = new CashLeg(dates, amounts, currencies);

            // Act
            var result = cashLeg.GetCashflowCurrencies();

            // Assert
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.AreEqual(_zar, result[0]);
        }

        [TestMethod]
        public void CashLeg_GetCashflowCurrencies_MultipleCurrencies()
        {
            // Arrange
            var dates = new[] 
            { 
                new Date("2020-03-15"), 
                new Date("2020-06-15"), 
                new Date("2020-09-15") 
            };
            var amounts = new[] { 100.0, 200.0, 300.0 };
            var currencies = new[] { _zar, _usd, _eur };
            var cashLeg = new CashLeg(dates, amounts, currencies);

            // Act
            var result = cashLeg.GetCashflowCurrencies();

            // Assert
            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result.Contains(_zar));
            Assert.That(result.Contains(_usd));
            Assert.That(result.Contains(_eur));
        }

        [TestMethod]
        public void CashLeg_GetCashflowCurrencies_DuplicatesNotReturned()
        {
            // Arrange
            var dates = new[] 
            { 
                new Date("2020-03-15"), 
                new Date("2020-06-15"), 
                new Date("2020-09-15"),
                new Date("2020-12-15")
            };
            var amounts = new[] { 100.0, 200.0, 300.0, 400.0 };
            var currencies = new[] { _zar, _usd, _zar, _usd };
            var cashLeg = new CashLeg(dates, amounts, currencies);

            // Act
            var result = cashLeg.GetCashflowCurrencies();

            // Assert
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result.Contains(_zar));
            Assert.That(result.Contains(_usd));
        }

        [TestMethod]
        public void CashLeg_GetCashflowDates_OnlyFutureDates()
        {
            // Arrange
            var dates = new[] 
            { 
                new Date("2020-01-10"), 
                new Date("2020-01-14"), 
                new Date("2020-01-15"),
                new Date("2020-01-16"),
                new Date("2020-06-15") 
            };
            var amounts = new[] { 100.0, 200.0, 300.0, 400.0, 500.0 };
            var currencies = new[] { _zar, _zar, _zar, _zar, _zar };
            var cashLeg = new CashLeg(dates, amounts, currencies);
            cashLeg.SetValueDate(_valueDate);

            // Act
            var result = cashLeg.GetCashflowDates(_zar);

            // Assert
            // Only dates strictly after valueDate (2020-01-15) should be returned
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result.Contains(new Date("2020-01-16")));
            Assert.That(result.Contains(new Date("2020-06-15")));
        }

        [TestMethod]
        public void CashLeg_GetCFs_FiltersPastCashflows()
        {
            // Arrange
            var dates = new[] 
            { 
                new Date("2020-01-10"), 
                new Date("2020-01-14"), 
                new Date("2020-01-15"),
                new Date("2020-01-16"),
                new Date("2020-06-15") 
            };
            var amounts = new[] { 100.0, 200.0, 300.0, 400.0, 500.0 };
            var currencies = new[] { _zar, _zar, _zar, _zar, _zar };
            var cashLeg = new CashLeg(dates, amounts, currencies);
            cashLeg.SetValueDate(_valueDate);

            // Act
            var result = cashLeg.GetCFs();

            // Assert
            // Only cashflows strictly after valueDate (2020-01-15) should be returned
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.AreEqual(new Date("2020-01-16"), result[0].Date);
            Assert.AreEqual(400.0, result[0].Amount, 1e-10);
            Assert.AreEqual(new Date("2020-06-15"), result[1].Date);
            Assert.AreEqual(500.0, result[1].Amount, 1e-10);
        }

        [TestMethod]
        public void CashLeg_GetCFs_AllFutureCashflows()
        {
            // Arrange
            var dates = new[] 
            { 
                new Date("2020-03-15"), 
                new Date("2020-06-15"), 
                new Date("2020-09-15") 
            };
            var amounts = new[] { 100.0, 200.0, 300.0 };
            var currencies = new[] { _zar, _zar, _zar };
            var cashLeg = new CashLeg(dates, amounts, currencies);
            cashLeg.SetValueDate(_valueDate);

            // Act
            var result = cashLeg.GetCFs();

            // Assert
            Assert.That(result, Has.Count.EqualTo(3));
        }

        [TestMethod]
        public void CashLeg_GetCFs_AllPastCashflows()
        {
            // Arrange
            var dates = new[] 
            { 
                new Date("2019-03-15"), 
                new Date("2019-06-15"), 
                new Date("2019-09-15") 
            };
            var amounts = new[] { 100.0, 200.0, 300.0 };
            var currencies = new[] { _zar, _zar, _zar };
            var cashLeg = new CashLeg(dates, amounts, currencies);
            cashLeg.SetValueDate(_valueDate);

            // Act
            var result = cashLeg.GetCFs();

            // Assert
            Assert.That(result, Has.Count.EqualTo(0));
        }

        [TestMethod]
        public void CashLeg_GetCFs_BeforeSetValueDate()
        {
            // Arrange
            var dates = new[] 
            { 
                new Date("2020-03-15"), 
                new Date("2020-06-15") 
            };
            var amounts = new[] { 100.0, 200.0 };
            var currencies = new[] { _zar, _zar };
            var cashLeg = new CashLeg(dates, amounts, currencies);

            // Act
            var result = cashLeg.GetCFs();

            // Assert
            // Before SetValueDate is called, all cashflows should be returned
            Assert.That(result, Has.Count.EqualTo(2));
        }

        [TestMethod]
        public void CashLeg_SetValueDate_UpdatesFiltering()
        {
            // Arrange
            var dates = new[] 
            { 
                new Date("2020-01-10"), 
                new Date("2020-03-15"), 
                new Date("2020-06-15") 
            };
            var amounts = new[] { 100.0, 200.0, 300.0 };
            var currencies = new[] { _zar, _zar, _zar };
            var cashLeg = new CashLeg(dates, amounts, currencies);

            // Act & Assert
            cashLeg.SetValueDate(new Date("2020-01-01"));
            Assert.That(cashLeg.GetCFs(), Has.Count.EqualTo(3));

            cashLeg.SetValueDate(new Date("2020-01-10"));
            Assert.That(cashLeg.GetCFs(), Has.Count.EqualTo(2));

            cashLeg.SetValueDate(new Date("2020-03-15"));
            Assert.That(cashLeg.GetCFs(), Has.Count.EqualTo(1));

            cashLeg.SetValueDate(new Date("2020-12-31"));
            Assert.That(cashLeg.GetCFs(), Has.Count.EqualTo(0));
        }

        [TestMethod]
        public void CashLeg_GetRequiredIndices_ReturnsEmptyList()
        {
            // Arrange
            var dates = new[] { new Date("2020-06-15") };
            var amounts = new[] { 1000.0 };
            var currencies = new[] { _zar };
            var cashLeg = new CashLeg(dates, amounts, currencies);

            // Act
            var result = cashLeg.GetRequiredIndices();

            // Assert
            Assert.IsNotNull(result);
            Assert.That(result, Has.Count.EqualTo(0));
        }

        [TestMethod]
        public void CashLeg_GetRequiredIndexDates_ReturnsEmptyList()
        {
            // Arrange
            var dates = new[] { new Date("2020-06-15") };
            var amounts = new[] { 1000.0 };
            var currencies = new[] { _zar };
            var cashLeg = new CashLeg(dates, amounts, currencies);
            var index = TestHelpers.Jibar3M;

            // Act
            var result = cashLeg.GetRequiredIndexDates(index);

            // Assert
            Assert.IsNotNull(result);
            Assert.That(result, Has.Count.EqualTo(0));
        }

        [TestMethod]
        public void CashLeg_Reset_DoesNotThrow()
        {
            // Arrange
            var dates = new[] { new Date("2020-06-15") };
            var amounts = new[] { 1000.0 };
            var currencies = new[] { _zar };
            var cashLeg = new CashLeg(dates, amounts, currencies);

            // Act & Assert
            Assert.DoesNotThrow(() => cashLeg.Reset());
        }

        [TestMethod]
        public void CashLeg_SetIndexValues_DoesNotThrow()
        {
            // Arrange
            var dates = new[] { new Date("2020-06-15") };
            var amounts = new[] { 1000.0 };
            var currencies = new[] { _zar };
            var cashLeg = new CashLeg(dates, amounts, currencies);
            var index = TestHelpers.Jibar3M;
            var indexValues = new[] { 0.05, 0.06 };

            // Act & Assert
            Assert.DoesNotThrow(() => cashLeg.SetIndexValues(index, indexValues));
        }

        [TestMethod]
        public void CashLeg_Clone_CreatesDeepCopy()
        {
            // Arrange
            var dates = new[] 
            { 
                new Date("2020-03-15"), 
                new Date("2020-06-15") 
            };
            var amounts = new[] { 100.0, 200.0 };
            var currencies = new[] { _zar, _usd };
            var cashLeg = new CashLeg(dates, amounts, currencies);
            cashLeg.SetValueDate(_valueDate);

            // Act
            var clone = cashLeg.Clone();

            // Assert
            Assert.IsNotNull(clone);
            Assert.AreNotSame(cashLeg, clone);
            
            var originalCfs = cashLeg.GetCFs();
            var clonedCfs = clone.GetCFs();
            Assert.That(clonedCfs, Has.Count.EqualTo(originalCfs.Count));
            
            var cfCurrencies = clone.GetCashflowCurrencies();
            Assert.That(cfCurrencies, Has.Count.EqualTo(2));
            Assert.That(cfCurrencies.Contains(_zar));
            Assert.That(cfCurrencies.Contains(_usd));
        }

        [TestMethod]
        public void CashLeg_Clone_IndependentOfOriginal()
        {
            // Arrange
            var dates = new[] 
            { 
                new Date("2020-03-15"), 
                new Date("2020-06-15") 
            };
            var amounts = new[] { 100.0, 200.0 };
            var currencies = new[] { _zar, _zar };
            var cashLeg = new CashLeg(dates, amounts, currencies);
            cashLeg.SetValueDate(new Date("2020-01-01"));
            
            var clone = (CashLeg)cashLeg.Clone();
            
            // Act - modify the clone's value date
            clone.SetValueDate(new Date("2020-12-31"));

            // Assert - original should still have all cashflows
            var originalCfs = cashLeg.GetCFs();
            var clonedCfs = clone.GetCFs();
            
            Assert.That(originalCfs, Has.Count.EqualTo(2));
            Assert.That(clonedCfs, Has.Count.EqualTo(0));
        }

        [TestMethod]
        public void CashLeg_EmptyCashflows_HandledCorrectly()
        {
            // Arrange
            var dates = new Date[] { };
            var amounts = new double[] { };
            var currencies = new Currency[] { };
            var cashLeg = new CashLeg(dates, amounts, currencies);
            cashLeg.SetValueDate(_valueDate);

            // Act
            var cfs = cashLeg.GetCFs();
            var currencies1 = cashLeg.GetCashflowCurrencies();
            var dates1 = cashLeg.GetCashflowDates(_zar);

            // Assert
            Assert.That(cfs, Has.Count.EqualTo(0));
            Assert.That(currencies1, Has.Count.EqualTo(0));
            Assert.That(dates1, Has.Count.EqualTo(0));
        }

        [TestMethod]
        public void CashLeg_NegativeAmounts_HandledCorrectly()
        {
            // Arrange
            var dates = new[] 
            { 
                new Date("2020-03-15"), 
                new Date("2020-06-15") 
            };
            var amounts = new[] { -1000.0, -2000.0 };
            var currencies = new[] { _zar, _zar };
            var cashLeg = new CashLeg(dates, amounts, currencies);
            cashLeg.SetValueDate(_valueDate);

            // Act
            var result = cashLeg.GetCFs();

            // Assert
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.AreEqual(-1000.0, result[0].Amount, 1e-10);
            Assert.AreEqual(-2000.0, result[1].Amount, 1e-10);
        }

        [TestMethod]
        public void CashLeg_MixedPositiveNegativeAmounts_HandledCorrectly()
        {
            // Arrange
            var dates = new[] 
            { 
                new Date("2020-03-15"), 
                new Date("2020-06-15"), 
                new Date("2020-09-15") 
            };
            var amounts = new[] { 1000.0, -500.0, 1500.0 };
            var currencies = new[] { _zar, _zar, _zar };
            var cashLeg = new CashLeg(dates, amounts, currencies);
            cashLeg.SetValueDate(_valueDate);

            // Act
            var result = cashLeg.GetCFs();

            // Assert
            Assert.That(result, Has.Count.EqualTo(3));
            Assert.AreEqual(1000.0, result[0].Amount, 1e-10);
            Assert.AreEqual(-500.0, result[1].Amount, 1e-10);
            Assert.AreEqual(1500.0, result[2].Amount, 1e-10);
        }
    }
}
