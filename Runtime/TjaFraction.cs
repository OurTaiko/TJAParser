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

        // Decimal places kept when reading a number: far past a double's 17 significant digits, and
        // small enough that the parser's per-slot sums stay cheap whatever the source writes.
        internal const int MaxDecimals = 40;
        // A power of ten past a double's range (about 1e308); a number the parser accepts as a
        // finite double never gets there.
        const int MaxMagnitude = 400;

        // Read the decimal spelling directly, including exponents and TJA decimal commas, rounded
        // half away from zero to MaxDecimals places. False only for a spelling that is not a decimal
        // number, or one past a double's range.
        internal static bool TryParse(string text, out TjaFraction value)
        {
            value = default;
            text = TjaNumber.Normalize(text);
            BigInteger exponent = 0;
            int e = text.IndexOfAny(new[] { 'e', 'E' });
            if (e >= 0)
            {
                if (!BigInteger.TryParse(text.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) return false;
                text = text.Substring(0, e);
            }
            int dot = text.IndexOf('.');
            var scale = (dot < 0 ? 0 : text.Length - dot - 1) - exponent;
            if (dot >= 0) text = text.Remove(dot, 1);
            if (!BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var digits)) return false;
            if (digits.IsZero) return true;
            if (scale < 0)
            {
                // digits ≥ 1, so the value is at least 10^-scale.
                if (-scale > MaxMagnitude) return false;
                value = new TjaFraction(digits * BigInteger.Pow(10, (int)-scale), 1);
                return true;
            }
            if (scale > MaxDecimals)
            {
                // |digits| < 10^text.Length: dropping more places than that leaves less than 0.1 of
                // the last kept place, which rounds to zero (and keeps the power below small).
                var drop = scale - MaxDecimals;
                if (drop > text.Length) return true;
                var unit = BigInteger.Pow(10, (int)drop);
                var quotient = BigInteger.DivRem(digits, unit, out var remainder);
                if (BigInteger.Abs(remainder) * 2 >= unit) quotient += digits.Sign;
                digits = quotient;
                scale = MaxDecimals;
            }
            value = new TjaFraction(digits, BigInteger.Pow(10, (int)scale));
            return true;
        }
    }
}
