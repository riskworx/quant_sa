using QuantSA.Core.Primitives;
using QuantSA.Core.Products.Rates;
using QuantSA.Shared.MarketData;

namespace QuantSA.CoreExtensions.ProductPVs.Rates
{
    public static class FraPVs
    {
        /// <summary>
        /// Values the FRA by discounting the generated cashflow.
        /// </summary>
        public static double CurvePv(this FRA fra, IDiscountingSource discountCurve)
        {
            var cfs = fra.GetCFs();  // internally uses _nearDate, _rate, _accrualFraction etc.
            return cfs.PV(discountCurve);  // extension method in QuantSA
        }
    }
}