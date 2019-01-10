﻿using System.Collections.Generic;
using System.Linq;

namespace Our.Umbraco.Look.Extensions
{
    internal static partial class IEnumerableExtensions
    {
        /// <summary>
        /// Returns true if both collection have the same elements (regardless of sequence)
        /// https://stackoverflow.com/questions/3669970/compare-two-listt-objects-for-equality-ignoring-order
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <param name="first"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        internal static bool ElementsEqual<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second)
        {
            if (first.Count() != second.Count()) return false;

            Stack<TSource> stack = new Stack<TSource>(first);
            List<TSource> list = new List<TSource>(second);

            var areEqual = true;

            do
            {
                var element = stack.Pop();

                if (!list.Contains(element))
                {
                    areEqual = false;
                }
                else
                {
                    list.Remove(element);
                }
            }
            while (areEqual && stack.Any());

            return areEqual;
        }
    }
}