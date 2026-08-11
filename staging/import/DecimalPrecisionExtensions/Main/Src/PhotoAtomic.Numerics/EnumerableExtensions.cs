namespace PhotoAtomic.Numerics
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// extends the IEnumerable interface
    /// </summary>
    public static class EnumerableExtensions
    {
        /// <summary>
        /// returns a sequence of at least the specified size, if list is not long enough default values are provided before the values of the list
        /// </summary>
        /// <typeparam name="T">the type of the items in list</typeparam>
        /// <param name="list">the list of values</param>
        /// <param name="size">the minimum lenght of the required sequence</param>
        /// <returns>a sequence of element long at least the specified size, possibly filled with default(T) if not long enough</returns>
        public static IEnumerable<T> LeftFill<T>(this IEnumerable<T> list, int size)
        {
            return Enumerable.Repeat<T>(default(T), size - list.Count()).Concat(list);            
        }

        /// <summary>
        /// returns a sequence of at least the specified size, if list is not long enough default values are provided after the values of the list
        /// </summary>
        /// <typeparam name="T">the type of the items in list</typeparam>
        /// <param name="list">the list of values</param>
        /// <param name="size">the minimum lenght of the required sequence</param>
        /// <returns>a sequence of element long at least the specified size, possibly filled with default(T) if not long enough</returns>
        public static IEnumerable<T> RightFill<T>(this IEnumerable<T> list, int size)
        {
            return list.Concat(Enumerable.Repeat<T>(default(T), size - list.Count()));
        }

        /// <summary>
        /// split in chuncks of the specified size the given sequence
        /// </summary>
        /// <typeparam name="T">the type of elements in the list</typeparam>
        /// <param name="enumerable">the list to broke in chunks</param>
        /// <param name="chunkSize">the maximum size of each chunk</param>
        /// <returns>group of item of the specified lenght</returns>
        public static IEnumerable<IGrouping<int, T>> Chunk<T>(this IEnumerable<T> enumerable, int chunkSize)
        {
            int i = 0;
            return enumerable.GroupBy(x => (int)Math.Floor((i++) / (double)chunkSize));
        }
    }
}
