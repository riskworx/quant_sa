using System;
using System.Collections.Generic;
using System.Text;

namespace QuantSA.Core.CurvesAndSurfaces
{
    public interface IInterpolator1D
    {
        double Interpolate(double x);
    }
}
