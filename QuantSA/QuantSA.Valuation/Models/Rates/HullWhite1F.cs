using System;
using System.Collections.Generic;
using System.Linq;
using Accord.Math;
using Accord.Math.Random;
using Accord.Statistics.Distributions.Univariate;
using Newtonsoft.Json;
using QuantSA.Shared.Dates;
using QuantSA.Shared.MarketObservables;
using QuantSA.Shared.Primitives;

namespace QuantSA.Valuation.Models.Rates
{
    /// <summary>
    /// Delegate for the market discount factor function P^M(0,t), which represents the price 
    /// at time 0 (valuation date) of a zero-coupon bond maturing at time t.
    /// </summary>
    /// <param name="date">The date for computing the discount factor from the valuation date.</param>
    /// <returns>
    /// The market zero-coupon bond price from the valuation date (time 0) to the specified date.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This delegate is used in the <see cref="BondPrice"/> calculation to construct the A(t,T) term,
    /// specifically appearing as P^M(0,T) / P^M(0,t) in the analytical bond pricing formula.
    /// </para>
    /// <para>
    /// Current implementation: The default initialization uses a flat curve with 
    /// <c>exp(-_inputRate * t)</c>, where t is the time in years from the valuation date.
    /// This represents a constant continuously compounded interest rate term structure.
    /// </para>
    /// <para>
    /// Extensibility: This delegate can be extended to handle full term structures by providing
    /// a more sophisticated discount curve that reflects market-observed bond prices or 
    /// bootstrapped zero rates across different maturities.
    /// </para>
    /// </remarks>
    public delegate double MarketBonds(Date date);

    /// <summary>
    /// Delegate for the market instantaneous forward rate function f^M(0,t), which represents 
    /// the instantaneous forward rate observed at time 0 for instantaneous borrowing at time t.
    /// </summary>
    /// <param name="date">The date at which to evaluate the instantaneous forward rate.</param>
    /// <returns>
    /// The market instantaneous forward rate at the specified date.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This delegate is used in the <see cref="Theta"/> calculation to determine the drift term
    /// that calibrates the Hull-White model to the input term structure. The Theta function
    /// incorporates f^M(0,t) to ensure that the model-implied forward rates match the market
    /// forward rates, maintaining consistency with the input discount curve.
    /// </para>
    /// <para>
    /// Current implementation: The default initialization uses a constant <c>_inputRate</c>,
    /// representing a flat forward rate curve. This is consistent with the flat discount curve
    /// assumption where all forward rates equal the spot rate.
    /// </para>
    /// <para>
    /// Extensibility: This delegate can be extended to handle full term structures by computing
    /// the instantaneous forward rate from a full discount curve, typically as 
    /// f^M(0,t) = -d/dt[ln(P^M(0,t))], allowing the model to fit arbitrary market term structures.
    /// </para>
    /// </remarks>
    public delegate double MarketForwards(Date date);

    /// <summary>
    /// A single factor Hull White simulator.  It can simulate a numeraire and any number of
    /// forward rates off the same curve.
    /// </summary>
    /// <seealso cref="NumeraireSimulator" />
    public class HullWhite1F : NumeraireSimulator
    {
        private List<FloatRateIndex> _floatRateIndices;
        private readonly double _inputRate;
        private readonly double _a; // mean reversion
        private readonly Currency _currency;
        private readonly double _r0;
        private readonly double _vol;

        [JsonIgnore] private Date _anchorDate;
        [JsonIgnore] private List<Date> _allDates;
        [JsonIgnore] private double[] _allDatesDouble;
        [JsonIgnore] private double[] _bankAccount;
        [JsonIgnore] private MarketForwards _fM;
        [JsonIgnore] private MarketBonds _pm;
        [JsonIgnore] private double[] _r;
        [JsonIgnore] private NormalDistribution _dist;


        /// <summary>
        /// 
        /// </summary>
        /// <param name="currency"></param>
        /// <param name="a"></param>
        /// <param name="vol"></param>
        /// <param name="r0"></param>
        /// <param name="inputRate">the flat continuously compounded rate that is fitted to.</param>
        /// <param name="floatRateIndices"></param>
        public HullWhite1F(Currency currency, double a, double vol, double r0, double inputRate,
            IEnumerable<FloatRateIndex> floatRateIndices = null)
        {
            _a = a;
            _vol = vol;
            _r0 = r0;
            _inputRate = inputRate;
            _floatRateIndices = floatRateIndices != null ? floatRateIndices.ToList() : new List<FloatRateIndex>();
            _currency = currency;
        }

        /// <summary>
        /// Computes the time-dependent drift term theta(t) that calibrates the Hull-White model to match
        /// the input term structure of interest rates.
        /// </summary>
        /// <param name="date">The date representing the time point t for evaluating theta(t).</param>
        /// <returns>The calibrated drift term theta(t) at the specified time point.</returns>
        /// <remarks>
        /// The drift term is given by the formula:
        /// <para/>
        /// theta(t) = a * f^M(0,t) + (vol^2)/(2*a) * (1 - exp(-2*a*t))
        /// <para/>
        /// where f^M(0,t) is the market instantaneous forward rate at time t as seen from time 0.
        /// <para/>
        /// In the current implementation, the market forward rate curve is flat, meaning f^M(0,t)
        /// is constant and equal to the _inputRate field for all t. This corresponds to a flat
        /// continuously compounded zero curve.
        /// <para/>
        /// The theta(t) function serves a crucial calibration purpose: it ensures that the Hull-White
        /// model produces zero-coupon bond prices P^HW(0,T) that exactly match the market bond prices
        /// P^Market(0,T) for all maturities T. This calibration to the initial term structure is
        /// automatic and does not require numerical optimization.
        /// <para/>
        /// Code variable mapping:
        /// - a (mean reversion speed): _a field
        /// - vol (volatility): _vol field
        /// - f^M(0,t) (market forward rate): _fM delegate, which returns _inputRate
        /// - t (time in years): (date - _anchorDate) / 365.0
        /// <para/>
        /// Reference: Brigo &amp; Mercurio, "Interest Rate Models - Theory and Practice", Section 3.3.1
        /// </remarks>
        private double Theta(Date date)
        {
            var t = (date - _anchorDate) / 365.0;
            return _a * _fM(date) + _vol * _vol / (2 * _a) * (1 - Math.Exp(-2 * _a * t));
        }


        /// <summary>
        /// Computes the forward zero-coupon bond price P(t,T;r(t)) using the analytical solution
        /// in the Hull-White one-factor model. This method leverages the affine structure of the
        /// Hull-White framework to obtain closed-form bond prices from the current short rate.
        /// </summary>
        /// <param name="r">The short rate r(t) observed at the current time date1.</param>
        /// <param name="date1">The current time t in the pricing formula.</param>
        /// <param name="date2">The maturity time T of the zero-coupon bond.</param>
        /// <returns>The zero-coupon bond price P(t,T;r(t)) at time t maturing at time T, conditional on the short rate r(t).</returns>
        /// <remarks>
        /// The Hull-White model exhibits an affine term structure, meaning bond prices have the exponential-affine form:
        /// <para>
        ///     P(t,T;r(t)) = A(t,T) * exp(-B(t,T) * r(t))
        /// </para>
        /// where the coefficient functions B(t,T) and A(t,T) are deterministic and given by:
        /// <para>
        ///     B(t,T) = (1/a) * (1 - exp(-a*(T-t)))
        /// </para>
        /// <para>
        ///     A(t,T) = [P^M(0,T) / P^M(0,t)] * exp(B(t,T)*f^M(0,t) - (vol^2)/(4*a)*B(t,T)^2*(1-exp(-2*a*t)))
        /// </para>
        /// Here, a is the mean reversion parameter, vol is the volatility, P^M(0,t) are market discount factors, 
        /// and f^M(0,t) is the instantaneous forward rate from the initial market curve.
        /// <para>
        /// The affine structure is the key property that enables analytical bond pricing in the Hull-White model.
        /// Because bond prices depend exponentially on the short rate (with deterministic coefficients), we can
        /// compute exact prices without numerical integration or approximation. This analytical tractability extends
        /// to options on bonds (swaptions) and other interest rate derivatives.
        /// </para>
        /// <para>
        /// This formula is derived from the fundamental pricing equation and appears as Equation 3.39 in 
        /// Brigo &amp; Mercurio, "Interest Rate Models - Theory and Practice", 2nd edition.
        /// </para>
        /// <para>
        /// Within the simulation framework, this method is called to extract forward rates from simulated short rate 
        /// paths. Given a simulated value of r(t), we compute P(t,T) for the appropriate tenor, then back out the 
        /// forward rate using the relationship: forward rate = 365 * (1/P(t,T) - 1) / (T-t).
        /// </para>
        /// </remarks>
        private double BondPrice(double r, Date date1, Date date2)
        {
            // Equation 3.39 in Brigo Mercurio 2nd edition:
            var T = (date2 - _anchorDate) / 365.0;
            var t = (date1 - _anchorDate) / 365.0;
            var B = 1 / _a * (1 - Math.Exp(-_a * (T - t)));
            var A = _pm(date2) / _pm(date1);
            A *= Math.Exp(B * _fM(date1) - _vol * _vol / (4 * _a) * (1 - Math.Exp(-2 * _a * t)) * B * B);
            return A * Math.Exp(-B * r);
        }

        public override void Reset()
        {
            _allDates = new List<Date>();
        }

        public override void SetRequiredDates(MarketObservable index, List<Date> requiredDates)
        {
            if (_allDates == null) _allDates = requiredDates;
            else
                _allDates.AddRange(requiredDates);
        }

        public override void SetNumeraireDates(List<Date> requiredDates)
        {
            if (_allDates == null) _allDates = requiredDates;
            else
                _allDates.AddRange(requiredDates);
        }

        /// <summary>
        /// Add extra dates to make sure that the minimum spacing is not too large to make the Monte Carlo errors bad.
        /// <para/>
        /// At this point the dates are all copied.
        /// </summary>
        public override void Prepare(Date anchorDate)
        {
            _anchorDate = anchorDate;
            _fM = date => _inputRate;
            _pm = date => Math.Exp(-_inputRate * (date - anchorDate) / 365.0);
            double minStepSize = 20;
            _allDates.Insert(0, anchorDate);
            _allDates = _allDates.Distinct().ToList();
            _allDates.Sort();
            var newDates = new List<Date>();
            newDates.Add(new Date(_allDates[0]));
            for (var i = 1; i < _allDates.Count; i++)
            {
                var nSteps = (int) Math.Floor((_allDates[i] - _allDates[i - 1]) / minStepSize);
                var days = (_allDates[i] - _allDates[i - 1]) / (nSteps + 1);
                for (var j = 0; j < nSteps; j++)
                    newDates.Add(new Date(_allDates[i - 1].AddTenor(Tenor.FromDays((j + 1) * days))));
                newDates.Add(new Date(_allDates[i]));
            }

            _allDates = newDates;
            _allDatesDouble = _allDates.Select(date => (double) date).ToArray();
            _dist = new NormalDistribution();
            Generator.Seed = -1585814591; // This magic number is: "HW1FSimulator".GetHashCode();
        }

        public override void RunSimulation(int simNumber)
        {
            var W = _dist.Generate(_allDates.Count - 1);
            _r = new double[_allDates.Count];
            _bankAccount = new double[_allDates.Count];
            _r[0] = _r0;
            _bankAccount[0] = 1;
            for (var i = 0; i < _allDates.Count - 1; i++)
            {
                var dt = (_allDates[i + 1] - _allDates[i]) / 365.0;
                _r[i + 1] = _r[i] + (Theta(_allDates[i + 1]) - _a * _r[i]) * dt + _vol * Math.Sqrt(dt) * W[i];
                _bankAccount[i + 1] = _bankAccount[i] * Math.Exp(_r[i] * dt);
            }
        }

        public override double[] GetIndices(MarketObservable index, List<Date> requiredDates)
        {
            var floatRateIndex = index as FloatRateIndex;
            var result = new double[requiredDates.Count];
            for (var i = 0; i < requiredDates.Count; i++)
            {
                var rt = Tools.Interpolate1D(requiredDates[i].value, _allDatesDouble, _r, _r[0], _r[_r.Length - 1]);
                var tenor = floatRateIndex.Tenor;
                var date2 = requiredDates[i].AddTenor(tenor);
                var bondPrice = BondPrice(rt, requiredDates[i], date2);
                var rate = 365.0 * (1 / bondPrice - 1) / (date2 - requiredDates[i]);
                result[i] = rate;
            }

            return result;
        }

        public override double[] GetUnderlyingFactors(Date date)
        {
            var rt = Tools.Interpolate1D(date.value, _allDatesDouble, _r, _r[0], _r[_r.Length - 1]);
            return new[] {rt};
        }

        public override Currency GetNumeraireCurrency()
        {
            return _currency;
        }

        public override double Numeraire(Date valueDate)
        {
            if (valueDate < _anchorDate)
                throw new ArgumentException(
                    $"Numeraire requested at: {valueDate} but model only starts at {_anchorDate}");
            if (valueDate == _anchorDate) return 1.0;
            return Tools.Interpolate1D(valueDate, _allDatesDouble, _bankAccount, 1, _bankAccount.Last());
        }

        public override bool ProvidesIndex(MarketObservable index)
        {
            return _floatRateIndices.Contains(index);
        }

        public void AddForecast(FloatRateIndex index)
        {
            if (_floatRateIndices == null) _floatRateIndices = new List<FloatRateIndex>();
            _floatRateIndices.Add(index);
        }
    }
}