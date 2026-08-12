using System;
using System.Numerics;

namespace PhotoAtomic.Numerics;

/// <summary>
/// Extends <see cref="decimal"/> with precision (scale) manipulation that preserves
/// significant zeros, working directly on the binary representation of the value.
/// </summary>
public static class DecimalExtension
{
    /// <summary>
    /// Rounds a decimal and sets the required number of decimal places.
    /// </summary>
    /// <param name="value">Value to be rounded.</param>
    /// <param name="precision">The number of decimal places.</param>
    /// <param name="midpointRounding">How to round the digit 5; by default away from zero.</param>
    /// <returns>A decimal with exactly <paramref name="precision"/> decimal places.</returns>
    public static decimal RoundWithPrecision(this decimal value, int precision, MidpointRounding midpointRounding = MidpointRounding.AwayFromZero)
    {
        return Math.Round(value, precision, midpointRounding).SetPrecision(precision);
    }

    /// <summary>
    /// Truncates a decimal to the given precision. If the precision exceeds the current number
    /// of decimal places, trailing zeros are added; if the precision is negative, integral
    /// digits are zeroed out.
    /// </summary>
    /// <param name="value">Value to truncate.</param>
    /// <param name="precision">Number of decimal places (negative to zero integral digits).</param>
    /// <returns>A decimal with the required number of decimal places.</returns>
    /// <exception cref="InvalidOperationException">The result does not fit in a decimal.</exception>
    public static decimal SetPrecision(this decimal value, int precision)
    {
        int factor = precision - value.GetPrecision();
        if (factor == 0) return value;

        BigInteger digits = GetSignificand(value);

        digits = factor > 0
            ? digits * BigInteger.Pow(Ten, factor)
            : digits / BigInteger.Pow(Ten, -factor);

        if (digits > MaxSignificand) throw new InvalidOperationException("Precision exceeded for type decimal");

        uint lo = (uint)(digits & uint.MaxValue);
        uint mid = (uint)((digits >> 32) & uint.MaxValue);
        uint hi = (uint)((digits >> 64) & uint.MaxValue);

        bool negative = value < 0;

        if (precision >= 0)
        {
            if (precision > MaxScale) throw new InvalidOperationException("Precision exceeded for type decimal");
            return new decimal((int)lo, (int)mid, (int)hi, negative, (byte)precision);
        }

        if (digits.IsZero) return decimal.Zero;

        var result = new decimal((int)lo, (int)mid, (int)hi, negative, 0);
        for (int i = 0; i < -precision; i++)
        {
            result *= 10m; // se il risultato non sta in un decimal, OverflowException
        }

        return result;
    }

    /// <summary>
    /// Returns the number of decimal places (the scale) of the value.
    /// </summary>
    /// <param name="value">Value to inspect.</param>
    /// <returns>Number of decimal places.</returns>
    public static int GetPrecision(this decimal value)
    {
#if NET8_0_OR_GREATER
        return value.Scale;
#else
        return (byte)((decimal.GetBits(value)[3] >> 16) & 0x000000FF);
#endif
    }

    private const int MaxScale = 28;

    private static readonly BigInteger Ten = new BigInteger(10);

    // Il significando di un decimal è un intero a 96 bit.
    private static readonly BigInteger MaxSignificand = (BigInteger.One << 96) - 1;

    private static BigInteger GetSignificand(decimal value)
    {
#if NET8_0_OR_GREATER
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
#else
        int[] bits = decimal.GetBits(value);
#endif
        // Composizione aritmetica dai tre interi senza segno: sempre positiva, a differenza
        // della costruzione da 12 byte little-endian, che interpreterebbe come negativo un
        // significando col bit 95 acceso.
        return ((BigInteger)(uint)bits[2] << 64) | ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
    }
}
