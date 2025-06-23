using System.Collections.Generic;
using QuantSA.Shared.Primitives;
using QuantSA.Shared.Dates;

namespace QuantSA.Core.Products.Rates
{
    public class ZeroCouponBond : ProductWrapper
    {
        private readonly Date _maturityDate;
        private readonly double _notional;
        private readonly Currency _currency;

        public ZeroCouponBond(Date maturityDate, double notional, Currency currency)
        {
            _maturityDate = maturityDate;
            _notional = notional;
            _currency = currency;
        }
        // Required by Product abstract class
        public override List<Cashflow> GetCFs()
        {
            return new List<Cashflow>
            {
                new Cashflow(_maturityDate, _notional, _currency)
            };
        }

        // Required by ProductWrapper behavior
        public List<Cashflow> GetCFs(Date valueDate)
        {
            if (_maturityDate > valueDate)
            {
                return new List<Cashflow>
                {
                    new Cashflow(_maturityDate, _notional, _currency)
                };
            }
            return new List<Cashflow>();
        }



        /*public override List<Cashflow> GetCFs()
        {
            return new List<Cashflow>
            {
                {
                    new Cashflow(_maturityDate, _notional, _currency)
                }
            };
        }*/
    }
}