using System;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuantSA.Core.Optimization;
using QuantSA.Core.RootFinding;

namespace QuantSA.Core.Tests.RootFinding
{
    [TestClass]
    public class MultiDimNewtonTests
    {
        public class TestFunction : IObjectiveVectorFunction
        {
            public Vector<double> Point { get; set; }
            public Vector<double> Value { get; set; }

            public void EvaluateAt(Vector<double> point)
            {
                Point = point;
                Value = Vector<double>.Build.Dense(3);
                Value[0] = Math.Pow(point[0] - 1.0, 2);
                Value[1] = Math.Pow(point[1] - 2.0, 2);
                Value[2] = Math.Pow(point[2] - 3.0, 2);
            }
        }
        
        public class NeverConvergesFunction : IObjectiveVectorFunction
        {
            private int _callCount = 0;
            public Vector<double> Point { get; set; }
            public Vector<double> Value { get; set; }

            public void EvaluateAt(Vector<double> point)
            {
                Point = point;
                _callCount++;
                Value = Vector<double>.Build.DenseOfArray(new[]
                {
                    999.0 + _callCount,
                    999.0 + _callCount,
                    999.0 + _callCount
                });
            }
        }
        
        public class OneInsensitiveInputFunction : IObjectiveVectorFunction
        {
            public Vector<double> Point { get; set; }
            public Vector<double> Value { get; set; }

            public void EvaluateAt(Vector<double> point)
            {
                Point = point;

                double x = point[0];
                double y = point[1];

                Value = Vector<double>.Build.DenseOfArray(new[]
                {
                    x + y,
                    x - y,
                    x * y
                });
            }
        }

        [TestMethod]
        public void MultiDimNewton_CanFindRoot()
        {
            var t = new TestFunction();
            var mdnSolver = new MultiDimNewton(1e-6, 100);
            var initialGuess = new DenseVector(new[] { 1.0, 1.0, 1.0 });
            var solution = mdnSolver.FindRoot(t, initialGuess);
            t.EvaluateAt(solution.MinimizingPoint);
            Assert.AreEqual(0.0, t.Value.AbsoluteMaximum(), 1e-6);
        }

        [TestMethod]
        public void MultiDimNewton_ImmediateConvergence()
        {
            var t = new TestFunction();
            var mdnSolver = new MultiDimNewton(1e-6, 100);
            var root = Vector<double>.Build.DenseOfArray(new[] { 1.0, 2.0, 3.0 });

            var result = mdnSolver.FindRoot(t, root);

            Assert.AreEqual(0.0, t.Value.AbsoluteMaximum(), 1e-6);
            Assert.AreEqual(0, result.Iterations);
        }

        [TestMethod]
        public void MultiDimNewton_StopsAfterMaxIterations()
        {
            var f = new NeverConvergesFunction();
            var solver = new MultiDimNewton(1e-12, 3);
            var initialGuess = Vector<double>.Build.Dense(3, 0.0);

            var result = solver.FindRoot(f, initialGuess);

            Assert.AreEqual(3, result.Iterations, "Solver did not run up to the maximum iteration count.");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void MultiDimNewton_ThrowsIfJacobianColumnIsZero()
        {
            var f = new OneInsensitiveInputFunction();
            var solver = new MultiDimNewton(1e-8, 10);
            var initialGuess = Vector<double>.Build.DenseOfArray(new[] { 1.0, 2.0, 3.0 }); 

            solver.FindRoot(f, initialGuess);
        }
    }
}