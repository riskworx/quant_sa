using System;
using QuantSA.Shared.CurvesAndSurfaces;
using QuantSA.Shared.Dates;
using QuantSA.Core.CurvesAndSurfaces.InterpolationAdapters;

namespace QuantSA.Core.CurvesAndSurfaces
{
    public class InterpolatedCurve : ICurve
    {
        private readonly IInterpolator1D _interpolator;

        public InterpolatedCurve(double[] xVals, double[] yVals)
        {
            _interpolator = new LinearSpline(xVals, yVals);
        }

        public double InterpAtDate(Date date)
        {
            throw new NotImplementedException();
        }

        public double Interp(double requiredX)
        {
            return _interpolator.Interpolate(requiredX);
        }

        public double[,] Interp(double[,] requiredX)
        {
            var result = new double[requiredX.GetLength(0), requiredX.GetLength(1)];
            for (var i = 0; i < requiredX.GetLength(0); i++)
            for (var j = 0; j < requiredX.GetLength(1); j++)
                result[i, j] = _interpolator.Interpolate(requiredX[i, j]);
            return result;
        }
    }
}