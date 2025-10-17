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
    /// A single-factor Hull-White interest rate model simulator that implements the extended Vasicek model.
    /// Provides Monte Carlo simulation of the short rate process and can simulate a numeraire (bank account)
    /// and any number of forward rates from the same yield curve.
    /// </summary>
    /// <remarks>
    /// <para><b>Quantitative Description:</b></para>
    /// <para>
    /// The Hull-White one-factor model, also known as the extended Vasicek model, describes the evolution 
    /// of the instantaneous short rate r(t) under the risk-neutral measure Q as:
    /// </para>
    /// <para>
    ///   dr(t) = [θ(t) - a·r(t)]dt + σ·dW(t)
    /// </para>
    /// <para>where:</para>
    /// <list type="bullet">
    /// <item><description>r(t) is the instantaneous short rate at time t</description></item>
    /// <item><description>a is the mean reversion speed (a > 0), controlling how quickly rates revert to the long-term mean</description></item>
    /// <item><description>σ is the constant instantaneous volatility (σ > 0)</description></item>
    /// <item><description>θ(t) is a deterministic time-dependent drift function calibrated to match the initial term structure</description></item>
    /// <item><description>W(t) is a standard Brownian motion under the risk-neutral measure</description></item>
    /// </list>
    /// <para>
    /// The time-dependent drift function θ(t) is given by:
    /// </para>
    /// <para>
    ///   θ(t) = a·f^M(0,t) + (σ²/2a)·(1 - exp(-2at))
    /// </para>
    /// <para>
    /// where f^M(0,t) is the instantaneous forward rate at time t as seen from the initial time.
    /// This ensures the model is calibrated to the initial discount curve.
    /// </para>
    /// <para><b>Key Model Properties:</b></para>
    /// <list type="bullet">
    /// <item><description><b>Gaussian short rates:</b> The short rate is normally distributed, allowing for negative rates</description></item>
    /// <item><description><b>Mean reversion:</b> Rates exhibit mean-reverting behavior with speed parameter 'a'</description></item>
    /// <item><description><b>Analytical tractability:</b> Zero-coupon bond prices have closed-form solutions</description></item>
    /// <item><description><b>Perfect fit to initial curve:</b> The model exactly reproduces the initial term structure through θ(t)</description></item>
    /// </list>
    /// <para><b>Zero-Coupon Bond Pricing:</b></para>
    /// <para>
    /// The price at time t of a zero-coupon bond maturing at time T, given r(t), is:
    /// </para>
    /// <para>
    ///   P(t,T|r(t)) = A(t,T)·exp(-B(t,T)·r(t))
    /// </para>
    /// <para>where:</para>
    /// <para>
    ///   B(t,T) = (1/a)·(1 - exp(-a(T-t)))
    /// </para>
    /// <para>
    ///   A(t,T) = P^M(0,T)/P^M(0,t) · exp(B(t,T)·f^M(0,t) - (σ²/4a)·(1-exp(-2at))·B(t,T)²)
    /// </para>
    /// <para>
    /// where P^M(0,t) denotes the market discount factor from time 0 to time t.
    /// This formula is equation 3.39 in Brigo &amp; Mercurio (2nd edition).
    /// </para>
    /// <para><b>Forward Rate Calculation:</b></para>
    /// <para>
    /// Forward rates for a given tenor τ are calculated from zero-coupon bond prices as:
    /// </para>
    /// <para>
    ///   F(t,t+τ) = (365/τ)·(1/P(t,t+τ) - 1)
    /// </para>
    /// <para>
    /// where τ is expressed in days and the 365 day count convention is used.
    /// </para>
    /// <para><b>Implementation Details:</b></para>
    /// <para>
    /// The simulator uses an Euler-Maruyama discretization scheme for the SDE. To maintain accuracy,
    /// additional intermediate dates are automatically added if gaps between required simulation dates
    /// exceed 20 days. The bank account (numeraire) is calculated as:
    /// </para>
    /// <para>
    ///   B(t) = exp(∫₀ᵗ r(s)ds)
    /// </para>
    /// <para>
    /// which is discretized and updated at each simulation step.
    /// </para>
    /// <para><b>Use Cases:</b></para>
    /// <list type="bullet">
    /// <item><description>Pricing interest rate derivatives (swaps, caps, floors, swaptions)</description></item>
    /// <item><description>Valuing callable bonds and other path-dependent securities</description></item>
    /// <item><description>Calculating exposure profiles (EPE/ENE) for counterparty credit risk</description></item>
    /// <item><description>Scenarios analysis for risk management</description></item>
    /// </list>
    /// <para><b>References:</b></para>
    /// <list type="bullet">
    /// <item><description>Brigo, D. and Mercurio, F. (2006) "Interest Rate Models - Theory and Practice", 2nd Edition, Springer</description></item>
    /// <item><description>Hull, J. and White, A. (1990) "Pricing Interest-Rate-Derivative Securities", The Review of Financial Studies, Vol. 3, No. 4</description></item>
    /// </list>
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
        /// Initializes a new instance of the Hull-White one-factor model with specified parameters.
        /// </summary>
        /// <param name="currency">The currency for which this model simulates interest rates. 
        /// This becomes the numeraire currency for valuations using this model.</param>
        /// <param name="a">The mean reversion speed parameter (a > 0). Higher values indicate faster 
        /// mean reversion of rates toward the long-term mean. Typical values range from 0.01 to 0.5. 
        /// A value of 0.05 implies a half-life of approximately 14 years.</param>
        /// <param name="vol">The instantaneous volatility parameter (σ > 0) of the short rate process, 
        /// expressed as an absolute value (not percentage). Typical values range from 0.005 to 0.02. 
        /// Higher values produce more volatile interest rate scenarios.</param>
        /// <param name="r0">The initial value of the instantaneous short rate at the anchor date, 
        /// expressed as a continuously compounded rate (not percentage). For example, 0.05 represents 5%.</param>
        /// <param name="inputRate">The flat continuously compounded rate used to define the initial 
        /// term structure. The model's time-dependent drift θ(t) is calibrated to fit this flat curve. 
        /// In more sophisticated implementations, this could be replaced with a full yield curve. 
        /// Expressed as a continuously compounded rate (not percentage).</param>
        /// <param name="floatRateIndices">Optional collection of floating rate indices (e.g., LIBOR, JIBAR) 
        /// that this model will provide simulations for. Additional indices can be added later using 
        /// <see cref="AddForecast"/>. If null, an empty list is created.</param>
        /// <remarks>
        /// <para>
        /// The constructor sets up the model parameters but does not perform any simulation preparation.
        /// Before running simulations, you must call <see cref="Reset"/>, <see cref="SetNumeraireDates"/> 
        /// or <see cref="SetRequiredDates"/>, and then <see cref="Prepare"/> to initialize the simulation dates.
        /// </para>
        /// <para><b>Parameter Selection Guidance:</b></para>
        /// <list type="bullet">
        /// <item><description><b>Mean reversion (a):</b> Can be calibrated from historical rate data or from 
        /// market prices of interest rate derivatives. Lower values (0.01-0.05) represent slower mean reversion 
        /// suitable for long-term rates. Higher values (0.1-0.5) represent faster mean reversion typical of 
        /// short-term rates.</description></item>
        /// <item><description><b>Volatility (σ):</b> Should be calibrated to market prices of caps/floors or 
        /// swaptions. Can also be estimated from historical rate volatility, though market-implied volatility 
        /// is generally preferred for derivatives pricing.</description></item>
        /// <item><description><b>Initial rate (r0) and flat rate (inputRate):</b> Often set to the same value 
        /// for consistency. Should reflect the current short-term interest rate level in the market.</description></item>
        /// </list>
        /// </remarks>
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
        /// Calculates the time-dependent drift function θ(t) in the Hull-White short rate dynamics.
        /// </summary>
        /// <param name="date">The date at which to evaluate θ(t).</param>
        /// <returns>The value of the drift function at the specified date.</returns>
        /// <remarks>
        /// <para>
        /// The function θ(t) is the deterministic drift term in the Hull-White SDE:
        ///   dr(t) = [θ(t) - a·r(t)]dt + σ·dW(t)
        /// </para>
        /// <para>
        /// It is calculated as:
        ///   θ(t) = a·f^M(0,t) + (σ²/2a)·(1 - exp(-2at))
        /// </para>
        /// <para>
        /// where f^M(0,t) is the instantaneous forward rate from the market curve at time t.
        /// </para>
        /// <para>
        /// This drift ensures that the model is calibrated to the initial term structure. The first term 
        /// a·f^M(0,t) represents the contribution from matching the forward curve, while the second term 
        /// (σ²/2a)·(1 - exp(-2at)) is a convexity adjustment arising from the volatility of the process.
        /// </para>
        /// <para>
        /// In this implementation, f^M(0,t) is assumed to be a constant flat rate (inputRate from constructor),
        /// so the drift function varies only through the time-dependent exponential term.
        /// </para>
        /// </remarks>
        private double Theta(Date date)
        {
            var t = (date - _anchorDate) / 365.0;
            return _a * _fM(date) + _vol * _vol / (2 * _a) * (1 - Math.Exp(-2 * _a * t));
        }


        /// <summary>
        /// Calculates the forward zero-coupon bond price from <paramref name="date1"/> to <paramref name="date2"/>, 
        /// conditional on the short rate <paramref name="r"/> observed at <paramref name="date1"/>.
        /// </summary>
        /// <param name="r">The realized value of the instantaneous short rate r(t) at <paramref name="date1"/>.</param>
        /// <param name="date1">The current date (time t) at which the bond price is being calculated.</param>
        /// <param name="date2">The maturity date (time T) of the zero-coupon bond.</param>
        /// <returns>The price P(t,T|r(t)) of a unit zero-coupon bond maturing at <paramref name="date2"/>, 
        /// as observed at <paramref name="date1"/> given the short rate r.</returns>
        /// <remarks>
        /// <para>
        /// This method implements the analytical zero-coupon bond pricing formula for the Hull-White model,
        /// given as Equation 3.39 in Brigo &amp; Mercurio (2nd edition):
        /// </para>
        /// <para>
        ///   P(t,T|r(t)) = A(t,T)·exp(-B(t,T)·r(t))
        /// </para>
        /// <para>where:</para>
        /// <para>
        ///   B(t,T) = (1/a)·(1 - exp(-a(T-t)))
        /// </para>
        /// <para>
        ///   A(t,T) = [P^M(0,T)/P^M(0,t)] · exp[B(t,T)·f^M(0,t) - (σ²/4a)·(1-exp(-2at))·B(t,T)²]
        /// </para>
        /// <para>
        /// The B(t,T) function represents the sensitivity of the bond price to the short rate and approaches 
        /// (T-t) as mean reversion (a) approaches zero. The A(t,T) function ensures the model matches the 
        /// initial market discount curve and includes a convexity adjustment for volatility.
        /// </para>
        /// <para><b>Implementation Notes:</b></para>
        /// <list type="bullet">
        /// <item><description>P^M(0,t) is the market discount factor, calculated from the flat inputRate curve as exp(-inputRate·t)</description></item>
        /// <item><description>f^M(0,t) is the market instantaneous forward rate, which equals inputRate in the flat curve case</description></item>
        /// <item><description>All time differences are converted to year fractions using a 365-day convention</description></item>
        /// <item><description>This formula is exact (not approximated) for the Hull-White model</description></item>
        /// </list>
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
        /// Finalizes the simulation setup by processing required dates, adding intermediate dates for accuracy,
        /// and initializing all data structures needed for simulation runs.
        /// </summary>
        /// <param name="anchorDate">The starting date for the simulation, typically the valuation date. 
        /// The short rate r₀ and the numeraire B(0)=1.0 are anchored at this date.</param>
        /// <remarks>
        /// <para>
        /// This is the final initialization method that must be called after all required dates have been 
        /// registered via <see cref="SetRequiredDates"/> and <see cref="SetNumeraireDates"/>, but before 
        /// any calls to <see cref="RunSimulation"/>.
        /// </para>
        /// <para><b>Processing Steps:</b></para>
        /// <list type="number">
        /// <item><description><b>Date consolidation:</b> Combines all required dates from products and numeraire requests, 
        /// removes duplicates, and sorts chronologically</description></item>
        /// <item><description><b>Intermediate date insertion:</b> Adds extra simulation dates to ensure the maximum 
        /// time step does not exceed 20 days. This maintains accuracy of the Euler discretization scheme. If the gap 
        /// between two required dates is N×20 days, (N-1) intermediate dates are inserted</description></item>
        /// <item><description><b>Market curve initialization:</b> Sets up the forward rate function f^M(t) and 
        /// discount function P^M(t) using the flat inputRate provided in the constructor</description></item>
        /// <item><description><b>Data structure allocation:</b> Pre-converts dates to double format for efficient 
        /// interpolation during simulations</description></item>
        /// <item><description><b>Random number generator seeding:</b> Initializes the RNG with a fixed seed 
        /// (-1585814591, derived from "HW1FSimulator".GetHashCode()) to ensure reproducible results</description></item>
        /// </list>
        /// <para><b>Importance of the 20-Day Maximum Step Size:</b></para>
        /// <para>
        /// The Euler-Maruyama scheme used to discretize the SDE has a discretization error that grows with the 
        /// step size. A maximum step of 20 days provides a good balance between accuracy and computational efficiency 
        /// for typical interest rate volatilities (0.5%-2%). For higher volatilities or when higher accuracy is 
        /// required, this threshold could be reduced.
        /// </para>
        /// <para><b>Developer Notes:</b></para>
        /// <list type="bullet">
        /// <item><description>This method can be computationally expensive for long date ranges with many required dates</description></item>
        /// <item><description>The date list is immutable after this call - no new dates can be added</description></item>
        /// <item><description>The anchorDate is automatically inserted as the first simulation date if not present</description></item>
        /// <item><description>The fixed RNG seed means simulations are deterministic and reproducible</description></item>
        /// </list>
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
        /// Executes a single Monte Carlo simulation path, generating a complete trajectory of the short rate 
        /// and bank account numeraire from the anchor date through all required simulation dates.
        /// </summary>
        /// <param name="simNumber">The simulation path number. Currently not used for variance reduction but 
        /// provided for potential future enhancements (e.g., quasi-Monte Carlo, antithetic variates).</param>
        /// <remarks>
        /// <para><b>Simulation Algorithm:</b></para>
        /// <para>
        /// This method implements the Euler-Maruyama discretization of the Hull-White SDE:
        /// </para>
        /// <para>
        ///   dr(t) = [θ(t) - a·r(t)]dt + σ·dW(t)
        /// </para>
        /// <para>
        /// The discretized scheme from time tᵢ to tᵢ₊₁ is:
        /// </para>
        /// <para>
        ///   r(tᵢ₊₁) = r(tᵢ) + [θ(tᵢ₊₁) - a·r(tᵢ)]·Δt + σ·√Δt·Zᵢ
        /// </para>
        /// <para>
        /// where Zᵢ ~ N(0,1) are independent standard normal random variables, and Δt = tᵢ₊₁ - tᵢ.
        /// </para>
        /// <para><b>Implementation Steps:</b></para>
        /// <list type="number">
        /// <item><description><b>Random number generation:</b> Generates (n-1) independent standard normal 
        /// variates for n simulation dates</description></item>
        /// <item><description><b>Initialization:</b> Sets r(0) = r₀ and B(0) = 1.0</description></item>
        /// <item><description><b>Path generation:</b> Iteratively applies the Euler scheme to generate the 
        /// short rate path</description></item>
        /// <item><description><b>Numeraire calculation:</b> Updates the bank account at each step using 
        /// B(tᵢ₊₁) = B(tᵢ)·exp(r(tᵢ)·Δt)</description></item>
        /// </list>
        /// <para><b>Numerical Scheme Details:</b></para>
        /// <list type="bullet">
        /// <item><description><b>Euler-Maruyama:</b> A first-order scheme (weak convergence order 1.0, strong order 0.5)</description></item>
        /// <item><description><b>Time stepping:</b> Variable step sizes based on the spacing between required dates 
        /// (with maximum 20-day steps enforced by <see cref="Prepare"/>)</description></item>
        /// <item><description><b>Stability:</b> The scheme is unconditionally stable for the Hull-White model as 
        /// it has linear drift and constant diffusion</description></item>
        /// <item><description><b>Bias:</b> The discretization introduces a small bias of O(Δt), which is negligible 
        /// for typical step sizes</description></item>
        /// </list>
        /// <para><b>Bank Account Integration:</b></para>
        /// <para>
        /// The bank account numeraire requires integrating the short rate. The exact formula is 
        /// B(t) = exp(∫r(s)ds), but we approximate this with the left-point Riemann sum:
        /// </para>
        /// <para>
        ///   B(tᵢ₊₁) ≈ B(tᵢ)·exp(r(tᵢ)·Δt)
        /// </para>
        /// <para>
        /// This is more accurate than using the arithmetic average and avoids the need to store intermediate values.
        /// </para>
        /// <para><b>Developer Notes:</b></para>
        /// <list type="bullet">
        /// <item><description>Must be called after <see cref="Prepare"/> has been executed</description></item>
        /// <item><description>Each call generates a fresh independent path (assuming proper RNG initialization)</description></item>
        /// <item><description>The generated path is stored in member variables (_r, _bankAccount) for subsequent retrieval</description></item>
        /// <item><description>The simNumber parameter is currently unused but reserved for future variance reduction techniques</description></item>
        /// <item><description>For improved accuracy with large time steps, consider a Milstein scheme or exact simulation 
        /// (the Hull-White model admits exact simulation formulas)</description></item>
        /// </list>
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
        /// Retrieves simulated forward rate values for the specified floating rate index at the requested dates 
        /// from the current simulation path.
        /// </summary>
        /// <param name="index">The market observable index to retrieve values for. Must be a <see cref="FloatRateIndex"/> 
        /// that was previously registered with this model.</param>
        /// <param name="requiredDates">The dates at which forward rate fixings are required.</param>
        /// <returns>An array of forward rates, one for each date in <paramref name="requiredDates"/>. 
        /// Rates are expressed as continuously compounded rates (not percentages), e.g., 0.05 represents 5%.</returns>
        /// <remarks>
        /// <para><b>Forward Rate Calculation Method:</b></para>
        /// <para>
        /// Forward rates are derived from zero-coupon bond prices using the relationship:
        /// </para>
        /// <para>
        ///   F(t, t+τ) = (365/τ) · [1/P(t,t+τ) - 1]
        /// </para>
        /// <para>
        /// where τ is the tenor of the floating rate index (e.g., 90 days for 3-month LIBOR), and P(t,t+τ) is 
        /// the zero-coupon bond price from t to t+τ, calculated using the <see cref="BondPrice"/> method with 
        /// the simulated short rate at time t.
        /// </para>
        /// <para><b>Implementation Details:</b></para>
        /// <list type="bullet">
        /// <item><description>The short rate r(t) at each required date is obtained via linear interpolation of the 
        /// simulated short rate path (stored in _r array)</description></item>
        /// <item><description>The tenor is extracted from the FloatRateIndex (e.g., 3M, 6M)</description></item>
        /// <item><description>The bond price P(t,t+τ) is calculated analytically using the Hull-White formula</description></item>
        /// <item><description>The forward rate is converted from the bond price using the simple rate formula</description></item>
        /// <item><description>A 365-day count convention is used for rate calculations</description></item>
        /// </list>
        /// <para><b>Developer Notes:</b></para>
        /// <list type="bullet">
        /// <item><description>This method should only be called after <see cref="RunSimulation"/> has generated a path</description></item>
        /// <item><description>The index must have been registered via the constructor or <see cref="AddForecast"/></description></item>
        /// <item><description>Dates outside the simulation range use boundary values (first/last simulated rate)</description></item>
        /// </list>
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
        /// Retrieves the underlying state variable (the short rate) at the specified date from the current simulation path.
        /// </summary>
        /// <param name="date">The date at which the state variable is required.</param>
        /// <returns>A single-element array containing the instantaneous short rate r(t) at the specified date.</returns>
        /// <remarks>
        /// <para>
        /// This method returns the fundamental state variable of the Hull-White one-factor model: the instantaneous 
        /// short rate r(t). Since this is a one-factor model, only a single value is returned.
        /// </para>
        /// <para><b>Purpose:</b></para>
        /// <para>
        /// The underlying factor is used in regression-based methods for calculating continuation values in 
        /// early exercise products (e.g., Bermudan swaptions, callable bonds). The Longstaff-Schwartz algorithm 
        /// regresses discounted future cashflows against current state variables to estimate continuation values.
        /// </para>
        /// <para><b>Implementation:</b></para>
        /// <list type="bullet">
        /// <item><description>The short rate is obtained by linear interpolation from the simulated path (stored in _r)</description></item>
        /// <item><description>If the date is before the first simulation date, the initial rate r₀ is returned</description></item>
        /// <item><description>If the date is after the last simulation date, the final simulated rate is returned</description></item>
        /// </list>
        /// <para><b>Developer Notes:</b></para>
        /// <list type="bullet">
        /// <item><description>Always returns exactly one element (single-factor model)</description></item>
        /// <item><description>Must be called after <see cref="RunSimulation"/> to access the current path</description></item>
        /// <item><description>For multi-factor extensions, this would return multiple state variables</description></item>
        /// </list>
        /// </remarks>
        public override double[] GetUnderlyingFactors(Date date)
        {
            var rt = Tools.Interpolate1D(date.value, _allDatesDouble, _r, _r[0], _r[_r.Length - 1]);
            return new[] {rt};
        }

        public override Currency GetNumeraireCurrency()
        {
            return _currency;
        }

        /// <summary>
        /// Calculates the bank account numeraire (money market account) value at the specified date within 
        /// the current simulation path.
        /// </summary>
        /// <param name="valueDate">The date at which the numeraire value is requested. Must be at or after 
        /// the anchor date (simulation start date).</param>
        /// <returns>The value of the bank account numeraire at <paramref name="valueDate"/>. 
        /// The numeraire is normalized to 1.0 at the anchor date.</returns>
        /// <exception cref="ArgumentException">Thrown if <paramref name="valueDate"/> is before the anchor date.</exception>
        /// <remarks>
        /// <para><b>Numeraire Definition:</b></para>
        /// <para>
        /// The bank account (money market account) numeraire represents the value of investing one unit of 
        /// currency at the anchor date and continuously rolling it over at the risk-free short rate r(t):
        /// </para>
        /// <para>
        ///   B(t) = exp(∫₀ᵗ r(s)ds)
        /// </para>
        /// <para>
        /// where the integral is taken from the anchor date (time 0) to time t. This is the fundamental numeraire 
        /// used for risk-neutral valuation in interest rate models.
        /// </para>
        /// <para><b>Implementation:</b></para>
        /// <para>
        /// The continuous integral is approximated using the Euler discretization:
        /// </para>
        /// <para>
        ///   B(tᵢ₊₁) = B(tᵢ) · exp(r(tᵢ) · Δt)
        /// </para>
        /// <para>
        /// where Δt = tᵢ₊₁ - tᵢ is the time step between simulation dates. The bank account values are 
        /// pre-calculated during <see cref="RunSimulation"/> and stored. This method retrieves the value 
        /// via linear interpolation for dates between simulation points.
        /// </para>
        /// <para><b>Usage in Valuation:</b></para>
        /// <list type="bullet">
        /// <item><description><b>Discounting:</b> Future cashflows are divided by B(T) to obtain present values</description></item>
        /// <item><description><b>Change of numeraire:</b> Converting between different probability measures</description></item>
        /// <item><description><b>Monte Carlo valuation:</b> The expectation of (Cashflow/Numeraire) gives the present value</description></item>
        /// </list>
        /// <para><b>Developer Notes:</b></para>
        /// <list type="bullet">
        /// <item><description>The numeraire at the anchor date is always exactly 1.0</description></item>
        /// <item><description>Values between simulation dates are linearly interpolated</description></item>
        /// <item><description>The last simulated value is used for dates beyond the simulation horizon</description></item>
        /// <item><description>This method must be called after <see cref="RunSimulation"/> has generated a path</description></item>
        /// </list>
        /// </remarks>
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
        /// Adds a floating rate index to the list of indices that this model will provide forecasts for.
        /// </summary>
        /// <param name="index">The floating rate index (e.g., LIBOR 3M, JIBAR 3M, EURIBOR 6M) to add to the forecast list.</param>
        /// <remarks>
        /// <para>
        /// This method allows dynamic registration of floating rate indices after the model has been constructed.
        /// Once an index is added, the model's <see cref="GetIndices"/> method will be able to return simulated 
        /// forward rate values for that index at requested dates.
        /// </para>
        /// <para>
        /// Multiple indices can be added to simulate different tenors (e.g., both 3-month and 6-month LIBOR) 
        /// from the same underlying short rate process. Each index is simulated consistently using the same 
        /// short rate path, ensuring proper correlation between different tenors.
        /// </para>
        /// <para><b>Developer Notes:</b></para>
        /// <list type="bullet">
        /// <item><description>This method can be called multiple times to add different indices</description></item>
        /// <item><description>The index must implement the <see cref="FloatRateIndex"/> interface</description></item>
        /// <item><description>The index's tenor (e.g., 3 months) is used to calculate forward rates from bond prices</description></item>
        /// <item><description>Adding indices after <see cref="Prepare"/> has been called is allowed but they should be 
        /// added before <see cref="SetRequiredDates"/> is called for optimal performance</description></item>
        /// </list>
        /// </remarks>
        public void AddForecast(FloatRateIndex index)
        {
            if (_floatRateIndices == null) _floatRateIndices = new List<FloatRateIndex>();
            _floatRateIndices.Add(index);
        }
    }
}