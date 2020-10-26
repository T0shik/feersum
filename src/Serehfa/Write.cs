namespace Serehfa
{
    using System;
    using static ArgHelpers;

    public static class Write
    {
        [LispBuiltin("newline")]
        public static object Newline(object[] args)
        {
            CheckNoArgs(args);

            Console.WriteLine();

            return Undefined.Instance;
        }

        /// <summary>
        /// Display builtin. This is intended for user-readable output rather than
        /// any machine readable round tripping. Printing out strings &amp; chars should
        /// display their raw form. All other objects is up to the implementation.
        /// 
        /// This implementation calls `ToString` on the underlying .NET object and
        /// uses that directly.
        /// </summary>
        [LispBuiltin("display")]
        public static object Display(object[] args)
        {
            var obj = UnpackArgs<object>(args);

            var repr = obj == null ?
                "'()" : obj.ToString();

            Console.Write(repr);

            return Undefined.Instance;
        }
    }
}