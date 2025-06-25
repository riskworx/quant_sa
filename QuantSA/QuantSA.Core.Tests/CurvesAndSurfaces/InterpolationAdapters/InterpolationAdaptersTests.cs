using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuantSA.Core.CurvesAndSurfaces;

namespace QuantSA.Core.Tests.CurvesAndSurfaces.InterpolationAdapters
{
    [TestClass]
    public class InterpolatedCurveTests
    {
        [TestMethod]
        public void InterpolatedCurve_ReturnsValues_AsExpected()
        {
            double[] xVals = { 1, 2, 3 };
            double[] yVals = { 10, 20, 30 };
            var curve = new InterpolatedCurve(xVals, yVals);

            double valueAt1_5 = curve.Interp(1.5);

            Assert.AreEqual(15, valueAt1_5, 1e-10, "Interpolation failed");
        }
    }
}