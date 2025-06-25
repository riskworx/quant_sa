using MathNetSpline = MathNet.Numerics.Interpolation.LinearSpline;

namespace QuantSA.Core.CurvesAndSurfaces.InterpolationAdapters
{
    public class LinearSpline : IInterpolator1D
    {
        private readonly MathNetSpline _mathNetSpline;

        public LinearSpline(double[] xVals, double[] yVals)
        {
            _mathNetSpline = MathNetSpline.InterpolateSorted(xVals, yVals);
        }

        public double Interpolate(double x)
        {
            return _mathNetSpline.Interpolate(x);
        }
    }
}