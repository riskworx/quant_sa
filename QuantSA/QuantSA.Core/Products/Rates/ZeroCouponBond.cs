using System.Collections.Generic;
using QuantSA.Shared.Primitives;
using QuantSA.Shared.Dates;
using System;

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

        public override void SetValueDate(Date valueDate)
        {
            this.valueDate = valueDate; 
        }
        public override List<Cashflow> GetCFs()
        {
            Console.WriteLine($"valueDate: {valueDate}, maturity: {_maturityDate}, comparison: {_maturityDate <= valueDate}");
            if (_maturityDate <= valueDate)
            {
                return new List<Cashflow>();
            }

            return new List<Cashflow>
            {
                new Cashflow(_maturityDate, _notional, _currency)
            };
        }
    }
}