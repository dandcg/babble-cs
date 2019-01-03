using KellermanSoftware.CompareNetObjects;
using Xunit;

namespace Babble.Test.Helpers
{
    public static class CompareExtensions
    {
        public static void ShouldCompareTo<T1, T2>(this T1 actual, T2 expected, CompareLogic compareLogic = null)
        {
            compareLogic = compareLogic ?? new CompareLogic() { Config = new ComparisonConfig() { MaxDifferences = 100 } };

            var result = compareLogic.Compare(expected, actual);

            Assert.True(result.AreEqual,result.DifferencesString);
        }

    }
}
