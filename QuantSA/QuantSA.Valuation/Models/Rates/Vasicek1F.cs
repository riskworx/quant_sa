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
    public class Vasicek1F : NumeraireSimulator
    {
        private List<FloatRateIndex> _floatRateIndices;
        private readonly double _a;
        private readonly double _b;
        private readonly double _sigma;
        private readonly double _r0;
        private readonly Currency _currency;

        [JsonIgnore] private Date _anchorDate;
        [JsonIgnore] private List<Date> _allDates;
        [JsonIgnore] private double[] _allDatesDouble;
        [JsonIgnore] private double[] _r;
        [JsonIgnore] private double[] _bankAccount;
        [JsonIgnore] private NormalDistribution _dist;

        public Vasicek1F(Currency currency, double a, double b, double sigma, double r0,
            IEnumerable<FloatRateIndex> floatRateIndices = null)
        {
            _a = a;
            _b = b;
            _sigma = sigma;
            _r0 = r0;
            _currency = currency;
            _floatRateIndices = floatRateIndices != null ? floatRateIndices.ToList() : new List<FloatRateIndex>();
        }

        public override void Reset()
        {
            _allDates = new List<Date>();
        }
        /// <summary>
        /// Adds required dates for the given index to the model.
        /// Initializes the list if it's empty.
        /// </summary>
        /// <param name="index">The market observable needing the dates</param>
        /// <param name="requiredDates">Dates to include in the simulation</param>
        public override void SetRequiredDates(MarketObservable index, List<Date> requiredDates)
        {
            if (_allDates == null) _allDates = requiredDates;
            else _allDates.AddRange(requiredDates);
        }

        public override void SetNumeraireDates(List<Date> requiredDates)
        {
            if (_allDates == null) _allDates = requiredDates;
            else _allDates.AddRange(requiredDates);
        }

        public override void Prepare(Date anchorDate)
        {
            _anchorDate = anchorDate;
            _allDates.Insert(0, anchorDate);
            _allDates = _allDates.Distinct().ToList();
            _allDates.Sort();

            _allDatesDouble = _allDates.Select(d => (double)d).ToArray();
            _dist = new NormalDistribution();
            Generator.Seed = -1585814591; 
        }

        public override void RunSimulation(int simNumber)
        {
            var W = _dist.Generate(_allDates.Count - 1);
            _r = new double[_allDates.Count];
            _bankAccount = new double[_allDates.Count];
            _r[0] = _r0;
            _bankAccount[0] = 1;

            for (int i = 0; i < _allDates.Count - 1; i++)
            {
                var dt = (_allDates[i + 1] - _allDates[i]) / 365.0;
                _r[i + 1] = _r[i] + _a * (_b - _r[i]) * dt + _sigma * Math.Sqrt(dt) * W[i];
                _bankAccount[i + 1] = _bankAccount[i] * Math.Exp(_r[i] * dt);
            }
        }

        public override double[] GetIndices(MarketObservable index, List<Date> requiredDates)
        {
            var floatRateIndex = index as FloatRateIndex;
            var result = new double[requiredDates.Count];
            for (int i = 0; i < requiredDates.Count; i++)
            {
                var rt = Tools.Interpolate1D(requiredDates[i].value, _allDatesDouble, _r, _r[0], _r.Last());
                var tenor = floatRateIndex.Tenor;
                var date2 = requiredDates[i].AddTenor(tenor);
                var bondPrice = Math.Exp(-rt * (date2 - requiredDates[i]) / 365.0); 
                var rate = 365.0 * (1 / bondPrice - 1) / (date2 - requiredDates[i]);
                result[i] = rate;
            }
            return result;
        }

        public override double[] GetUnderlyingFactors(Date date)
        {
            var rt = Tools.Interpolate1D(date.value, _allDatesDouble, _r, _r[0], _r.Last());
            return new[] { rt };
        }

        public override Currency GetNumeraireCurrency() => _currency;

        public override double Numeraire(Date valueDate)
        {
            if (valueDate < _anchorDate)
                throw new ArgumentException($"Numeraire requested at: {valueDate} but model only starts at {_anchorDate}");
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
