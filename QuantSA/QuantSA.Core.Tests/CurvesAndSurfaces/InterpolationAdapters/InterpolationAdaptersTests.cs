using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuantSA.Core.CurvesAndSurfaces;

namespace QuantSA.Core.Tests.CurvesAndSurfaces.InterpolationAdapters
{
    [TestClass]
    public class InterpolatedCurveTests
    {
        private double[] _xVals;
        private double[] _yVals;

        [TestInitialize]
        public void Setup()
        {
            _xVals = new double[] { 1, 2, 3 };
            _yVals = new double[] { 10, 20, 30 };
        }
        [TestMethod]
        public void InterpolatedCurve_ReturnsValues_AsExpected()
        {
            var curve = new InterpolatedCurve(_xVals, _yVals);

            double valueAt1_5 = curve.Interp(1.5);

            Assert.AreEqual(15, valueAt1_5, 1e-10, "Interpolation failed");
        }

        [TestMethod]
        public void InterpolatedCurve_ExtrapolatesLeft_ReturnsASExpected()
        {
            var curve = new InterpolatedCurve(_xVals, _yVals);

            double valueAt0_5 = curve.Interp(0.5);

            Assert.AreEqual(5, valueAt0_5, 1e-10, "Left extrapolation failed");
        }

        [TestMethod]
        public void InterpolatedCurve_ExtrapolatesRight_ReturnsASExpected()
        {
            var curve = new InterpolatedCurve(_xVals, _yVals);

            double valueAt3_5 = curve.Interp(3.5);

            Assert.AreEqual(35, valueAt3_5, 1e-10, "Right extrapolation failed");
        }
    }
}