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
    /// Delegate for retrieving market zero-coupon bond prices P(0,T) from the anchor date to a future date.
    /// </summary>
    /// <param name="date">The maturity date T for which the bond price is required.</param>
    /// <returns>The zero-coupon bond price P(0,T) representing the present value of 1 unit of currency at the future date.</returns>
    public delegate double MarketBonds(Date date);

    /// <summary>
    /// Delegate for retrieving market instantaneous forward rates f(0,T) from the anchor date to a future date.
    /// </summary>
    /// <param name="date">The date T at which the instantaneous forward rate is required.</param>
    /// <returns>The instantaneous forward rate f(0,T) in continuously compounded terms.</returns>
    public delegate double MarketForwards(Date date);

    /// <summary>
    /// Single-factor Hull-White interest rate model simulator for Monte Carlo valuation of interest rate derivatives.
    /// This implementation can simulate both a numeraire (bank account) and any number of forward rates from the same curve.
    /// </summary>
    /// <remarks>
    /// <para><b>MATHEMATICAL DESCRIPTION</b></para>
    /// <para>
    /// The Hull-White model (also called extended Vasicek model) describes the evolution of the instantaneous short rate r(t)
    /// under the risk-neutral measure Q through the stochastic differential equation:
    /// </para>
    /// <para>
    ///     dr(t) = [theta(t) - a*r(t)]dt + vol*dW(t)
    /// </para>
    /// <para>where:</para>
    /// <list type="bullet">
    /// <item><description>r(t) = instantaneous short rate at time t</description></item>
    /// <item><description>a = mean reversion speed parameter (constant, a >= 0)</description></item>
    /// <item><description>vol = instantaneous short rate volatility (constant, vol >= 0)</description></item>
    /// <item><description>theta(t) = time-dependent drift function calibrated to match market discount curve</description></item>
    /// <item><description>W(t) = standard Brownian motion under risk-neutral measure Q</description></item>
    /// </list>
    /// 
    /// <para><b>MEAN REVERSION</b></para>
    /// <para>
    /// The parameter 'a' controls mean reversion. When a > 0, the short rate is pulled back toward a long-term level
    /// at speed 'a'. Higher values of 'a' cause faster mean reversion, making the short rate less persistent and
    /// reducing the impact of volatility over longer time horizons. When a = 0, the model reduces to the Ho-Lee model
    /// with no mean reversion.
    /// </para>
    /// 
    /// <para><b>CALIBRATION AND THETA FUNCTION</b></para>
    /// <para>
    /// The time-dependent function theta(t) is chosen to exactly fit the initial term structure of interest rates.
    /// This is achieved by setting:
    /// </para>
    /// <para>
    ///     theta(t) = a*f(0,t) + (vol^2)/(2*a) * (1 - exp(-2*a*t))
    /// </para>
    /// <para>
    /// where f(0,t) is the instantaneous forward rate at time t as seen from the anchor date.
    /// This calibration ensures that the model reproduces market discount bond prices at initialization.
    /// In this implementation, f(0,t) is taken as a constant flat rate for simplicity.
    /// </para>
    /// 
    /// <para><b>BOND PRICING FORMULA</b></para>
    /// <para>
    /// Under the Hull-White model, the price at time t of a zero-coupon bond maturing at T, given r(t), is:
    /// </para>
    /// <para>
    ///     P(t,T|r(t)) = A(t,T) * exp(-B(t,T)*r(t))
    /// </para>
    /// <para>where:</para>
    /// <para>
    ///     B(t,T) = (1/a) * (1 - exp(-a*(T-t)))
    /// </para>
    /// <para>
    ///     A(t,T) = [P(0,T)/P(0,t)] * exp(B(t,T)*f(0,t) - (vol^2)/(4*a) * (1 - exp(-2*a*t)) * B(t,T)^2)
    /// </para>
    /// <para>
    /// This analytical formula allows efficient computation of bond prices and forward rates during simulation
    /// without requiring nested Monte Carlo.
    /// </para>
    /// 
    /// <para><b>SIMULATION MECHANICS</b></para>
    /// <para>
    /// The short rate r(t) is simulated using an Euler discretization scheme on a time grid. At each time step dt:
    /// </para>
    /// <para>
    ///     r(t+dt) = r(t) + [theta(t+dt) - a*r(t)]*dt + vol*sqrt(dt)*Z
    /// </para>
    /// <para>
    /// where Z ~ N(0,1) is a standard normal random variable. The bank account (numeraire) is accumulated as:
    /// </para>
    /// <para>
    ///     B(t+dt) = B(t) * exp(r(t)*dt)
    /// </para>
    /// 
    /// <para><b>FORWARD RATE EXTRACTION</b></para>
    /// <para>
    /// Forward rates (e.g., 3M LIBOR) are extracted from the simulated short rate using the bond pricing formula.
    /// For a forward rate with tenor T-t observed at time t, the simply-compounded forward rate is computed as:
    /// </para>
    /// <para>
    ///     L(t,T) = (1/P(t,T|r(t)) - 1) * (365/(T-t))
    /// </para>
    /// <para>
    /// where P(t,T|r(t)) is the model-implied bond price from the analytical formula.
    /// </para>
    /// 
    /// <para><b>IMPLEMENTATION NOTES</b></para>
    /// <para>
    /// - This implementation uses a flat continuously compounded rate curve for both discount and forward curves
    /// - The simulator automatically adds intermediate dates to ensure reasonable step sizes (minimum ~20 days)
    /// - The short rate r(t) is the single underlying factor used for regression-based early exercise decisions
    /// - The model can provide multiple FloatRateIndex values (e.g., different tenors) from the same simulation
    /// - All cashflows are discounted using the simulated bank account (money market account numeraire)
    /// </para>
    /// 
    /// <para><b>USE CASES</b></para>
    /// <para>
    /// This model is suitable for pricing and risk-managing:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Interest rate swaps and swaptions</description></item>
    /// <item><description>Caps and floors</description></item>
    /// <item><description>Callable bonds and structured notes</description></item>
    /// <item><description>Bermudan swaptions (using regression for early exercise)</description></item>
    /// <item><description>Any single-currency interest rate derivatives where mean reversion is important</description></item>
    /// </list>
    /// 
    /// <para><b>LIMITATIONS</b></para>
    /// <para>
    /// - Single factor: cannot capture term structure of volatility (all rates perfectly correlated)
    /// - Allows negative interest rates (can be an advantage or disadvantage depending on market conditions)
    /// - Flat curve calibration: does not fit arbitrary term structures (extension would require interpolation)
    /// - Euler discretization introduces time-stepping error (can be reduced by finer time steps)
    /// </para>
    /// 
    /// <para><b>REFERENCES</b></para>
    /// <para>
    /// The implementation follows standard formulations found in Brigo &amp; Mercurio, "Interest Rate Models - Theory and Practice",
    /// particularly equations 3.33 (SDE), 3.37 (theta), and 3.39 (bond pricing formula).
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
        /// Initializes a new instance of the Hull-White 1-Factor interest rate model with specified model parameters and market calibration data.
        /// </summary>
        /// <param name="currency">
        /// The currency for which this interest rate model applies. This determines the currency of the numeraire (bank account)
        /// and restricts which floating rate indices can be simulated. All cashflows will be converted to this currency during valuation.
        /// </param>
        /// <param name="a">
        /// Mean reversion speed parameter (a >= 0, typically 0.01 to 0.5). Controls how quickly the short rate reverts to its long-term level.
        /// Higher values (e.g., 0.3-0.5) imply faster mean reversion, making short rate movements less persistent.
        /// Lower values (e.g., 0.01-0.1) imply slower mean reversion, allowing for more persistent rate movements.
        /// When a = 0, the model becomes the Ho-Lee model with no mean reversion (not typically recommended).
        /// QUANT NOTE: The mean reversion affects bond option pricing - higher 'a' reduces volatility of long-dated rates relative to short-dated rates.
        /// </param>
        /// <param name="vol">
        /// Instantaneous short rate volatility (vol >= 0, typically 0.005 to 0.02 for annual volatility of 0.5% to 2%).
        /// This parameter controls the magnitude of random fluctuations in the short rate.
        /// Higher volatility increases the value of optionality in derivative products (e.g., swaptions, caps/floors).
        /// QUANT NOTE: This is the constant volatility coefficient in the diffusion term. In practice, vol is often calibrated
        /// to match market prices of liquid instruments like caps, floors, or swaptions.
        /// </param>
        /// <param name="r0">
        /// Initial short rate at the anchor date (typically the valuation date), expressed as a continuously compounded rate.
        /// This should generally match the overnight rate or the very short end of the market discount curve.
        /// For example, if the overnight rate is 7% annual, r0 = 0.07.
        /// QUANT NOTE: In this implementation, r0 is typically set equal to inputRate for consistency with the flat curve assumption.
        /// </param>
        /// <param name="inputRate">
        /// The flat continuously compounded rate used for calibration of the model's initial term structure.
        /// This rate defines both the market forward rate function f(0,t) and the discount curve P(0,t).
        /// QUANT NOTE: In this simplified implementation, f(0,t) = inputRate (constant) and P(0,t) = exp(-inputRate*t).
        /// For production use with real market curves, this would be replaced by interpolated curves from market data.
        /// The theta(t) function is calibrated to this curve to ensure model consistency with market discount factors.
        /// </param>
        /// <param name="floatRateIndices">
        /// Optional collection of <see cref="FloatRateIndex"/> objects (e.g., 3M LIBOR, 6M EURIBOR) that this simulator will provide.
        /// These indices define the floating rate fixings that will be simulated. If null or empty, no floating rate indices are initially provided,
        /// but they can be added later using <see cref="AddForecast"/>.
        /// DEV NOTE: The simulator can provide multiple indices with different tenors from the same underlying short rate simulation,
        /// making it efficient for products with multiple floating rate dependencies.
        /// </param>
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
        /// Computes the time-dependent drift function theta(t) for the Hull-White short rate process at a given date.
        /// This function is calibrated to ensure the model matches the initial market discount curve.
        /// </summary>
        /// <param name="date">The date at which to evaluate theta(t).</param>
        /// <returns>The value of theta(t), representing the deterministic drift component of the short rate at time t.</returns>
        /// <remarks>
        /// <para><b>MATHEMATICAL FORMULA</b></para>
        /// <para>
        /// The drift function is computed as:
        /// </para>
        /// <para>
        ///     theta(t) = a * f(0,t) + (vol^2)/(2*a) * (1 - exp(-2*a*t))
        /// </para>
        /// <para>where:</para>
        /// <list type="bullet">
        /// <item><description>a = mean reversion speed</description></item>
        /// <item><description>f(0,t) = instantaneous forward rate at time t from the initial curve</description></item>
        /// <item><description>vol = short rate volatility</description></item>
        /// <item><description>t = time in years from anchor date to the specified date</description></item>
        /// </list>
        /// 
        /// <para><b>CALIBRATION PURPOSE</b></para>
        /// <para>
        /// The theta function is the mechanism by which the Hull-White model is calibrated to match market prices.
        /// Without theta, the model would be the Vasicek model, which cannot fit arbitrary initial term structures.
        /// By choosing theta as shown above, we ensure that E[r(t)] follows the market forward rate curve,
        /// and the model reproduces market discount bond prices P(0,T) at time 0.
        /// </para>
        /// 
        /// <para><b>IMPLEMENTATION NOTE</b></para>
        /// <para>
        /// In this implementation, f(0,t) is taken as a constant (inputRate) for simplicity, representing a flat forward curve.
        /// For more realistic applications, f(0,t) would be derived from market instruments such as swap rates, FRA rates, or bond yields,
        /// typically through interpolation on a curve built from market quotes.
        /// </para>
        /// 
        /// <para><b>SPECIAL CASE</b></para>
        /// <para>
        /// When a approaches 0 (no mean reversion), the formula would require special handling. However, for practical purposes,
        /// a should always be positive. If a is very small, theta(t) approaches: f(0,t) * a + vol^2 * t.
        /// </para>
        /// </remarks>
        private double Theta(Date date)
        {
            var t = (date - _anchorDate) / 365.0;
            return _a * _fM(date) + _vol * _vol / (2 * _a) * (1 - Math.Exp(-2 * _a * t));
        }


        /// <summary>
        /// Computes the forward zero-coupon bond price P(t,T|r(t)) under the Hull-White model, conditional on the short rate r(t) at time t.
        /// This is an analytical formula that allows efficient pricing of bonds and extraction of forward rates during Monte Carlo simulation.
        /// </summary>
        /// <param name="r">
        /// The short rate r(t) observed or simulated at date1. This is the instantaneous interest rate from the Hull-White process.
        /// </param>
        /// <param name="date1">
        /// The observation date 't' at which the short rate r is known. This is the start date of the forward bond price.
        /// </param>
        /// <param name="date2">
        /// The maturity date 'T' of the zero-coupon bond. Must be greater than or equal to date1.
        /// </param>
        /// <returns>
        /// The price P(t,T|r(t)) of a zero-coupon bond paying 1 unit of currency at date2, as seen from date1 given the short rate r.
        /// This represents the present value at time t of receiving 1 at time T.
        /// </returns>
        /// <remarks>
        /// <para><b>ANALYTICAL FORMULA</b></para>
        /// <para>
        /// Under the Hull-White model, the conditional bond price has the affine form:
        /// </para>
        /// <para>
        ///     P(t,T|r(t)) = A(t,T) * exp(-B(t,T) * r(t))
        /// </para>
        /// <para>where the functions A(t,T) and B(t,T) are given by:</para>
        /// <para>
        ///     B(t,T) = (1/a) * (1 - exp(-a*(T-t)))
        /// </para>
        /// <para>
        ///     A(t,T) = [P(0,T)/P(0,t)] * exp(B(t,T)*f(0,t) - (vol^2)/(4*a) * (1 - exp(-2*a*t)) * B(t,T)^2)
        /// </para>
        /// <para>
        /// where P(0,T) is the market discount factor from anchor date to T, and f(0,t) is the market forward rate.
        /// </para>
        /// 
        /// <para><b>INTERPRETATION OF B(t,T)</b></para>
        /// <para>
        /// B(t,T) represents the sensitivity of the bond price to the short rate. It is always positive for T > t.
        /// - When a is large (strong mean reversion), B(t,T) approaches (T-t), becoming linear in maturity
        /// - When a is small (weak mean reversion), B(t,T) approaches (1-exp(-a*(T-t)))/a, which grows slowly with maturity
        /// - This explains why mean reversion flattens the term structure of rate volatilities
        /// </para>
        /// 
        /// <para><b>INTERPRETATION OF A(t,T)</b></para>
        /// <para>
        /// A(t,T) is a deterministic function that ensures the model matches the initial discount curve.
        /// The first term [P(0,T)/P(0,t)] brings in the market discount factors.
        /// The exponential adjustment term accounts for the convexity effects due to volatility.
        /// </para>
        /// 
        /// <para><b>QUANT APPLICATION</b></para>
        /// <para>
        /// This analytical formula is crucial for efficiency in Monte Carlo simulation because:
        /// 1. It avoids nested Monte Carlo for pricing bonds within each simulation path
        /// 2. It allows extraction of forward rates (e.g., LIBOR) from the simulated short rate
        /// 3. It provides the building blocks for pricing more complex interest rate derivatives
        /// </para>
        /// 
        /// <para><b>IMPLEMENTATION DETAILS</b></para>
        /// <para>
        /// - Times t and T are measured in years (day count / 365.0) from the anchor date
        /// - The formula is based on Brigo &amp; Mercurio equation 3.39 (2nd edition)
        /// - For numerical stability, ensure that 'a' is not too close to zero (typically a >= 0.001)
        /// - The function assumes date2 >= date1; no validation is performed for efficiency
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
        /// Resets the simulator state to prepare for a new valuation run. Clears all previously collected required dates.
        /// This method is part of the simulator lifecycle and must be called before setting up a new set of dates for simulation.
        /// </summary>
        /// <remarks>
        /// <para><b>SIMULATOR LIFECYCLE</b></para>
        /// <para>
        /// The typical workflow for using a simulator is:
        /// 1. Reset() - Clear previous state
        /// 2. SetRequiredDates() / SetNumeraireDates() - Collect all dates needed by products (called multiple times as needed)
        /// 3. Prepare() - Finalize the date grid and initialize simulation structures
        /// 4. RunSimulation() - Execute individual Monte Carlo paths (called N times for N simulations)
        /// 5. GetIndices() / Numeraire() - Retrieve simulated values within each path
        /// </para>
        /// <para><b>DEV NOTE</b></para>
        /// <para>
        /// This method only clears the date list. Other state variables like _r, _bankAccount are cleared during Prepare() and RunSimulation().
        /// The model parameters (a, vol, r0, inputRate) are preserved across resets.
        /// </para>
        /// </remarks>
        public override void Reset()
        {
            _allDates = new List<Date>();
        }

        /// <summary>
        /// Registers dates at which a specific market observable (floating rate index) will be required during valuation.
        /// This method can be called multiple times for different indices or to add additional dates for the same index.
        /// All dates are accumulated internally and will be finalized during <see cref="Prepare"/>.
        /// </summary>
        /// <param name="index">
        /// The market observable (typically a <see cref="FloatRateIndex"/>) for which dates are being registered.
        /// The simulator will provide simulated values for this index at the specified dates.
        /// </param>
        /// <param name="requiredDates">
        /// List of dates at which the index will be observed. These dates are added to the internal simulation grid.
        /// </param>
        /// <remarks>
        /// <para><b>PURPOSE</b></para>
        /// <para>
        /// This method allows the simulator to know in advance all dates where market observables will be needed.
        /// By collecting dates upfront from all products being valued, the simulator can:
        /// - Create an efficient combined simulation grid
        /// - Add intermediate dates if gaps are too large
        /// - Optimize memory allocation and interpolation
        /// </para>
        /// <para><b>DEV NOTE</b></para>
        /// <para>
        /// Dates from multiple calls are accumulated. Duplicates will be removed during Prepare().
        /// The order of dates does not matter; they will be sorted in Prepare().
        /// If _allDates is null (shouldn't happen after Reset), it is initialized with the provided dates.
        /// </para>
        /// </remarks>
        public override void SetRequiredDates(MarketObservable index, List<Date> requiredDates)
        {
            if (_allDates == null) _allDates = requiredDates;
            else
                _allDates.AddRange(requiredDates);
        }

        /// <summary>
        /// Registers dates at which the numeraire (bank account) will be required for discounting during valuation.
        /// This method can be called multiple times to accumulate all necessary numeraire dates.
        /// All dates are combined internally and will be finalized during <see cref="Prepare"/>.
        /// </summary>
        /// <param name="requiredDates">
        /// List of dates at which the numeraire value will be needed for discounting cashflows.
        /// Typically these are cashflow payment dates or intermediate valuation dates.
        /// </param>
        /// <remarks>
        /// <para><b>PURPOSE</b></para>
        /// <para>
        /// The numeraire B(t) = exp(integral from 0 to t of r(s)ds) is used to discount cashflows in the risk-neutral measure.
        /// By knowing where the numeraire will be required, the simulator can ensure these dates are included in the simulation grid.
        /// This is particularly important for accurate pricing, as the numeraire may be needed at dates different from
        /// where market observables are required.
        /// </para>
        /// <para><b>DEV NOTE</b></para>
        /// <para>
        /// In this implementation, numeraire dates and index dates are stored in the same _allDates collection.
        /// This is efficient because the short rate r(t) needs to be simulated at a combined set of all required dates.
        /// The bank account B(t) can then be interpolated from this grid when needed.
        /// </para>
        /// </remarks>
        public override void SetNumeraireDates(List<Date> requiredDates)
        {
            if (_allDates == null) _allDates = requiredDates;
            else
                _allDates.AddRange(requiredDates);
        }

        /// <summary>
        /// Finalizes the simulation setup by preparing the date grid, initializing market curve functions, and setting up random number generation.
        /// This is the last step before running actual simulations and must be called after all required dates have been registered.
        /// </summary>
        /// <param name="anchorDate">
        /// The anchor date (typically the valuation date) from which all simulations will start. This is time t=0 for the model.
        /// All forward rates and discount factors are measured relative to this date.
        /// </param>
        /// <remarks>
        /// <para><b>DATE GRID CONSTRUCTION</b></para>
        /// <para>
        /// This method constructs the final simulation date grid by:
        /// 1. Adding the anchor date as the first date (t=0)
        /// 2. Removing duplicate dates from all collected required dates
        /// 3. Sorting dates in chronological order
        /// 4. Adding intermediate dates where gaps exceed a minimum step size (~20 days)
        /// </para>
        /// <para>
        /// The automatic insertion of intermediate dates is crucial for numerical accuracy. The Euler discretization scheme
        /// used to simulate the short rate process has discretization error proportional to sqrt(dt). Large time steps can cause:
        /// - Accumulation of discretization bias
        /// - Poor approximation of the continuous-time process
        /// - Potential numerical instability
        /// By ensuring maximum step size of ~20 days, these errors are kept manageable for typical interest rate applications.
        /// </para>
        /// 
        /// <para><b>MARKET CURVE INITIALIZATION</b></para>
        /// <para>
        /// The method initializes the market forward rate function _fM and bond price function _pm based on the flat inputRate:
        /// - _fM(t) = inputRate (constant forward rate)
        /// - _pm(t) = exp(-inputRate * t) (discount factor for maturity t)
        /// These functions are used in the theta(t) calibration and the analytical bond pricing formula.
        /// </para>
        /// 
        /// <para><b>RANDOM NUMBER GENERATION</b></para>
        /// <para>
        /// A standard normal distribution generator is initialized for producing the random shocks in the simulation.
        /// The seed is set to a fixed value (hash code of "HW1FSimulator") to ensure reproducibility of results across runs.
        /// DEV NOTE: For production use with multiple valuations, you may want to make the seed configurable or use
        /// different seeds for different model instances to ensure independence.
        /// </para>
        /// 
        /// <para><b>QUANT CONSIDERATIONS</b></para>
        /// <para>
        /// The choice of minimum step size (20 days) is a trade-off between:
        /// - Accuracy: Smaller steps reduce discretization error but increase computation time
        /// - Performance: Larger steps are faster but less accurate
        /// For high-volatility scenarios or very long-dated derivatives, consider reducing minStepSize.
        /// For calibration or speed-critical applications, consider increasing it (with caution).
        /// </para>
        /// 
        /// <para><b>DEV NOTE - MEMORY ALLOCATION</b></para>
        /// <para>
        /// After this method:
        /// - _allDates contains the full sorted date grid (with intermediate dates)
        /// - _allDatesDouble is a double array version for efficient interpolation
        /// - The actual state arrays (_r, _bankAccount) are allocated per simulation in RunSimulation()
        /// This design separates grid construction (done once) from path simulation (done N times).
        /// </para>
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
        /// Executes a single Monte Carlo simulation path, generating the short rate process r(t) and bank account numeraire B(t)
        /// over the entire date grid. This method must be called after <see cref="Prepare"/> and stores the simulated path
        /// internally for retrieval via <see cref="GetIndices"/> and <see cref="Numeraire"/>.
        /// </summary>
        /// <param name="simNumber">
        /// The simulation path number (0-indexed). This can be used for variance reduction techniques or stratified sampling.
        /// In the current implementation, the random number generator seed is fixed, so this parameter doesn't affect the random draws,
        /// but it's available for future enhancements or debugging purposes.
        /// </param>
        /// <remarks>
        /// <para><b>SIMULATION ALGORITHM</b></para>
        /// <para>
        /// The method simulates the Hull-White short rate SDE using an Euler-Maruyama discretization scheme:
        /// </para>
        /// <para>
        ///     dr(t) = [theta(t) - a*r(t)]dt + vol*dW(t)
        /// </para>
        /// <para>
        /// Discretized as:
        /// </para>
        /// <para>
        ///     r(t+dt) = r(t) + [theta(t+dt) - a*r(t)]*dt + vol*sqrt(dt)*Z
        /// </para>
        /// <para>
        /// where Z ~ N(0,1) is a standard normal random variable drawn independently for each time step.
        /// </para>
        /// 
        /// <para><b>INITIALIZATION</b></para>
        /// <para>
        /// The simulation starts at the anchor date with:
        /// - r(0) = r0 (initial short rate, specified in constructor)
        /// - B(0) = 1 (bank account starts at 1 unit of currency)
        /// </para>
        /// 
        /// <para><b>SHORT RATE EVOLUTION</b></para>
        /// <para>
        /// At each time step i:
        /// 1. Calculate time increment dt = (date[i+1] - date[i]) / 365.0 in years
        /// 2. Evaluate drift: theta(t+dt) - a*r(t) where theta provides calibration to market curve
        /// 3. Generate random shock: vol*sqrt(dt)*Z where Z is drawn from standard normal
        /// 4. Update: r(t+dt) = r(t) + drift*dt + shock
        /// </para>
        /// 
        /// <para><b>BANK ACCOUNT ACCUMULATION</b></para>
        /// <para>
        /// The bank account (numeraire) B(t) is the value of a money market account earning the short rate:
        /// </para>
        /// <para>
        ///     B(t+dt) = B(t) * exp(r(t) * dt)
        /// </para>
        /// <para>
        /// This uses the short rate at the beginning of each interval (r(t), not r(t+dt)), which is consistent with
        /// the interpretation of r as the instantaneous rate. The bank account grows according to the realized path of rates.
        /// </para>
        /// 
        /// <para><b>QUANT NOTES</b></para>
        /// <para>
        /// - Euler scheme: The discretization has weak convergence order 1 and strong convergence order 0.5.
        ///   Discretization error is O(dt) for expectations and O(sqrt(dt)) for individual paths.
        /// - Theta evaluation: theta(t+dt) is evaluated at the end of the interval, which is appropriate for the drift term.
        /// - Mean reversion: The term -a*r(t) pulls the rate back toward the level implied by theta(t)/a.
        /// - Volatility scaling: The random term scales with sqrt(dt), which is correct for Brownian motion increments.
        /// - Negative rates: The model allows negative short rates, which can be realistic for some markets but
        ///   may be undesirable for others. Consider this when choosing model parameters.
        /// </para>
        /// 
        /// <para><b>RANDOM NUMBER GENERATION</b></para>
        /// <para>
        /// The method generates (_allDates.Count - 1) independent standard normal random variables, one for each time step.
        /// These are drawn from the NormalDistribution object initialized in Prepare(). The seed is fixed for reproducibility.
        /// DEV NOTE: For parallel execution across multiple simulations, ensure thread-safety of the random number generator.
        /// </para>
        /// 
        /// <para><b>MEMORY AND PERFORMANCE</b></para>
        /// <para>
        /// Arrays _r and _bankAccount are allocated fresh for each simulation path. This is necessary because each simulation
        /// needs to store its own path for subsequent queries via GetIndices() and Numeraire(). For very long date grids or
        /// large numbers of simulations, memory usage can become significant. Consider streaming approaches for production systems.
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
        /// Retrieves simulated values of a floating rate index (e.g., LIBOR, EURIBOR) at specified observation dates
        /// from the current simulation path. The forward rates are extracted analytically from the simulated short rate
        /// using the Hull-White bond pricing formula.
        /// </summary>
        /// <param name="index">
        /// The market observable for which values are requested. Must be a <see cref="FloatRateIndex"/> that has been
        /// registered with this simulator (either in constructor or via <see cref="AddForecast"/>).
        /// </param>
        /// <param name="requiredDates">
        /// List of dates at which the floating rate index should be observed. These are typically reset dates for
        /// floating rate coupons. The dates should have been registered previously via <see cref="SetRequiredDates"/>.
        /// </param>
        /// <returns>
        /// An array of floating rate values (e.g., LIBOR rates) corresponding to each date in requiredDates.
        /// Rates are returned as decimals (e.g., 0.05 for 5% annual rate) in simple compounding convention.
        /// </returns>
        /// <remarks>
        /// <para><b>FORWARD RATE EXTRACTION</b></para>
        /// <para>
        /// For each observation date t in requiredDates, the method computes the forward rate L(t,T) where T = t + tenor:
        /// </para>
        /// <para>
        /// 1. Interpolate the short rate r(t) from the simulated path at date t
        /// 2. Calculate the maturity date T = t + tenor (e.g., if tenor is 3M, T is 3 months after t)
        /// 3. Compute the zero-coupon bond price P(t,T|r(t)) using the analytical Hull-White formula
        /// 4. Extract the simply-compounded forward rate:
        /// </para>
        /// <para>
        ///     L(t,T) = [1/P(t,T) - 1] * (365/(T-t))
        /// </para>
        /// <para>
        /// This converts the bond price into an annualized rate with ACT/365 day count convention.
        /// </para>
        /// 
        /// <para><b>INTERPOLATION</b></para>
        /// <para>
        /// The short rate r(t) is simulated on a discrete grid (_allDates), so values at arbitrary requiredDates
        /// are obtained via linear interpolation. If a required date falls outside the simulation grid, the boundary
        /// values are used (r[0] for dates before the grid, r[end] for dates after).
        /// QUANT NOTE: Linear interpolation of the short rate is generally acceptable for small time steps.
        /// For applications requiring higher accuracy, consider interpolating in a different space (e.g., log-space)
        /// or using more sophisticated schemes.
        /// </para>
        /// 
        /// <para><b>TENOR HANDLING</b></para>
        /// <para>
        /// The tenor (e.g., 3 months for 3M LIBOR) is extracted from the FloatRateIndex. The maturity date T
        /// is calculated by adding this tenor to the observation date. The forward rate L(t,T) then represents
        /// the rate for borrowing from t to T, which is what floating rate indices like LIBOR represent.
        /// </para>
        /// 
        /// <para><b>DAY COUNT CONVENTION</b></para>
        /// <para>
        /// The implementation uses ACT/365 for converting bond prices to rates:
        /// </para>
        /// <para>
        ///     rate = 365.0 * (1/bondPrice - 1) / (T-t in days)
        /// </para>
        /// <para>
        /// This is appropriate for many markets (e.g., GBP, ZAR) but may need adjustment for others.
        /// USD LIBOR traditionally uses ACT/360, while some indices use 30/360 or other conventions.
        /// DEV NOTE: For production systems, day count conventions should be configurable per index.
        /// </para>
        /// 
        /// <para><b>ANALYTICAL VS SIMULATION</b></para>
        /// <para>
        /// A key advantage of the Hull-White model is that forward rates can be extracted analytically from the
        /// simulated short rate using the bond pricing formula. This avoids the need for nested Monte Carlo, where
        /// you would simulate forward from each observation date to estimate the expected rate. The analytical approach is:
        /// - Much faster (no nested simulation)
        /// - More accurate (no additional Monte Carlo error)
        /// - Theoretically exact (given the model assumptions)
        /// </para>
        /// 
        /// <para><b>CONSISTENCY WITH MARKET</b></para>
        /// <para>
        /// At time t=0 (anchor date), if you call this method with requiredDates = [anchorDate], the returned rate
        /// will match the market forward rate implied by the initial curve (inputRate in this implementation).
        /// As the simulation progresses to future dates, rates evolve stochastically according to the Hull-White dynamics.
        /// </para>
        /// 
        /// <para><b>DEV NOTES</b></para>
        /// <para>
        /// - The cast to FloatRateIndex should always succeed if the simulator is used correctly (ProvidesIndex should be checked first)
        /// - No validation is performed for efficiency; ensure requiredDates were registered in SetRequiredDates
        /// - The result array is allocated fresh for each call
        /// - Thread safety: This method reads the current simulation path (_r, _allDatesDouble), so it should not be called
        ///   concurrently with RunSimulation for the same instance
        /// </para>
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
        /// Retrieves the underlying state factors of the model at a specified date for use in regression-based valuation methods.
        /// For the Hull-White 1-Factor model, this returns a single value: the short rate r(t).
        /// </summary>
        /// <param name="date">
        /// The date at which the underlying factors are required. Typically used for regression at early exercise decision dates
        /// or for American/Bermudan option valuation.
        /// </param>
        /// <returns>
        /// An array containing a single element: the short rate r(t) at the specified date, obtained from the current simulation path.
        /// </returns>
        /// <remarks>
        /// <para><b>REGRESSION-BASED VALUATION</b></para>
        /// <para>
        /// Many derivative valuation problems require conditional expectations, particularly for products with early exercise features:
        /// - American and Bermudan options (e.g., Bermudan swaptions)
        /// - Callable bonds
        /// - Swing options
        /// </para>
        /// <para>
        /// The Longstaff-Schwartz regression approach estimates these conditional expectations by regressing future cashflows
        /// against underlying factors. The factors returned by this method serve as the explanatory variables in that regression.
        /// </para>
        /// 
        /// <para><b>WHY THE SHORT RATE?</b></para>
        /// <para>
        /// In the Hull-White 1-Factor model, the short rate r(t) is a Markov state variable, meaning:
        /// - All future distributions are determined by the current value of r(t) (Markov property)
        /// - r(t) is sufficient to compute all bond prices and forward rates analytically
        /// - Conditional expectations E[X|F_t] can be expressed as functions of r(t)
        /// </para>
        /// <para>
        /// Therefore, r(t) contains all information needed for regression-based continuation value estimation at time t.
        /// </para>
        /// 
        /// <para><b>INTERPOLATION</b></para>
        /// <para>
        /// The short rate is simulated on the discrete grid _allDates. For dates not exactly on the grid,
        /// linear interpolation is used. Boundary values (r[0] and r[end]) are used for dates outside the grid.
        /// This is consistent with the interpolation used in GetIndices().
        /// </para>
        /// 
        /// <para><b>MULTI-FACTOR MODELS</b></para>
        /// <para>
        /// In a multi-factor model (e.g., Hull-White 2-Factor, or multi-currency models), this method would return
        /// multiple factors (e.g., [r(t), r_f(t), X(t)] for domestic rate, foreign rate, and FX factor).
        /// The single-factor structure here is simple but limits the model's ability to capture:
        /// - Non-parallel term structure movements
        /// - Decorrelation between different rate tenors
        /// </para>
        /// 
        /// <para><b>REGRESSION IMPLEMENTATION NOTES</b></para>
        /// <para>
        /// When using these factors for regression:
        /// 1. Collect factors at exercise date from all simulation paths: [r_1(t), r_2(t), ..., r_N(t)]
        /// 2. Compute continuation values (discounted future cashflows) from each path
        /// 3. Regress continuation values against factors using basis functions (e.g., polynomials in r)
        /// 4. Use regression to estimate conditional expectation for exercise decision
        /// </para>
        /// <para>
        /// Common basis functions for Hull-White:
        /// - Polynomial: {1, r, r^2, r^3, ...}
        /// - Laguerre polynomials (theoretically optimal for affine models)
        /// - Exponential: {1, r, exp(r), exp(-r)}
        /// </para>
        /// 
        /// <para><b>QUANT CONSIDERATIONS</b></para>
        /// <para>
        /// The effectiveness of regression depends on:
        /// - Number of simulation paths: More paths improve regression accuracy
        /// - Choice of basis functions: Should span the space of continuation value functions
        /// - Degree of polynomial: Higher degree can overfit with insufficient paths
        /// - Range of r(t) values: Regression performs poorly if exercise date has low rate variance
        /// </para>
        /// <para>
        /// For the Hull-White model, 2nd or 3rd degree polynomials typically work well. Very high degrees
        /// (>5) can cause numerical instability and overfitting.
        /// </para>
        /// 
        /// <para><b>DEV NOTES</b></para>
        /// <para>
        /// - Returns a new array for each call (no caching)
        /// - Thread safety: Reads from _r, _allDatesDouble which are set per simulation path
        /// - For performance-critical applications with many regression dates, consider caching interpolated values
        /// </para>
        /// </remarks>
        public override double[] GetUnderlyingFactors(Date date)
        {
            var rt = Tools.Interpolate1D(date.value, _allDatesDouble, _r, _r[0], _r[_r.Length - 1]);
            return new[] {rt};
        }

        /// <summary>
        /// Returns the currency in which this simulator operates and in which the numeraire is denominated.
        /// All cashflows valued using this simulator will be converted to this currency for discounting.
        /// </summary>
        /// <returns>The numeraire currency (e.g., USD, EUR, ZAR) associated with this interest rate model.</returns>
        /// <remarks>
        /// <para><b>PURPOSE</b></para>
        /// <para>
        /// In multi-currency valuations, each interest rate model operates in a specific currency.
        /// The coordinator uses this method to determine which currency's numeraire to use for discounting
        /// cashflows and to identify when FX conversion is needed.
        /// </para>
        /// <para><b>SINGLE VS MULTI-CURRENCY</b></para>
        /// <para>
        /// For single-currency products, all cashflows are naturally in the numeraire currency.
        /// For multi-currency products (e.g., cross-currency swaps), the valuation framework will:
        /// 1. Identify which cashflows need FX conversion
        /// 2. Convert all cashflows to the numeraire currency
        /// 3. Discount using the numeraire from this simulator
        /// </para>
        /// </remarks>
        public override Currency GetNumeraireCurrency()
        {
            return _currency;
        }

        /// <summary>
        /// Retrieves the value of the bank account numeraire B(t) at a specified date within the current simulation path.
        /// The numeraire represents the accumulated value of investing 1 unit of currency at the short rate from the anchor date to the specified date.
        /// </summary>
        /// <param name="valueDate">
        /// The date at which the numeraire value is required. Must be greater than or equal to the anchor date.
        /// </param>
        /// <returns>
        /// The numeraire value B(t), which is the growth factor from the anchor date to valueDate under the simulated short rate path.
        /// At the anchor date, returns 1.0 by definition.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown if valueDate is before the anchor date, as the model cannot provide values before its start date.
        /// </exception>
        /// <remarks>
        /// <para><b>BANK ACCOUNT NUMERAIRE</b></para>
        /// <para>
        /// The numeraire B(t) is the fundamental discounting factor in risk-neutral valuation. It represents
        /// a money market account that continuously rolls over at the short rate r(s):
        /// </para>
        /// <para>
        ///     B(t) = exp(integral from 0 to t of r(s) ds)
        /// </para>
        /// <para>
        /// In the discretized simulation, this is computed iteratively as:
        /// </para>
        /// <para>
        ///     B(t+dt) = B(t) * exp(r(t) * dt)
        /// </para>
        /// <para>
        /// where r(t) is the short rate at time t and dt is the time step.
        /// </para>
        /// 
        /// <para><b>DISCOUNTING AND PRICING</b></para>
        /// <para>
        /// In Monte Carlo valuation, the price of a derivative is computed as:
        /// </para>
        /// <para>
        ///     V(0) = E[Sum of C(t_i) / B(t_i)]
        /// </para>
        /// <para>
        /// where C(t_i) are the cashflows at times t_i, and the expectation is over all simulation paths.
        /// Each path provides a different realization of r(t) and hence B(t). Dividing cashflows by B(t)
        /// brings them back to present value in that particular path.
        /// </para>
        /// 
        /// <para><b>RISK-NEUTRAL MEASURE</b></para>
        /// <para>
        /// Under the risk-neutral (bank account) measure, B(t) is the numeraire asset. Any traded asset's
        /// price divided by B(t) is a martingale. This is the theoretical foundation for discounting with
        /// the bank account in Monte Carlo simulation.
        /// </para>
        /// 
        /// <para><b>INTERPOLATION</b></para>
        /// <para>
        /// The bank account is computed at discrete dates in _allDates during RunSimulation().
        /// For dates not exactly on the grid, linear interpolation is used:
        /// - Interpolate between adjacent simulated values
        /// - For dates before the first grid point (shouldn't happen): return 1.0
        /// - For dates after the last grid point: return the last simulated value
        /// </para>
        /// <para>
        /// Linear interpolation of B(t) is equivalent to piecewise constant r(t) between grid points,
        /// which is consistent with the way B(t) is accumulated during simulation.
        /// </para>
        /// 
        /// <para><b>SPECIAL CASE: ANCHOR DATE</b></para>
        /// <para>
        /// At the anchor date (t=0), the numeraire is exactly 1.0 by definition. This is checked explicitly
        /// for efficiency and numerical precision.
        /// </para>
        /// 
        /// <para><b>GROWTH VS DISCOUNTING</b></para>
        /// <para>
        /// - B(t) > 1 represents growth: 1 unit invested at t=0 grows to B(t) units at time t
        /// - 1/B(t) is the discount factor: 1 unit at time t is worth 1/B(t) units at t=0
        /// - For typical positive rates, B(t) grows exponentially with time
        /// - For negative rates (possible in Hull-White), B(t) can decrease, meaning 1/B(t) > 1 (unusual but theoretically valid)
        /// </para>
        /// 
        /// <para><b>COMPARISON WITH MARKET DISCOUNT FACTORS</b></para>
        /// <para>
        /// At t=0, the expected value E[1/B(T)] equals the market discount factor P(0,T), due to the model calibration
        /// via theta(t). However, in any individual simulation path, B(T) will differ from 1/P(0,T) due to the stochastic
        /// evolution of rates. This path-dependence is what allows the model to capture interest rate risk.
        /// </para>
        /// 
        /// <para><b>DEV NOTES</b></para>
        /// <para>
        /// - The method validates that valueDate >= anchorDate, throwing an exception otherwise
        /// - No validation that valueDate is within the simulation range; relies on interpolation with boundary values
        /// - Thread safety: Reads _bankAccount which is set per simulation path in RunSimulation()
        /// - Performance: Direct interpolation with no caching; for repeated queries at the same date, consider caching
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
        /// Checks whether this simulator can provide simulated values for a specified market observable (typically a floating rate index).
        /// </summary>
        /// <param name="index">
        /// The market observable being queried, typically a <see cref="FloatRateIndex"/> such as 3M LIBOR or 6M EURIBOR.
        /// </param>
        /// <returns>
        /// <c>true</c> if the simulator has been configured to provide this index (via constructor or <see cref="AddForecast"/>);
        /// <c>false</c> otherwise.
        /// </returns>
        /// <remarks>
        /// <para><b>PURPOSE</b></para>
        /// <para>
        /// The valuation framework uses this method to route market observable requests to the appropriate simulator.
        /// In a multi-model setup (e.g., multiple currencies, equity models, credit models), each simulator advertises
        /// which market observables it can provide. The coordinator then ensures each product's requirements are met
        /// by the available simulators.
        /// </para>
        /// 
        /// <para><b>FLOATING RATE INDICES</b></para>
        /// <para>
        /// For the Hull-White model, the typical market observables are floating rate indices with different tenors:
        /// - Short tenors: 1M, 3M LIBOR/EURIBOR for floating legs of swaps
        /// - Medium tenors: 6M, 12M rates for bonds or structured products
        /// - Any tenor can be supported as long as it's registered
        /// </para>
        /// <para>
        /// Importantly, multiple tenors can be provided from the same underlying short rate simulation,
        /// making the model efficient for products with multiple floating rate dependencies.
        /// </para>
        /// 
        /// <para><b>CURRENCY MATCHING</b></para>
        /// <para>
        /// This method only checks if the index is in the registered list; it doesn't validate currency consistency.
        /// However, it's expected that all registered indices have currencies matching the simulator's currency.
        /// The user should ensure this when adding indices via constructor or AddForecast().
        /// </para>
        /// 
        /// <para><b>DEV NOTES</b></para>
        /// <para>
        /// - Uses simple list containment check (O(n) where n is number of indices)
        /// - For large numbers of indices, consider using HashSet for O(1) lookup
        /// - No validation that index is actually a FloatRateIndex type (duck typing approach)
        /// - Thread-safe for reads if _floatRateIndices is not modified after Prepare()
        /// </para>
        /// </remarks>
        public override bool ProvidesIndex(MarketObservable index)
        {
            return _floatRateIndices.Contains(index);
        }

        /// <summary>
        /// Adds a floating rate index to the list of market observables that this simulator can provide.
        /// This allows the simulator to be dynamically configured after construction to support additional indices.
        /// </summary>
        /// <param name="index">
        /// The <see cref="FloatRateIndex"/> to add (e.g., 3M LIBOR, 6M EURIBOR, 1M JIBAR).
        /// The index should have a currency matching this simulator's currency for consistency.
        /// </param>
        /// <remarks>
        /// <para><b>WHEN TO USE</b></para>
        /// <para>
        /// This method is useful when:
        /// - The required indices are not known at simulator construction time
        /// - You want to configure the simulator incrementally based on product requirements
        /// - You're building a generic valuation framework that discovers index dependencies dynamically
        /// </para>
        /// 
        /// <para><b>TIMING</b></para>
        /// <para>
        /// This method can be called:
        /// - After construction, before Reset()
        /// - After Reset(), before Prepare()
        /// - Generally, it's safest to add all required indices before starting the simulation workflow
        /// </para>
        /// <para>
        /// Adding indices after Prepare() is not recommended as the required dates may not have been registered.
        /// </para>
        /// 
        /// <para><b>MULTIPLE TENORS</b></para>
        /// <para>
        /// A key advantage of the Hull-White model is that multiple tenors can be added efficiently:
        /// </para>
        /// <para>
        ///     hwSim.AddForecast(Libor1M);
        ///     hwSim.AddForecast(Libor3M);
        ///     hwSim.AddForecast(Libor6M);
        /// </para>
        /// <para>
        /// All these indices are extracted from the same underlying short rate simulation using the analytical
        /// bond pricing formula with different tenors. No additional simulation overhead is incurred.
        /// </para>
        /// 
        /// <para><b>CURRENCY CONSISTENCY</b></para>
        /// <para>
        /// While not validated by this method, the added index should have a currency matching the simulator's currency.
        /// For example, if the simulator is for USD, add USD.LIBOR.3M, not EUR.EURIBOR.3M.
        /// Mixing currencies within a single simulator is not supported; use separate simulators for each currency.
        /// </para>
        /// 
        /// <para><b>DUPLICATE HANDLING</b></para>
        /// <para>
        /// The method does not check for duplicates. Adding the same index multiple times will result in
        /// multiple entries in the list, but this is harmless (ProvidesIndex will still return true).
        /// For cleaner code, avoid adding duplicates, or use HashSet instead of List internally.
        /// </para>
        /// 
        /// <para><b>DEV NOTES</b></para>
        /// <para>
        /// - Initializes _floatRateIndices if null (defensive programming for edge cases)
        /// - Not thread-safe: don't call concurrently from multiple threads
        /// - No validation of index properties (currency, tenor validity, etc.)
        /// - For large-scale systems, consider more sophisticated index management (e.g., index registry pattern)
        /// </para>
        /// </remarks>
        public void AddForecast(FloatRateIndex index)
        {
            if (_floatRateIndices == null) _floatRateIndices = new List<FloatRateIndex>();
            _floatRateIndices.Add(index);
        }
    }
}