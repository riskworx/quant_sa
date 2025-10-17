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

        /// <summary>
        /// Clears all stored simulation dates to prepare the model for reuse.
        /// </summary>
        /// <remarks>
        /// This method allows the simulator to be reconfigured with new dates without creating a new instance.
        /// It is the first step in the date management lifecycle: Reset → SetRequiredDates/SetNumeraireDates → Prepare → RunSimulation.
        /// </remarks>
        public override void Reset()
        {
            _allDates = new List<Date>();
        }

        /// <summary>
        /// Registers dates where a specific market observable will be queried during simulation.
        /// </summary>
        /// <param name="index">The market observable (typically a <see cref="FloatRateIndex"/>) to track.</param>
        /// <param name="requiredDates">List of dates where this index is needed for valuation.</param>
        /// <remarks>
        /// The simulator collects all required dates from multiple calls to this method to build a complete simulation timeline.
        /// These dates are sorted and merged with numeraire dates during the <see cref="Prepare"/> call.
        /// This method can be called multiple times for different indices or with additional dates for the same index.
        /// </remarks>
        public override void SetRequiredDates(MarketObservable index, List<Date> requiredDates)
        {
            if (_allDates == null) _allDates = requiredDates;
            else
                _allDates.AddRange(requiredDates);
        }

        /// <summary>
        /// Registers dates where numeraire (money market account) values are required.
        /// </summary>
        /// <param name="requiredDates">Dates where <see cref="Numeraire"/> will be called during valuation.</param>
        /// <remarks>
        /// The numeraire represents the bank account value B(t) = exp(integral_0^t r(s)ds), where r(s) is the short rate process.
        /// Numeraire values are needed for discounting payoffs to present value in Monte Carlo valuation.
        /// These dates are merged with index observation dates during the <see cref="Prepare"/> call to create the complete simulation timeline.
        /// </remarks>
        public override void SetNumeraireDates(List<Date> requiredDates)
        {
            if (_allDates == null) _allDates = requiredDates;
            else
                _allDates.AddRange(requiredDates);
        }

        /// <summary>
        /// Performs the final pre-simulation setup step, preparing the model for Monte Carlo simulation 
        /// by consolidating all required dates, adding interpolation dates, and initializing market functions.
        /// </summary>
        /// <param name="anchorDate">The valuation date and simulation start date from which all time calculations 
        /// are measured. This is the reference point (t=0) for the Hull-White model.</param>
        /// <remarks>
        /// This method must be called after all dates have been registered via <see cref="SetRequiredDates"/> 
        /// and <see cref="SetNumeraireDates"/>, but before <see cref="RunSimulation"/>. It performs the following 
        /// setup operations:
        /// <para/>
        /// <b>Date Merging and Sorting:</b> Combines all dates registered from SetRequiredDates and SetNumeraireDates, 
        /// removes duplicates, and sorts them chronologically with the anchorDate at position zero. This creates the 
        /// base timeline for simulation.
        /// <para/>
        /// <b>Interpolation Date Insertion:</b> Adds intermediate dates between existing dates when gaps exceed 20 days. 
        /// The 20-day minimum step size controls discretization error in the Euler scheme used by RunSimulation, 
        /// ensuring that the continuous-time Hull-White SDE is approximated with sufficient accuracy. Larger steps 
        /// would increase Monte Carlo discretization bias.
        /// <para/>
        /// <b>Market Function Initialization:</b> Initializes the forward rate function _fM and discount factor 
        /// function _pm. In the current implementation, _fM returns a flat rate equal to _inputRate for any date, 
        /// and _pm returns exp(-_inputRate * t) where t is time in years from anchorDate. These functions can be 
        /// extended to support full term structure curves.
        /// <para/>
        /// <b>Array Allocation Framework:</b> Sets up the _allDates array containing the complete simulation timeline. 
        /// This array determines the size for _r (short rate path) and _bankAccount (numeraire path) arrays that will 
        /// be allocated in RunSimulation based on _allDates.Count.
        /// <para/>
        /// <b>Complete Simulation Timeline:</b> The _allDates array holds all simulation dates in chronological order, 
        /// including the anchorDate, all required dates from products and numeraire calculations, and all interpolated 
        /// dates. This timeline is stored both as Date objects (_allDates) and as double values (_allDatesDouble) for 
        /// efficient interpolation during simulation.
        /// <para/>
        /// <b>Method Sequencing:</b> The simulator lifecycle requires this ordering: (1) Reset clears previous state, 
        /// (2) SetRequiredDates and SetNumeraireDates register all needed dates, (3) Prepare consolidates dates and 
        /// initializes functions, (4) RunSimulation executes the Monte Carlo path generation. Calling Prepare before 
        /// dates are registered or calling RunSimulation before Prepare will produce incorrect results.
        /// </remarks>
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

        /// <summary>
        /// Executes a single Monte Carlo path simulation using the Euler-Maruyama discretization scheme
        /// to generate the short rate process and numeraire (bank account) values along the simulation timeline.
        /// </summary>
        /// <param name="simNumber">The simulation path identifier, used as a random seed or path index for reproducible path generation.</param>
        /// <remarks>
        /// <para>
        /// This method implements the Euler-Maruyama discretization of the Hull-White short rate SDE.
        /// The discrete-time update scheme for the short rate r is:
        /// </para>
        /// <para>
        /// r[i+1] = r[i] + [theta(t[i]) - a*r[i]]*dt + vol*sqrt(dt)*Z[i]
        /// </para>
        /// <para>
        /// where Z[i] ~ N(0,1) are independent standard normal random variables (stored in W array in code).
        /// </para>
        /// <para>
        /// This discretization approximates the continuous-time stochastic differential equation:
        /// </para>
        /// <para>
        /// dr(t) = [theta(t) - a*r(t)]*dt + vol*dW(t)
        /// </para>
        /// <para>
        /// where theta(t) is the time-dependent drift term (obtained from the Theta method), a is the mean
        /// reversion speed (_a), vol is the volatility (_vol), and dW(t) is the increment of a standard
        /// Brownian motion.
        /// </para>
        /// <para>
        /// The bank account (numeraire) B evolves according to:
        /// </para>
        /// <para>
        /// B[i+1] = B[i] * exp(r[i] * dt)
        /// </para>
        /// <para>
        /// where dt is the time step size in years between consecutive dates.
        /// </para>
        /// <para>
        /// Initial conditions are r[0] = _r0 (initial short rate) and B[0] = 1 (unit numeraire at anchor date).
        /// </para>
        /// <para>
        /// The time discretization uses a minimum step size of 20 days (set in the Prepare method) to control
        /// discretization errors. This ensures the Euler-Maruyama scheme provides accurate approximations of
        /// the continuous-time process.
        /// </para>
        /// <para>
        /// The complete paths are stored in the _r array (short rate values) and _bankAccount array (numeraire values)
        /// for all dates in _allDates. These stored paths are subsequently accessed by query methods such as
        /// GetIndices (for forward rate extraction) and Numeraire (for numeraire value retrieval).
        /// </para>
        /// </remarks>
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

        /// <summary>
        /// Extracts forward rates from the simulated short rate path by computing bond prices and 
        /// converting them to simple interest rates.
        /// </summary>
        /// <param name="index">The FloatRateIndex for which forward rates are requested. This index 
        /// must be pre-registered with the model via the <see cref="AddForecast"/> method.</param>
        /// <param name="requiredDates">The dates at which forward rates are needed.</param>
        /// <returns>An array of simple interest rates (not continuously compounded), annualized using 
        /// the Act/365 day count convention.</returns>
        /// <remarks>
        /// This method extracts forward rates that are consistent with the simulated short rate path.
        /// The extraction process works as follows:
        /// <para/>
        /// 1. Forward rate extraction via BondPrice method: For each required date t, the method uses 
        /// the simulated short rate r(t) to compute the bond price P(t, t+tenor) using the 
        /// <see cref="BondPrice"/> method, where tenor is the index's tenor (e.g., 3 months for 3M LIBOR).
        /// <para/>
        /// 2. Bond-to-rate conversion formula: The bond price is converted to a simple interest rate 
        /// using the formula: rate = 365 * (1/P - 1) / days, where days is the actual number of days 
        /// between t and t+tenor.
        /// <para/>
        /// 3. Tenor definition: The tenor is determined by the FloatRateIndex (e.g., 3M LIBOR has a 
        /// 3-month tenor). This tenor defines the forward period over which the rate applies.
        /// <para/>
        /// 4. Day count: The conversion uses actual days between the reset date t and maturity date 
        /// t+tenor, computed directly from the Date objects.
        /// <para/>
        /// 5. Consistency guarantee: The extracted forward rates are consistent with the simulated 
        /// short rate path r(t), ensuring that the rates reflect the same underlying stochastic evolution 
        /// generated by the Hull-White model.
        /// <para/>
        /// 6. Rate convention: Returns simple rates (market quoting convention) rather than continuously 
        /// compounded rates. The rates are annualized using Act/365 basis (365 days per year).
        /// </remarks>
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

        /// <summary>
        /// Returns the short rate r(t) at the specified date for use in regression-based valuation algorithms.
        /// </summary>
        /// <param name="date">The date at which to query the short rate r(t).</param>
        /// <returns>
        /// An array containing a single element with the value of the short rate r(t) at the specified date.
        /// </returns>
        /// <remarks>
        /// This method is primarily used in Longstaff-Schwartz regression algorithms where the short rate
        /// serves as the state variable for computing continuation values. The short rate r(t) represents
        /// the instantaneous risk-free rate and is the fundamental state variable in the Hull-White model.
        /// </remarks>
        public override double[] GetUnderlyingFactors(Date date)
        {
            var rt = Tools.Interpolate1D(date.value, _allDatesDouble, _r, _r[0], _r[_r.Length - 1]);
            return new[] {rt};
        }

        /// <summary>
        /// Returns the currency of the numeraire (money market account).
        /// </summary>
        /// <returns>
        /// The <see cref="Currency"/> object representing the currency in which the numeraire is denominated.
        /// </returns>
        /// <remarks>
        /// All cash flows being valued must be denominated in this currency for the valuation to be valid.
        /// The numeraire provides the unit of account for risk-neutral pricing in this currency.
        /// </remarks>
        public override Currency GetNumeraireCurrency()
        {
            return _currency;
        }

        /// <summary>
        /// Returns the value of the money market account B(t) at the specified date.
        /// </summary>
        /// <param name="valueDate">The date at which to query the money market account value B(t).</param>
        /// <returns>
        /// The value of the bank account B(t) = exp(integral from 0 to t of r(s)ds), where r(s) is the 
        /// short rate process. By construction, B(0) = 1.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The numeraire is a fundamental concept in risk-neutral pricing. It represents the value of a 
        /// bank account that continuously compounds at the risk-free short rate r(t). In the Hull-White 
        /// model, this is the money market account.
        /// </para>
        /// <para>
        /// A key property of the numeraire is the martingale property: when any payoff is divided by 
        /// B(t), the resulting process is a martingale under the risk-neutral measure. This property 
        /// underpins the risk-neutral valuation framework where the expected value (under the risk-neutral 
        /// measure) of a payoff X(T) discounted by the numeraire equals its present value: 
        /// V(0) = E[X(T)/B(T)] * B(0).
        /// </para>
        /// <para>
        /// The computation method used here is iterative during the <see cref="RunSimulation"/> method, 
        /// where the bank account is updated at each time step via the recursion: 
        /// B[i+1] = B[i] * exp(r[i] * dt). This provides a discrete-time approximation to the continuous 
        /// integral formula above.
        /// </para>
        /// </remarks>
        public override double Numeraire(Date valueDate)
        {
            if (valueDate < _anchorDate)
                throw new ArgumentException(
                    $"Numeraire requested at: {valueDate} but model only starts at {_anchorDate}");
            if (valueDate == _anchorDate) return 1.0;
            return Tools.Interpolate1D(valueDate, _allDatesDouble, _bankAccount, 1, _bankAccount.Last());
        }

        /// <summary>
        /// Checks if this simulator can provide values for the specified market observable.
        /// </summary>
        /// <param name="index">The market observable to check.</param>
        /// <returns>
        /// True if the specified index was registered with this simulator via <see cref="AddForecast"/>, 
        /// false otherwise.
        /// </returns>
        /// <remarks>
        /// This method is used by the simulation coordinator to determine which simulator should handle
        /// queries for a particular market observable. The coordinator routes index queries to the 
        /// appropriate simulator based on the results of this method.
        /// </remarks>
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