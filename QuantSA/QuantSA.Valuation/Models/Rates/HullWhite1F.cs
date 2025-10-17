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
    public delegate double MarketBonds(Date date);

    public delegate double MarketForwards(Date date);

    /// <summary>
    /// A single-factor Hull-White short rate model simulator for Monte Carlo valuation of 
    /// interest rate derivatives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Hull-White one-factor model (also known as the extended Vasicek model) describes the 
    /// evolution of the instantaneous short rate r(t) under the risk-neutral measure through 
    /// the following stochastic differential equation (SDE):
    /// </para>
    /// <para>
    ///     dr(t) = [theta(t) - a*r(t)]*dt + vol*dW(t)
    /// </para>
    /// <para>
    /// where W(t) is a standard Brownian motion under the risk-neutral measure.
    /// </para>
    /// 
    /// <para><b>Model Characteristics</b></para>
    /// <para>
    /// The Hull-White model is a mean-reverting short rate model belonging to the affine term 
    /// structure class. The mean-reverting property ensures that extreme interest rate values 
    /// are pulled back toward a time-varying central tendency, preventing unbounded growth or 
    /// negative explosions. The affine structure means that zero-coupon bond prices are exponential 
    /// affine functions of the short rate, enabling closed-form solutions for bond prices and 
    /// European-style derivatives.
    /// </para>
    /// 
    /// <para><b>Parameters and Their Interpretations</b></para>
    /// <para>
    /// <b>a</b> (mean reversion speed): Controls how quickly the short rate reverts to its 
    /// mean level. Higher values of 'a' lead to faster mean reversion and shorter memory of 
    /// past shocks. Typical values range from 0.01 to 0.5 (annualized). When a = 0, the model 
    /// reduces to the Ho-Lee model with no mean reversion.
    /// </para>
    /// <para>
    /// <b>vol</b> (short rate volatility): The instantaneous volatility of the short rate, 
    /// expressed in absolute terms (e.g., 0.01 for 100 basis points). This parameter controls 
    /// the width of the distribution of future short rates. Higher volatility increases option 
    /// values but also the likelihood of negative interest rates, which is a known limitation 
    /// of the model.
    /// </para>
    /// <para>
    /// <b>theta(t)</b> (time-dependent drift): A deterministic function of time that ensures 
    /// the model fits exactly to the observed initial term structure of interest rates. 
    /// Mathematically, theta(t) is calibrated so that the model-implied discount curve matches 
    /// the market discount curve at time zero. The function theta(t) absorbs all the information 
    /// about the initial yield curve shape, allowing the model to be consistent with market 
    /// prices while 'a' and 'vol' control the future dynamics.
    /// </para>
    /// 
    /// <para><b>Current Implementation</b></para>
    /// <para>
    /// This implementation fits the model to a flat continuously compounded interest rate curve, 
    /// which is a simplification for practical convenience. In this case, theta(t) is derived 
    /// analytically from the flat input rate. The implementation can be extended to fit a full 
    /// term structure by providing a more general curve object and calibrating theta(t) 
    /// accordingly. The flat curve assumption means that all discount factors are computed as 
    /// exp(-inputRate * T) where T is the time to maturity.
    /// </para>
    /// 
    /// <para><b>Theory-to-Code Mapping</b></para>
    /// <para>
    /// This class inherits from <see cref="NumeraireSimulator"/> and implements Monte Carlo 
    /// simulation of the Hull-White model. The correspondence between mathematical notation 
    /// and code is as follows:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// The short rate r(t) is simulated and stored in the <c>_r</c> array across all simulation 
    /// time steps.
    /// </description></item>
    /// <item><description>
    /// The bank account (numeraire) B(t) = exp(integral from 0 to t of r(s)ds) is computed and 
    /// stored in the <c>_bankAccount</c> array. This represents the value of a risk-free money 
    /// market account.
    /// </description></item>
    /// <item><description>
    /// The mean reversion speed parameter 'a' is stored in <c>_a</c>.
    /// </description></item>
    /// <item><description>
    /// The volatility parameter 'vol' is stored in <c>_vol</c>.
    /// </description></item>
    /// <item><description>
    /// The time-dependent drift theta(t) is computed in the <see cref="Theta"/> method using 
    /// the calibration formula that ensures fit to the input term structure.
    /// </description></item>
    /// <item><description>
    /// Zero-coupon bond prices are computed using the closed-form formula in the 
    /// <see cref="BondPrice"/> method (Equation 3.39 in the reference below).
    /// </description></item>
    /// <item><description>
    /// Forward rate indices (LIBOR-like rates) are computed from bond prices using the standard 
    /// relationship: F(t,T1,T2) = (P(t,T1)/P(t,T2) - 1) * (day_count / (T2-T1)).
    /// </description></item>
    /// </list>
    /// 
    /// <para><b>Reference</b></para>
    /// <para>
    /// For a comprehensive treatment of the Hull-White model, including derivations of bond 
    /// pricing formulas, calibration procedures, and extensions, see:
    /// </para>
    /// <para>
    /// Brigo, D., and Mercurio, F. (2006). <i>Interest Rate Models - Theory and Practice: 
    /// With Smile, Inflation and Credit</i> (2nd ed.). Springer Finance. Chapter 3: 
    /// One-factor short-rate models.
    /// </para>
    /// </remarks>
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

        private double Theta(Date date)
        {
            var t = (date - _anchorDate) / 365.0;
            return _a * _fM(date) + _vol * _vol / (2 * _a) * (1 - Math.Exp(-2 * _a * t));
        }


        /// <summary>
        /// Forward zero coupon bond price between <paramref name="date1"/> and <paramref name="date2"/> given
        /// that <paramref name="r"/> has been observed at <paramref name="date1"/>
        /// </summary>
        /// <param name="r"></param>
        /// <param name="date1"></param>
        /// <param name="date2"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Registers a forward rate index for simulation, such as 3M JIBAR or 3M LIBOR.
        /// </summary>
        /// <param name="index">The FloatRateIndex object to add (e.g., FloatRateIndex representing 3M JIBAR).</param>
        /// <remarks>
        /// This method allows multiple forward rate indices to be registered with the Hull-White model for simulation.
        /// After indices are registered, they are simulated alongside the short rate process when <see cref="RunSimulation"/> is called.
        /// <para/>
        /// The forward rates are extracted from the simulated short rate r(t) using the analytical bond pricing formula
        /// described in Brigo and Mercurio (see <see cref="BondPrice"/>). Specifically, for a given observation date and tenor,
        /// the model calculates the zero-coupon bond price P(t, T) and derives the forward rate as: rate = 365 * (1/P(t,T) - 1) / (T - t).
        /// <para/>
        /// Multiple indices can be added by calling this method repeatedly. There is no limit on the number of indices that can be registered.
        /// Once registered, indices can be queried after simulation via the <see cref="GetIndices"/> method.
        /// <para/>
        /// Typical workflow: Registration (AddForecast) → Simulation setup (Reset, SetRequiredDates, Prepare) → Simulation (RunSimulation) → Query (GetIndices).
        /// </remarks>
        public void AddForecast(FloatRateIndex index)
        {
            if (_floatRateIndices == null) _floatRateIndices = new List<FloatRateIndex>();
            _floatRateIndices.Add(index);
        }
    }
}