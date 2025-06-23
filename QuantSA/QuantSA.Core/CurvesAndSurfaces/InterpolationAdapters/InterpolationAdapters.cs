using MathNet.Numerics.Interpolation;

namespace QuantSA.Core.CurvesAndSurfaces
{
    public class LinearSplineAdapter : IInterpolator1D
    {
        private readonly LinearSpline _spline;

        public LinearSplineAdapter(double[] xVals, double[] yVals)
        {
            _spline = LinearSpline.InterpolateSorted(xVals, yVals);
        }

        public double Interpolate(double x)
        {
            return _spline.Interpolate(x);
        }
    }
}