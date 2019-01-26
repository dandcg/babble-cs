using System;
using Babble.Core;
using KellermanSoftware.CompareNetObjects;
using Xunit;

namespace Babble.Test.Utils
{
    public static class AssertExtensions
    {
        public static void ShouldCompareTo<T1, T2>(this T1 actual, T2 expected, string userMessage)
        {
            var compareLogic = new CompareLogic {Config = new ComparisonConfig {MaxDifferences = 100}};

            var result = compareLogic.Compare(expected, actual);

            Assert.True(result.AreEqual, $"{userMessage}{Environment.NewLine}{result.DifferencesString}");
        }

        public static void ShouldCompareTo<T1, T2>(this T1 actual, T2 expected, CompareLogic compareLogic = null)
        {
            compareLogic = compareLogic ?? new CompareLogic {Config = new ComparisonConfig {MaxDifferences = 100}};

            var result = compareLogic.Compare(expected, actual);

            Assert.True(result.AreEqual, result.DifferencesString);
        }

        public static void IsNotError(this BabbleError err, string userMessage=null)
        {
            Assert.True(err==null,userMessage);
        }

    }
}