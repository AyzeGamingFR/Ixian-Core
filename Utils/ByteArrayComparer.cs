﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace IXICore.Utils
{
    public class _ByteArrayComparer
    {
        public static int Compare(byte[] x, byte[] y)
        {
            var len = Math.Min(x.Length, y.Length);
            for (var i = 0; i < len; i++)
            {
                var c = x[i].CompareTo(y[i]);
                if (c != 0)
                {
                    return c;
                }
            }

            return x.Length.CompareTo(y.Length);
        }

    }

    public class ByteArrayComparer : IComparer<byte[]>, IEqualityComparer<byte[]>
    {
        public int Compare(byte[] x, byte[] y)
        {
            return _ByteArrayComparer.Compare(x, y);
        }
        public bool Equals(byte[] left, byte[] right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            if (left.Length != right.Length)
            {
                return false;
            }
            return left.SequenceEqual(right);
        }
        public int GetHashCode(byte[] key)
        {
            if (key == null)
            {
                return -1;
            }
            int value = key.Length;
            if (value >= 4)
            {
                return BitConverter.ToInt32(key, value - 4); // take last 4 bytes
            }
            foreach (var b in key)
            {
                value <<= 8;
                value += b;
            }
            return value;
        }
    }
}
