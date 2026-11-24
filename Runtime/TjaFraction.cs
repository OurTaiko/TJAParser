using System;
using System.Globalization;
using System.Numerics;

namespace OurTaiko
{
    /// <summary>An exact rational number. The default value is zero.</summary>
    public readonly struct TjaFraction : IEquatable<TjaFraction>
    {
        readonly BigInteger numerator, denominator;
        public BigInteger Numerator => numerator;
        public BigInteger Denominator => denominator.IsZero ? BigInteger.One : denominator;
        public bool IsInteger => numerator % Denominator == 0;
        public BigInteger Floor
        {
            get
            {
                var quotient = BigInteger.DivRem(numerator, Denominator, out var remainder);
                return remainder.Sign < 0 ? quotient - 1 : quotient;
            }
        }
        public BigInteger Ceiling => -(-this).Floor;

        public TjaFraction(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.IsZero) throw new DivideByZeroException();
            if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
            if (denominator.IsOne) { this.numerator = numerator; this.denominator = denominator; return; }
            var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
            this.numerator = numerator / gcd;
            this.denominator = denominator / gcd;
        }

        public static implicit operator TjaFraction(int value) => new TjaFraction(value, 1);
        public static TjaFraction operator -(TjaFraction a) => new TjaFraction(-a.numerator, a.Denominator);
        public static TjaFraction operator +(TjaFraction a, TjaFraction b)
        {
            if (a.numerator.IsZero) return b;
            if (b.numerator.IsZero) return a;
            if (a.Denominator == b.Denominator) return new TjaFraction(a.numerator + b.numerator, a.Denominator);
            return new TjaFraction(a.numerator * b.Denominator + b.numerator * a.Denominator, a.Denominator * b.Denominator);
        }
        public static TjaFraction operator -(TjaFraction a, TjaFraction b) => a + -b;
        public static TjaFraction operator *(TjaFraction a, TjaFraction b)
            => new TjaFraction(a.numerator * b.numerator, a.Denominator * b.Denominator);
        public static TjaFraction operator /(TjaFraction a, TjaFraction b)
            => new TjaFraction(a.numerator * b.Denominator, a.Denominator * b.numerator);
        public static bool operator <(TjaFraction a, TjaFraction b) => a.numerator * b.Denominator < b.numerator * a.Denominator;
        public static bool operator >(TjaFraction a, TjaFraction b) => b < a;
        public bool Equals(TjaFraction other) => numerator == other.numerator && Denominator == other.Denominator;
        public override bool Equals(object obj) => obj is TjaFraction other && Equals(other);
        public override int GetHashCode() => numerator.GetHashCode() * 397 ^ Denominator.GetHashCode();
        public override string ToString() => numerator.ToString(CultureInfo.InvariantCulture) + "/" + Denominator.ToString(CultureInfo.InvariantCulture);

        // Read the decimal spelling directly, including exponents and TJA decimal commas.
        // Bound the scale so a tiny exponent such as 1e-2147483647 cannot exhaust memory.
        internal static bool TryParse(string text, out TjaFraction value)
        {
            value = default;
            text = TjaNumber.Normalize(text);
            int exponent = 0, e = text.IndexOfAny(new[] { 'e', 'E' });
            if (e >= 0)
            {
                if (!int.TryParse(text.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) return false;
                text = text.Substring(0, e);
            }
            int dot = text.IndexOf('.');
            long scale = (dot < 0 ? 0 : text.Length - dot - 1L) - exponent;
            if (dot >= 0) text = text.Remove(dot, 1);
            if (!BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var digits)) return false;
            if (digits.IsZero) return true;
            if (scale < -10000 || scale > 10000) return false;
            value = scale >= 0 ? new TjaFraction(digits, BigInteger.Pow(10, (int)scale))
                : new TjaFraction(digits * BigInteger.Pow(10, (int)-scale), 1);
            return true;
        }
    }
}
