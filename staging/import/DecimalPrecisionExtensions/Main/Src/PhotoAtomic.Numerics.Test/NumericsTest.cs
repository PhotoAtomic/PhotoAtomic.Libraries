using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpTestsEx;
using DecimalExtensions;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using PhotoAtomic.Numerics;

namespace DecimalExtensions.test
{
    [TestClass]
    public class NumericsTest
    {
        [TestMethod]
        public void ReportAndNumericSetPrecision_Expected_Equivalent()
        {
            int precision = 3;
            decimal value = 123.4567m;
            var provider = new CultureInfo("en-US");
            GetSeedsReportFormat(value, precision).Should().Be(value.RoundWithPrecision(precision).ToString(provider));
                                                               
            value = 123.4m;
            GetSeedsReportFormat(value, precision).Should().Be(value.RoundWithPrecision(precision).ToString(provider));
                                                               
            value = 123m;
            GetSeedsReportFormat(value, precision).Should().Be(value.RoundWithPrecision(precision).ToString(provider));

            value = 123.4564m;
            GetSeedsReportFormat(value, precision).Should().Be(value.RoundWithPrecision(precision).ToString(provider));

            value = 123.4565m;
            GetSeedsReportFormat(value, precision).Should().Be(value.RoundWithPrecision(precision).ToString(provider));

            value = 123.4575m;
            GetSeedsReportFormat(value, precision).Should().Be(value.RoundWithPrecision(precision).ToString(provider));

            value = 666.9999m;
            GetSeedsReportFormat(value, precision).Should().Be(value.RoundWithPrecision(precision).ToString(provider));
        }

        private static string GetSeedsReportFormat(decimal value, int precision)
        {
            NumberFormatInfo nfi = new CultureInfo("en-US", false).NumberFormat;
            nfi.NumberDecimalDigits = precision;
            nfi.NumberGroupSeparator = "";

            //Divido la in due stringhe la parte intera e la parte decimale

            var decimalInReport = value.ToString("N", nfi);
            return decimalInReport;
        }

        [TestMethod]
        public void VaringDecimalPrecision_TruncatesOrAddsCorrectDigits()
        {
            var value = 123.456m;

            var provider = new CultureInfo("en-US");

            value.SetPrecision(0).ToString(provider).Should().Be("123");
            value.SetPrecision(1).ToString(provider).Should().Be("123.4");
            value.SetPrecision(2).ToString(provider).Should().Be("123.45");
            value.SetPrecision(3).ToString(provider).Should().Be("123.456");
            value.SetPrecision(4).ToString(provider).Should().Be("123.4560");
        }

        [TestMethod]
        public void VaringIntegralPrecision_ZeroesIntegralDigits()
        {
            var value = 123.456m;

            var provider = new CultureInfo("en-US");

            value.SetPrecision(-1).ToString(provider).Should().Be("120");
            value.SetPrecision(-2).ToString(provider).Should().Be("100");
            value.SetPrecision(-3).ToString(provider).Should().Be("0");
            value.SetPrecision(-4).ToString(provider).Should().Be("0");
        }


        [TestMethod]
        public void TestWithLongNumbers()
        {
            var value = 999988798765123.456m;

            var provider = new CultureInfo("en-US");

            value.SetPrecision(-1).ToString(provider).Should().Be("999988798765120");
            value.SetPrecision(-2).ToString(provider).Should().Be("999988798765100");
            value.SetPrecision(-3).ToString(provider).Should().Be("999988798765000");
            value.SetPrecision(-4).ToString(provider).Should().Be("999988798760000");
        }
       

        [TestMethod]
        public void TestForRightFillExtension_Expected_ZeroesAddedInBack()
        {
            var array = new List<int> { 1, 2 };
            var filled = array.RightFill(4);

            filled.SequenceEqual(new int[] { 1, 2, 0, 0 }).Should().Be.True();
        }

        [TestMethod]
        public void TestForLeftFillExtension_Expected_ZeroesAddedInFront()
        {
            var array = new List<int> { 1, 2 };
            var filled = array.LeftFill(4);

            filled.SequenceEqual(new int[] { 0, 0, 1, 2 }).Should().Be.True();
        }
    }
}
