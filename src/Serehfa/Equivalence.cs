namespace Serehfa
{
    using System;
    using System.Collections.Generic;
    using static ArgHelpers;

    public static class Equivalence
    {
        /// <summary>
        ///  Object equivalence check. This is a slightly more fuzzy
        ///  version of <c>eq?</c>, but with value equivalence for
        ///  Our various boxed values.
        /// </summary>
        [LispBuiltin("eqv?")]
        public static object Equiv(object[] args)
        {
            return UnpackArgs<object, object>(args) switch
            {
                (bool a, bool b) => a == b,
                (Ident a, Ident b) => a.Equals(b),
                (double a, double b) => a == b,
                (char a, char b) => a == b,
                (Func<object[], object> a, Func<object[], object> b) =>
                    a.Target == b.Target && a.Method == b.Method,
                (null, null) => true,
                (object lhs, object rhs) =>
                    (lhs != null && rhs != null) &&
                    object.ReferenceEquals(lhs, rhs),
            };
        }

        /// <summary>
        ///  Simple pointer-style equivalence. For our scheme we have
        ///  to handle identifiers + procedures specially as they
        ///  aren't completely transparent references.
        /// </summary>
        [LispBuiltin("eq?")]
        public static object Eq(object[] args)
        {
            return UnpackArgs<object, object>(args) switch
            {
                (Ident a, Ident b) => a.Equals(b),
                (Func<object[], object> a, Func<object[], object> b) =>
                    a.Target == b.Target && a.Method == b.Method,
                (null, null) => true,
                (object lhs, object rhs) =>
                    (lhs != null && rhs != null) &&
                    object.ReferenceEquals(lhs, rhs),
            };
        }

        [LispBuiltin("equal?")]
        public static object Equal(object[] args)
        {
            var (left, right) = UnpackArgs<object, object>(args);
            return LispEqualityComparer.Default.Equals(left, right);
        }

        private class LispEqualityComparer : EqualityComparer<object>
        {
            public override bool Equals(object left, object right)
            {
                return (left, right) switch
                {
                    (ConsPair l, ConsPair r) =>
                        Equals(l.Car, r.Car) && Equals(l.Cdr, r.Cdr),
                    (object[] l, object[] r) => ArrayEq(l, r),
                    (bool[] l, bool[] r) => ArrayEq(l, r),
                    _ => EqualityComparer<object>.Default.Equals(left, right),
                };
            }

            private bool ArrayEq<T>(T[] left, T[] right)
            {
                if (left.Length != right.Length)
                {
                    return false;
                }

                for (int i = 0; i < left.Length; i++)
                {
                    if (!Equals(left[i], right[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            // FIXME: Should return custom hashcode with arrays + cons pairs.
            public override int GetHashCode(object o) =>
                EqualityComparer<object>.Default.GetHashCode(o);

            public new static LispEqualityComparer Default { get; } =
                new LispEqualityComparer();
        }
    }
}
