using System.Collections.Generic;

namespace FaceLocker.Extensions
{
    public static class ListExtensions
    {
        /// <summary>
        /// 向列表中添加多个元素
        /// </summary>
        public static void AddRange<T>(this List<T> list, IEnumerable<T> items)
        {
            if (list == null || items == null) return;

            foreach (var item in items)
            {
                list.Add(item);
            }
        }
    }
}
