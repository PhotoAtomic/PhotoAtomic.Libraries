namespace PhotoAtomic.Numerics
{
    using System;
    using System.Linq;
    using System.Numerics;

    /// <summary>
    /// Extends the System.Decimal datatypes
    /// </summary>
    public static class DecimalExtension
    {        
        /// <summary>
        /// Rounds a decimal and sets the requied number of decimal places
        /// </summary>
        /// <param name="value">Value to be rounded</param>
        /// <param name="precision">The number of decimal places</param>
        /// <param name="midpointRounding">Type of midpoint rounding (how to consider digit 5).By default 5 rounds to the upper value</param>
        /// <returns>A decimal number with the required amount of decimal places</returns>
        public static decimal RoundWithPrecision(this decimal value, int precision, MidpointRounding midpointRounding = MidpointRounding.AwayFromZero)
        {
            return Math.Round(value, precision, midpointRounding).SetPrecision(precision);
        }

        /// <summary>
        /// Truncate a decimal value to the given precision, if the precision exceeds the number of decimal places zeroes are added at the end, if the precision is negative integral digits are transformed in zeroes
        /// </summary>
        /// <param name="value">value to truncate</param>
        /// <param name="precision">number of decimal places</param>
        /// <returns>a decimal value with the required amount of decimal places</returns>
        public static decimal SetPrecision(this decimal value, int precision)
        {
            var actualPrecision = value.GetPrecision();
            var bits = Decimal.GetBits(value).Take(3).SelectMany(x => BitConverter.GetBytes(x));
            BigInteger digits = new BigInteger(bits.ToArray());

            var factor = precision - actualPrecision;

            if (factor == 0) return value;

            if (factor > 0)
            {
                digits *= PowerOfTen(Math.Abs(factor));
            }
            else
            {
                digits /= PowerOfTen(Math.Abs(factor));
            }
            if (digits.ToByteArray().Count() > 12) throw new InvalidOperationException("Precision Exceeded for type decimal");
            
            var newBits =
               digits.ToByteArray().RightFill(12).Chunk(4)
               .Select(x =>
                   BitConverter.ToUInt32(x.ToArray(), 0))
               .ToArray();
            
            if (precision > 0)
            {
                return new Decimal((int)newBits[0], (int)newBits[1], (int)newBits[2], value < 0, (byte)precision);
            }
            else
            {
                return new Decimal((int)newBits[0], (int)newBits[1], (int)newBits[2], value < 0, 0) * PowerOfTen(-precision);
            }
        }

        /// <summary>
        /// returns the number of decimal places contained in the value
        /// </summary>
        /// <param name="value">value to check</param>
        /// <returns>number of decimal places</returns>
        public static int GetPrecision(this decimal value)
        {
            byte precision = (byte)((Decimal.GetBits(value)[3] >> 16) & 0x000000FF);
            return precision;
        }
      
        private static int PowerOfTen(int times)
        {
            return (int)Math.Pow(10, times);
        }      
    }
}

