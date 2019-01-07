using System;
using System.Collections.Generic;
using System.Linq;

namespace ZwaveExperiments
{
    public static class Debugging
    {
        public static IEnumerable<IEnumerable<TSource>> Batch<TSource>(this IEnumerable<TSource> source, int size)
        {
            TSource[] bucket = null;
            var count = 0;

            foreach (var item in source)
            {
                if (bucket == null)
                    bucket = new TSource[size];

                bucket[count++] = item;
                if (count != size)
                    continue;

                yield return bucket;

                bucket = null;
                count = 0;
            }

            if (bucket != null && count > 0)
                yield return bucket.Take(count);
        }

        public static void BinaryDump(this byte[] bytes, string description = null, int bytesPerLine = 8)
        {
            ((ReadOnlySpan<byte>) bytes).BinaryDump(description, bytesPerLine);
        }

        public static void BinaryDump(this Span<byte> bytes, string description = null, int bytesPerLine = 8)
        {
            ((ReadOnlySpan<byte>) bytes).BinaryDump(description, bytesPerLine);
        }

        public static void BinaryDump(this ReadOnlySpan<byte> bytes, string description = null, int bytesPerLine = 8)
        {
            var hexLength = bytesPerLine * 3 - 1;
            var lines = bytes.ToArray().Batch(bytesPerLine).Select(b =>
            {
                var hex = string.Join(" ", b.Select(x => x.ToString("X2")));
                hex += new string(' ', hexLength - hex.Length);
                var ascii = string.Join("", b.Select(x => x < 0x20 || x == 255 ? "." : new string((char) x, 1)));
                return hex + "\t" + ascii;
            });

            //var pre = new XElement("pre", String.Join("\n", lines));
            //pre.SetAttributeValue("style", "font-family: monospace");
            //Util.RawHtml(pre).Dump(description);
            if (description != null)
            {
                Console.WriteLine(description);
            }

            Console.WriteLine(string.Join("\n", lines.Select(x => "\t" + x)));
        }
    }
}