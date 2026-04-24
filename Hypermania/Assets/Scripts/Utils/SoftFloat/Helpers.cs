using System;
using System.Runtime.CompilerServices;

namespace Utils.SoftFloat
{
    public static partial class Mathsf
    {
        public static readonly sfloat Epsilon = (sfloat)1E-5f;
        public static readonly sfloat EpsilonNormalSqrt = (sfloat)1E-15f;

        //
        // Summary:
        //     Returns the absolute value of f.
        //
        // Parameters:
        //   f:
        public static sfloat Abs(sfloat f)
        {
            return sfloat.Abs(f);
        }

        //
        // Summary:
        //     Returns the absolute value of value.
        //
        // Parameters:
        //   value:
        public static int Abs(int value)
        {
            return Math.Abs(value);
        }

        //
        // Summary:
        //     Returns the smallest of two or more values.
        //
        // Parameters:
        //   a:
        //
        //   b:
        //
        //   values:
        public static sfloat Min(sfloat a, sfloat b)
        {
            return (a < b) ? a : b;
        }

        //
        // Summary:
        //     Returns the smallest of two or more values.
        //
        // Parameters:
        //   a:
        //
        //   b:
        //
        //   values:
        public static sfloat Min(params sfloat[] values)
        {
            int num = values.Length;
            if (num == 0)
            {
                return sfloat.Zero;
            }

            sfloat num2 = values[0];
            for (int i = 1; i < num; i++)
            {
                if (values[i] < num2)
                {
                    num2 = values[i];
                }
            }

            return num2;
        }

        //
        // Summary:
        //     Returns the smallest of two or more values.
        //
        // Parameters:
        //   a:
        //
        //   b:
        //
        //   values:
        public static int Min(int a, int b)
        {
            return (a < b) ? a : b;
        }

        //
        // Summary:
        //     Returns the smallest of two or more values.
        //
        // Parameters:
        //   a:
        //
        //   b:
        //
        //   values:
        public static int Min(params int[] values)
        {
            int num = values.Length;
            if (num == 0)
            {
                return 0;
            }

            int num2 = values[0];
            for (int i = 1; i < num; i++)
            {
                if (values[i] < num2)
                {
                    num2 = values[i];
                }
            }

            return num2;
        }

        //
        // Summary:
        //     Returns the largest of two or more values. When comparing negative values, values
        //     closer to zero are considered larger.
        //
        // Parameters:
        //   a:
        //
        //   b:
        //
        //   values:
        public static sfloat Max(sfloat a, sfloat b)
        {
            return (a > b) ? a : b;
        }

        //
        // Summary:
        //     Returns the largest of two or more values. When comparing negative values, values
        //     closer to zero are considered larger.
        //
        // Parameters:
        //   a:
        //
        //   b:
        //
        //   values:
        public static sfloat Max(params sfloat[] values)
        {
            int num = values.Length;
            if (num == 0)
            {
                return sfloat.Zero;
            }

            sfloat num2 = values[0];
            for (int i = 1; i < num; i++)
            {
                if (values[i] > num2)
                {
                    num2 = values[i];
                }
            }

            return num2;
        }

        //
        // Summary:
        //     Returns the largest value. When comparing negative values, values closer to zero
        //     are considered larger.
        //
        // Parameters:
        //   a:
        //
        //   b:
        //
        //   values:
        public static int Max(int a, int b)
        {
            return (a > b) ? a : b;
        }

        //
        // Summary:
        //     Returns the largest value. When comparing negative values, values closer to zero
        //     are considered larger.
        //
        // Parameters:
        //   a:
        //
        //   b:
        //
        //   values:
        public static int Max(params int[] values)
        {
            int num = values.Length;
            if (num == 0)
            {
                return 0;
            }

            int num2 = values[0];
            for (int i = 1; i < num; i++)
            {
                if (values[i] > num2)
                {
                    num2 = values[i];
                }
            }

            return num2;
        }

        //
        // Summary:
        //     Returns the logarithm of a specified number in a specified base.
        //
        // Parameters:
        //   f:
        //
        //   p:
        public static sfloat Log(sfloat f, sfloat p)
        {
            return Log(f) / Log(p);
        }

        //
        // Summary:
        //     Returns the base 10 logarithm of a specified number.
        //
        // Parameters:
        //   f:
        public static sfloat Log10(sfloat f)
        {
            return Log(f) / Log((sfloat)10f);
        }

        //
        // Summary:
        //     Returns the smallest integer greater to or equal to f.
        //
        // Parameters:
        //   f:
        public static int CeilToInt(sfloat f)
        {
            return (int)Ceil(f);
        }

        //
        // Summary:
        //     Returns the largest integer smaller to or equal to f.
        //
        // Parameters:
        //   f:
        public static int FloorToInt(sfloat f)
        {
            return (int)Floor(f);
        }

        //
        // Summary:
        //     Returns f rounded to the nearest integer.
        //
        // Parameters:
        //   f:
        public static int RoundToInt(sfloat f)
        {
            return (int)Round(f);
        }

        //
        // Summary:
        //     Returns the sign of f.
        //
        // Parameters:
        //   f:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sfloat Sign(sfloat f)
        {
            return (f >= sfloat.Zero) ? sfloat.One : sfloat.MinusOne;
        }

        //
        // Summary:
        //     Clamps the given value between the given minimum sfloat and maximum sfloat values.
        //     Returns the given value if it is within the minimum and maximum range.
        //
        // Parameters:
        //   value:
        //     The sfloating point value to restrict inside the range defined by the minimum
        //     and maximum values.
        //
        //   min:
        //     The minimum sfloating point value to compare against.
        //
        //   max:
        //     The maximum sfloating point value to compare against.
        //
        // Returns:
        //     The sfloat result between the minimum and maximum values.
        public static sfloat Clamp(sfloat value, sfloat min, sfloat max)
        {
            if (value < min)
            {
                value = min;
            }
            else if (value > max)
            {
                value = max;
            }

            return value;
        }

        //
        // Summary:
        //     Clamps the given value between a range defined by the given minimum integer and
        //     maximum integer values. Returns the given value if it is within min and max.
        //
        //
        // Parameters:
        //   value:
        //     The integer point value to restrict inside the min-to-max range.
        //
        //   min:
        //     The minimum integer point value to compare against.
        //
        //   max:
        //     The maximum integer point value to compare against.
        //
        // Returns:
        //     The int result between min and max values.
        public static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                value = min;
            }
            else if (value > max)
            {
                value = max;
            }

            return value;
        }

        //
        // Summary:
        //     Clamps value between 0 and 1 and returns value.
        //
        // Parameters:
        //   value:
        public static sfloat Clamp01(sfloat value)
        {
            if (value < sfloat.Zero)
            {
                return sfloat.Zero;
            }

            if (value > sfloat.One)
            {
                return sfloat.One;
            }

            return value;
        }

        //
        // Summary:
        //     Linearly interpolates between a and b by t.
        //
        // Parameters:
        //   a:
        //     The start value.
        //
        //   b:
        //     The end value.
        //
        //   t:
        //     The interpolation value between the two sfloats.
        //
        // Returns:
        //     The interpolated sfloat result between the two sfloat values.
        public static sfloat Lerp(sfloat a, sfloat b, sfloat t)
        {
            return a + (b - a) * Clamp01(t);
        }

        //
        // Summary:
        //     Linearly interpolates between a and b by t with no limit to t.
        //
        // Parameters:
        //   a:
        //     The start value.
        //
        //   b:
        //     The end value.
        //
        //   t:
        //     The interpolation between the two sfloats.
        //
        // Returns:
        //     The sfloat value as a result from the linear interpolation.
        public static sfloat LerpUnclamped(sfloat a, sfloat b, sfloat t)
        {
            return a + (b - a) * t;
        }

        //
        // Summary:
        //     Same as Lerp but makes sure the values interpolate correctly when they wrap around
        //     360 degrees.
        //
        // Parameters:
        //   a:
        //     The start angle. A sfloat expressed in degrees.
        //
        //   b:
        //     The end angle. A sfloat expressed in degrees.
        //
        //   t:
        //     The interpolation value between the start and end angles. This value is clamped
        //     to the range [0, 1].
        //
        // Returns:
        //     Returns the interpolated sfloat result between angle a and angle b, based on the
        //     interpolation value t.
        public static sfloat LerpAngle(sfloat a, sfloat b, sfloat t)
        {
            sfloat num = Repeat(b - a, (sfloat)360f);
            if (num > (sfloat)180f)
            {
                num -= (sfloat)360f;
            }

            return a + num * Clamp01(t);
        }

        //
        // Summary:
        //     Moves a value current towards target.
        //
        // Parameters:
        //   current:
        //     The current value.
        //
        //   target:
        //     The value to move towards.
        //
        //   maxDelta:
        //     The maximum change applied to the current value.
        public static sfloat MoveTowards(sfloat current, sfloat target, sfloat maxDelta)
        {
            if (Abs(target - current) <= maxDelta)
            {
                return target;
            }

            return current + Sign(target - current) * maxDelta;
        }

        //
        // Summary:
        //     Same as MoveTowards but makes sure the values interpolate correctly when they
        //     wrap around 360 degrees.
        //
        // Parameters:
        //   current:
        //
        //   target:
        //
        //   maxDelta:
        public static sfloat MoveTowardsAngle(sfloat current, sfloat target, sfloat maxDelta)
        {
            sfloat num = DeltaAngle(current, target);
            if (sfloat.Zero - maxDelta < num && num < maxDelta)
            {
                return target;
            }

            target = current + num;
            return MoveTowards(current, target, maxDelta);
        }

        //
        // Summary:
        //     Interpolates between from and to with smoothing at the limits.
        //
        // Parameters:
        //   from:
        //     The start of the range.
        //
        //   to:
        //     The end of the range.
        //
        //   t:
        //     The interpolation value between the from and to range limits.
        //
        // Returns:
        //     The interpolated sfloat result between from and to.
        public static sfloat SmoothStep(sfloat from, sfloat to, sfloat t)
        {
            t = Clamp01(t);
            t = (sfloat)(-2f) * t * t * t + (sfloat)(3f) * t * t;
            return to * t + from * (sfloat.One - t);
        }

        public static sfloat Gamma(sfloat value, sfloat absmax, sfloat gamma)
        {
            bool flag = value < sfloat.Zero;
            sfloat num = Abs(value);
            if (num > absmax)
            {
                return flag ? (sfloat.Zero - num) : num;
            }

            sfloat num2 = Pow(num / absmax, gamma) * absmax;
            return flag ? (sfloat.Zero - num2) : num2;
        }

        //
        // Summary:
        //     Compares two sfloating point values and returns true if they are similar.
        //
        // Parameters:
        //   a:
        //
        //   b:
        public static bool Approximately(sfloat a, sfloat b)
        {
            return Abs(b - a) < Max((sfloat)1E-06f * Max(Abs(a), Abs(b)), Epsilon * (sfloat)8f);
        }

        //
        // Summary:
        //     Loops the value t, so that it is never larger than length and never smaller than
        //     0.
        //
        // Parameters:
        //   t:
        //
        //   length:
        public static sfloat Repeat(sfloat t, sfloat length)
        {
            return Clamp(t - Floor(t / length) * length, sfloat.Zero, length);
        }

        //
        // Summary:
        //     PingPong returns a value that increments and decrements between zero and the
        //     length. It follows the triangle wave formula where the bottom is set to zero
        //     and the peak is set to length.
        //
        // Parameters:
        //   t:
        //
        //   length:
        public static sfloat PingPong(sfloat t, sfloat length)
        {
            t = Repeat(t, length * (sfloat)2f);
            return length - Abs(t - length);
        }

        //
        // Summary:
        //     Determines where a value lies between two points.
        //
        // Parameters:
        //   a:
        //     The start of the range.
        //
        //   b:
        //     The end of the range.
        //
        //   value:
        //     The point within the range you want to calculate.
        //
        // Returns:
        //     A value between zero and one, representing where the "value" parameter falls
        //     within the range defined by a and b.
        public static sfloat InverseLerp(sfloat a, sfloat b, sfloat value)
        {
            if (a != b)
            {
                return Clamp01((value - a) / (b - a));
            }

            return sfloat.Zero;
        }

        //
        // Summary:
        //     Calculates the shortest difference between two angles.
        //
        // Parameters:
        //   current:
        //     The current angle in degrees.
        //
        //   target:
        //     The target angle in degrees.
        //
        // Returns:
        //     A value between -179 and 180, in degrees.
        public static sfloat DeltaAngle(sfloat current, sfloat target)
        {
            sfloat num = Repeat(target - current, (sfloat)360f);
            if (num > (sfloat)180f)
            {
                num -= (sfloat)360f;
            }

            return num;
        }

        //
        // Summary:
        //     Returns the next power of two that is equal to, or greater than, the argument.
        //
        //
        // Parameters:
        //   value:
        public static int NextPowerOfTwo(int value)
        {
            value--;
            value |= value >> 16;
            value |= value >> 8;
            value |= value >> 4;
            value |= value >> 2;
            value |= value >> 1;
            return value + 1;
        }

        //
        // Summary:
        //     Returns the closest power of two value.
        //
        // Parameters:
        //   value:
        public static int ClosestPowerOfTwo(int value)
        {
            int num = NextPowerOfTwo(value);
            int num2 = num >> 1;
            if (value - num2 < num - value)
            {
                return num2;
            }

            return num;
        }

        //
        // Summary:
        //     Returns true if the value is power of two.
        //
        // Parameters:
        //   value:
        public static bool IsPowerOfTwo(int value)
        {
            return (value & (value - 1)) == 0;
        }
    }
}
